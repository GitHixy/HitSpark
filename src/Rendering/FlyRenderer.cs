using System;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using HitSpark.Capture;
using HitSpark.Models;
using HitSpark.Services;

namespace HitSpark.Rendering;

/// <summary>Per-frame inputs shared by every instance.</summary>
public struct RenderContext
{
    public double Now;
    public float DeltaTime;
    /// <summary>Global size multiplier: user scale times screen-resolution factor.</summary>
    public float Scale;
    public float Opacity;
    public bool ReducedMotion;
    public int MaxLive;
    /// <summary>False while the game UI is hidden: instances age but nothing is drawn.</summary>
    public bool Visible;
    public Vector2 ClipMin;
    public Vector2 ClipMax;
}

/// <summary>
/// Spawns, animates and draws combat text on an ImGui draw list. One instance renders into the
/// world (background draw list, actor anchors); another renders the settings preview.
/// All state lives in fixed pooled arrays, so a frame allocates nothing.
/// </summary>
public sealed unsafe class FlyRenderer
{
    public const int Capacity = 512;
    private const int ParticleCapacity = 1024;
    private const int QueueCapacity = 512;
    private const int ObjectSlots = 819;

    private static readonly Vector2[] OutlineDirs8 =
    [
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(0.7071f, 0.7071f), new(-0.7071f, 0.7071f), new(0.7071f, -0.7071f), new(-0.7071f, -0.7071f),
    ];

    private readonly FlyInstance[] pool = new FlyInstance[Capacity];
    private readonly Particle[] particles = new Particle[ParticleCapacity];
    private readonly bool worldSpace;

    // Spawn requests arrive from game hooks; a lock keeps the handoff safe whatever thread draws.
    private readonly SpawnRequest[] queue = new SpawnRequest[QueueCapacity];
    private readonly object queueGate = new();
    private int queueHead;
    private int queueCount;

    // Actor positions resolved once per frame, however many numbers share the actor.
    private readonly int[] anchorFrame = new int[ObjectSlots];
    private readonly Vector3[] anchorWorld = new Vector3[ObjectSlots];
    private readonly bool[] anchorValid = new bool[ObjectSlots];
    private int frame;

    private uint rng = 0x9E3779B9;
    private int alternate = 1;
    private readonly Stopwatch timer = new();

    public CompiledPreset? Preset { get; set; }

    /// <summary>Log how each new number's anchor resolves (driven by the event recorder).</summary>
    public bool Trace { get; set; }
    private int traceBudget;
    public FontService Fonts { get; }

    // Diagnostics
    public int LiveCount { get; private set; }
    public int PeakLive { get; private set; }
    public double LastFrameMs { get; private set; }
    public int Dropped { get; private set; }
    public long Spawned { get; private set; }
    public int Queued
    {
        get
        {
            lock (queueGate)
                return queueCount;
        }
    }

    public FlyRenderer(FontService fonts, bool worldSpace)
    {
        Fonts = fonts;
        this.worldSpace = worldSpace;
    }

    // ------------------------------------------------------------------
    // Spawning
    // ------------------------------------------------------------------

    /// <summary>Queues text for the next frame. Safe to call from any thread.</summary>
    public void Enqueue(in SpawnRequest request)
    {
        lock (queueGate)
        {
            if (queueCount == QueueCapacity)
            {
                Dropped++;
                return;
            }

            queue[(queueHead + queueCount) % QueueCapacity] = request;
            queueCount++;
        }
    }

    public void Clear()
    {
        lock (queueGate)
        {
            queueCount = 0;
        }

        for (var i = 0; i < pool.Length; i++) pool[i].Active = false;
        for (var i = 0; i < particles.Length; i++) particles[i].Active = false;
        LiveCount = 0;
    }

    private void DrainQueue(in RenderContext ctx)
    {
        while (true)
        {
            SpawnRequest request;
            lock (queueGate)
            {
                if (queueCount == 0) return;
                request = queue[queueHead];
                queueHead = (queueHead + 1) % QueueCapacity;
                queueCount--;
            }

            Spawn(request, ctx);
        }
    }

    private void Spawn(in SpawnRequest r, in RenderContext ctx)
    {
        var preset = Preset;
        if (preset is null) return;

        var style = preset[r.Category];
        var now = ctx.Now;

        // Aggregation: fold into a live instance of the same key if it is still accepting hits.
        var key = (style.AggregateKey == AggregateKey.Target ? r.AggregateTargetKey : r.AggregateActionKey) ^ (ulong)r.Involvement << 48;
        if (style.Aggregate && r.HasNumber)
        {
            for (var i = 0; i < pool.Length; i++)
            {
                ref var live = ref pool[i];
                if (!live.Active || live.AggregateKey != key || live.Style != style || now >= live.ReleaseTime) continue;

                live.Value += r.Value;
                live.Hits++;
                live.LastHit = now;
                live.ReleaseTime = now + style.AggregateWindow;
                live.PunchStart = now;
                return;
            }
        }

        var slot = AcquireSlot(ctx.MaxLive, style.Priority);
        if (slot < 0)
        {
            Dropped++;
            return;
        }

        Spawned++;
        ref var inst = ref pool[slot];
        inst = default;
        inst.Active = true;
        inst.Style = style;
        inst.SpawnTime = now;
        inst.LastHit = now;
        inst.PunchStart = now;
        inst.ReleaseTime = style.Aggregate ? now + style.AggregateWindow : now;
        inst.HoldAge = Math.Min(style.PopTime + 0.08f, style.Duration * 0.3f);
        inst.AggregateKey = style.Aggregate ? key : 0;
        inst.Hits = 1;

        inst.ActorEntityId = r.ActorEntityId;
        inst.ActorIndex = r.ActorIndex;
        inst.UseScreenAnchor = r.UseScreenAnchor;
        inst.ScreenActor = r.ScreenActor;
        inst.ScreenSelf = r.ScreenSelf;

        inst.HasNumber = r.HasNumber;
        inst.Value = r.Value;
        inst.Displayed = r.Value;
        inst.Shown = r.Value;
        inst.Word = r.Word;
        inst.Unit = r.Unit;
        inst.Name = r.Name;
        inst.IconId = r.IconId;
        inst.DamageKind = r.DamageKind;
        inst.SizeScale = r.SizeScale > 0f ? r.SizeScale : 1f;
        inst.WordFallback = NeedsFallback(style.Font, r.Word) || NeedsFallback(style.Font, r.Unit);
        inst.NameFallback = NeedsFallback(style.Font, r.Name);

        // Motion randomness
        inst.Rand = NextFloat();
        inst.Dir = style.ArcDirection switch
        {
            ArcDirection.Left => -1f,
            ArcDirection.Right => 1f,
            ArcDirection.Alternate => alternate = -alternate,
            _ => NextFloat() < 0.5f ? -1f : 1f,
        };
        inst.Jitter = new Vector2((NextFloat() - 0.5f) * style.Spread, (NextFloat() - 0.5f) * style.Spread * 0.35f);
        inst.Tilt = (NextFloat() * 2f - 1f) * style.TiltRadians;
        inst.AnchorKey = r.UseScreenAnchor ? 1000 + (int)style.Anchor : r.ActorIndex * 8 + (int)style.Anchor;
        inst.Slot = PickLane(inst.AnchorKey, now, slot);
        if (inst.Slot % 2 == 1 && style.Trajectory is Trajectory.Arc or Trajectory.Fountain)
            inst.Dir = -inst.Dir;

        inst.SparksPending = style.Sparks && !ctx.ReducedMotion;
        ComposeText(ref inst);
    }

    /// <summary>
    /// A free pool slot, or the least important, oldest live instance when the cap is reached.
    /// </summary>
    private int AcquireSlot(int maxLive, int priority)
    {
        var live = 0;
        var free = -1;
        var victim = -1;
        var victimPriority = int.MaxValue;
        var victimAge = double.MaxValue;

        for (var i = 0; i < pool.Length; i++)
        {
            ref var p = ref pool[i];
            if (!p.Active)
            {
                if (free < 0) free = i;
                continue;
            }

            live++;
            var pr = p.Style.Priority;
            if (pr < victimPriority || (pr == victimPriority && p.SpawnTime < victimAge))
            {
                victim = i;
                victimPriority = pr;
                victimAge = p.SpawnTime;
            }
        }

        if (live < Math.Min(maxLive, Capacity) && free >= 0) return free;
        if (victim >= 0 && victimPriority <= priority) return victim;
        return -1;
    }

    /// <summary>
    /// Numbers on the same anchor spawned close together take different lanes, so a burst
    /// fans out instead of stacking into one unreadable blob.
    /// </summary>
    private int PickLane(int anchorKey, double now, int self)
    {
        var used = 0;
        for (var i = 0; i < pool.Length; i++)
        {
            ref var p = ref pool[i];
            if (!p.Active || i == self || p.AnchorKey != anchorKey) continue;
            if (now - p.SpawnTime > p.Style.Duration * 0.45f) continue;
            if (p.Slot < 8) used |= 1 << p.Slot;
        }

        for (var s = 0; s < 8; s++)
            if ((used & (1 << s)) == 0)
                return s;
        return (int)(NextFloat() * 8f);
    }

    private static bool NeedsFallback(FontId font, string? text)
    {
        if (text is null || !FontService.IsLatinOnly(font)) return false;
        foreach (var c in text)
            if (c > 0x17F && c is < (char)0x2013 or > (char)0x2026)
                return true;
        return false;
    }

    private static void ComposeText(ref FlyInstance inst)
    {
        Span<char> buf = inst.Text;
        var n = 0;
        var s = inst.Style;

        if (inst.HasNumber)
        {
            if (inst.Word is not null)
            {
                n = Append(buf, n, inst.Word);
                n = Append(buf, n, " +");
            }

            n = Append(buf, n, s.Prefix);
            n += NumberFormatter.Write(buf[n..], inst.Shown, s.Separators, s.Abbreviate, s.AbbreviateFrom);
            n = Append(buf, n, s.Suffix);
            n = Append(buf, n, inst.Unit);
        }
        else
        {
            n = Append(buf, n, s.Prefix);
            n = Append(buf, n, inst.Word);
            n = Append(buf, n, inst.Unit);
            n = Append(buf, n, s.Suffix);
        }

        inst.TextLength = n;
    }

    private static int Append(Span<char> buf, int at, string? text)
    {
        if (string.IsNullOrEmpty(text)) return at;
        var count = Math.Min(text.Length, buf.Length - at);
        text.AsSpan(0, count).CopyTo(buf[at..]);
        return at + count;
    }

    // ------------------------------------------------------------------
    // Frame
    // ------------------------------------------------------------------

    public void Draw(ImDrawListPtr dl, in RenderContext ctx)
    {
        timer.Restart();
        frame++;

        DrainQueue(ctx);

        if (ctx.Visible)
            dl.PushClipRect(ctx.ClipMin, ctx.ClipMax, true);

        var live = 0;
        for (var i = 0; i < pool.Length; i++)
        {
            ref var inst = ref pool[i];
            if (!inst.Active) continue;

            if (!UpdateAndDraw(dl, ref inst, ctx))
            {
                inst.Active = false;
                inst.Style = null!;
                inst.Word = inst.Unit = inst.Name = null;
                continue;
            }

            live++;
        }

        DrawParticles(dl, ctx);

        if (ctx.Visible)
            dl.PopClipRect();

        LiveCount = live;
        PeakLive = Math.Max(PeakLive, live);
        LastFrameMs = timer.Elapsed.TotalMilliseconds;
    }

    /// <summary>Advances one instance and draws it. Returns false once it has expired.</summary>
    private bool UpdateAndDraw(ImDrawListPtr dl, ref FlyInstance inst, in RenderContext ctx)
    {
        var s = inst.Style;
        var now = ctx.Now;
        var age = (float)(now - inst.SpawnTime);

        // Aggregated numbers freeze just after their pop while hits keep landing, then play out.
        var motionAge = age;
        if (s.Aggregate)
        {
            if (now < inst.ReleaseTime)
            {
                motionAge = Math.Min(age, inst.HoldAge);
            }
            else
            {
                var ageAtRelease = (float)(inst.ReleaseTime - inst.SpawnTime);
                motionAge = Math.Min(ageAtRelease, inst.HoldAge) + (float)(now - inst.ReleaseTime);
            }
        }

        if (motionAge >= s.Duration) return false;

        // Rolling value for aggregated numbers.
        if (inst.HasNumber && inst.Displayed != inst.Value)
        {
            inst.Displayed += (inst.Value - inst.Displayed) * Math.Min(1.0, ctx.DeltaTime * 14.0);
            if (Math.Abs(inst.Value - inst.Displayed) < 0.5) inst.Displayed = inst.Value;
            var shown = (long)Math.Round(inst.Displayed);
            if (shown != inst.Shown)
            {
                inst.Shown = shown;
                ComposeText(ref inst);
            }
        }

        if (!ctx.Visible) return true;

        var resolved = ResolveAnchor(ref inst, ctx, out var anchor);
        if (Trace && !inst.Traced)
            TraceAnchor(ref inst, resolved, anchor);

        if (!resolved) return worldSpace ? inst.HasWorld : true;

        // Stepped motion (retro look) samples the timeline at a fixed rate.
        var sampleAge = s.StepRate > 0f ? MathF.Floor(motionAge * s.StepRate) / s.StepRate : motionAge;
        var t = sampleAge / s.Duration;

        // Smaller numbers (other players) also travel proportionally less.
        var unit = ctx.Scale * inst.SizeScale;
        var offset = PathOffset(ref inst, s, t, ctx.ReducedMotion) + s.Offset;
        var pos = anchor + offset * unit;

        // Scale: pop-in, punch on impact, shrink toward EndScale during the fade.
        var scale = 1f;
        if (s.PopTime > 0f && sampleAge < s.PopTime)
            scale = s.PopScale + (1f - s.PopScale) * Ease.Apply(s.PopEasing, sampleAge / s.PopTime);

        var sinceHit = (float)(now - inst.PunchStart);
        if (s.Punch)
            scale *= 1f + (s.PunchScale - 1f) * Ease.Punch(sinceHit / s.PunchTime) * (ctx.ReducedMotion ? 0.4f : 1f);

        var alpha = 1f;
        if (s.FadeInTime > 0f && motionAge < s.FadeInTime)
            alpha = motionAge / s.FadeInTime;

        var remaining = s.Duration - motionAge;
        if (remaining < s.FadeOutTime)
        {
            var u = 1f - remaining / s.FadeOutTime;
            alpha *= 1f - u * u;
            scale *= 1f + (s.EndScale - 1f) * u;
        }

        alpha *= ctx.Opacity;
        if (alpha <= 0.003f || scale <= 0.01f) return true;

        if (s.Shake && !ctx.ReducedMotion && sinceHit < s.ShakeTime)
        {
            var u = 1f - sinceHit / s.ShakeTime;
            var amp = s.ShakeAmount * u * u * unit;
            var phase = (float)now;
            pos += new Vector2(MathF.Sin(phase * 83f + inst.Rand * 40f), MathF.Cos(phase * 71f + inst.Rand * 17f)) * amp;
        }

        var rotation = inst.Tilt;
        if (s.Trajectory == Trajectory.Arc && !ctx.ReducedMotion)
            rotation += inst.Dir * 0.16f * Ease.Apply(s.PathEasing, t);

        var flash = s.Flash ? Math.Max(0f, 1f - sinceHit / s.FlashTime) : 0f;

        // Ghost trail: sample the position ~60 times a second.
        if (s.Ghost && !ctx.ReducedMotion && now - inst.GhostLastSample >= 1.0 / 60.0)
        {
            inst.Ghosts[inst.GhostHead] = pos;
            inst.GhostHead = (inst.GhostHead + 1) % FlyInstance.MaxGhosts;
            inst.GhostFilled = Math.Min(inst.GhostFilled + 1, FlyInstance.MaxGhosts);
            inst.GhostLastSample = now;
        }

        DrawInstance(dl, ref inst, pos, scale * unit, alpha, rotation, flash, ctx);

        if (inst.SparksPending)
        {
            inst.SparksPending = false;
            EmitSparks(pos, s, unit);
        }

        return true;
    }

    /// <summary>Offset along the style's trajectory at normalised time t, in unscaled pixels.</summary>
    private static Vector2 PathOffset(ref FlyInstance inst, CompiledStyle s, float t, bool reducedMotion)
    {
        var p = Ease.Apply(s.PathEasing, t);
        var d = s.Distance;
        var w = s.Spread;
        var lineHeight = s.Size * 0.85f;
        var trajectory = s.Trajectory;

        if (reducedMotion && trajectory is Trajectory.Arc or Trajectory.Fountain or Trajectory.Fall)
        {
            trajectory = Trajectory.Rise;
            d *= 0.4f;
        }

        switch (trajectory)
        {
            case Trajectory.Rise:
                return new Vector2(inst.Jitter.X, -d * p + inst.Slot * lineHeight);

            case Trajectory.Fall:
                return new Vector2(inst.Jitter.X + inst.Dir * w * 0.25f * p, d * p - inst.Slot * lineHeight * 0.6f);

            case Trajectory.Arc:
                // Up and over: peaks a little past halfway, lands above where it started.
                return new Vector2(inst.Dir * w * p, -d * (2.2f * p - 2.0f * p * p) - inst.Slot * lineHeight * 0.35f);

            case Trajectory.Fountain:
            {
                // Ballistic: constant sideways speed, gravity pulls it below its launch point.
                var reach = w * (0.45f + 0.55f * inst.Rand);
                return new Vector2(inst.Dir * reach * t, -d * (2.2f * t - 2.6f * t * t) - inst.Slot * lineHeight * 0.25f);
            }

            case Trajectory.Lane:
                return new Vector2(0f, -d * t + inst.Slot * lineHeight);

            default:
                return new Vector2(inst.Jitter.X, inst.Jitter.Y + inst.Slot * lineHeight);
        }
    }

    // ------------------------------------------------------------------
    // Anchors
    // ------------------------------------------------------------------

    private bool ResolveAnchor(ref FlyInstance inst, in RenderContext ctx, out Vector2 screen)
    {
        var s = inst.Style;
        var laneOffset = s.Anchor switch
        {
            AnchorMode.SelfLeft => new Vector2(-170f, -10f) * ctx.Scale,
            AnchorMode.SelfRight => new Vector2(170f, -10f) * ctx.Scale,
            _ => Vector2.Zero,
        };

        if (inst.UseScreenAnchor)
        {
            screen = (s.Anchor == AnchorMode.Actor ? inst.ScreenActor : inst.ScreenSelf) + laneOffset;
            return true;
        }

        screen = default;
        Vector3 world;
        if (s.Anchor == AnchorMode.Actor && inst.ActorIndex != SpawnRequest.NoActor)
        {
            if (TryActorWorld(inst.ActorIndex, inst.ActorEntityId, out world))
            {
                inst.LastWorld = world;
                inst.HasWorld = true;
            }
            else if (inst.HasWorld)
            {
                // The actor despawned or died out of the table: finish where it last stood.
                world = inst.LastWorld;
            }
            else
            {
                return false;
            }
        }
        else
        {
            if (!TryActorWorld(0, 0, out world)) return false;
            inst.LastWorld = world;
            inst.HasWorld = true;
        }

        var selfAnchored = s.Anchor != AnchorMode.Actor;
        if (!Plugin.GameGui.WorldToScreen(world, out screen, out var inView))
        {
            if (!selfAnchored) return false;
        }

        if (!inView && !selfAnchored) return false;

        screen += laneOffset;
        if (selfAnchored)
            screen = Vector2.Clamp(screen, ctx.ClipMin + new Vector2(60f, 60f), ctx.ClipMax - new Vector2(60f, 60f));
        return true;
    }

    /// <summary>One line per new number: which actor, where it is, and whether it projected on screen.</summary>
    private void TraceAnchor(ref FlyInstance inst, bool resolved, Vector2 screen)
    {
        inst.Traced = true;
        if (traceBudget >= 60) return;
        traceBudget++;

        var found = TryActorWorld(inst.ActorIndex == SpawnRequest.NoActor ? (ushort)0 : inst.ActorIndex,
            inst.ActorIndex == SpawnRequest.NoActor ? 0 : inst.ActorEntityId, out var world);
        var onScreen = Plugin.GameGui.WorldToScreen(world, out var projected, out var inView);

        // Font state too: a face that failed to (re)build would draw nothing without any error.
        var fontId = inst.WordFallback ? FontId.Interface : inst.Style.Font;
        var font = Fonts.Get(fontId);
        Plugin.Log.Debug(
            "[anchor] {Category} anchor={Anchor} actor=#{Index} id={Id:X} found={Found} world=({X:0.0},{Y:0.0},{Z:0.0}) " +
            "w2s={OnScreen} inView={InView} projected=({PX:0},{PY:0}) resolved={Resolved} screen=({SX:0},{SY:0}) text=\"{Text}\" " +
            "font={Font} ready={Ready} fontSize={FontSize:0.0} glyphs={Glyphs}",
            inst.Style.Category, inst.Style.Anchor, inst.ActorIndex, inst.ActorEntityId, found, world.X, world.Y, world.Z,
            onScreen, inView, projected.X, projected.Y, resolved, screen.X, screen.Y, new string(inst.Text[..inst.TextLength]),
            fontId, Fonts.IsReady(fontId), font.FontSize, font.Glyphs.Size);
    }

    /// <summary>Allows another batch of anchor traces, when the recorder is switched on again.</summary>
    public void ResetTrace() => traceBudget = 0;

    /// <summary>
    /// World position of the top of an actor, cached per frame. Index 0 is always the local
    /// player; an entity id of 0 skips the identity check.
    /// </summary>
    private bool TryActorWorld(ushort index, uint entityId, out Vector3 world)
    {
        world = default;
        if (index >= ObjectSlots) return false;

        if (anchorFrame[index] == frame)
        {
            world = anchorWorld[index];
            return anchorValid[index];
        }

        anchorFrame[index] = frame;
        anchorValid[index] = false;

        var manager = GameObjectManager.Instance();
        if (manager == null) return false;

        var obj = manager->Objects.IndexSorted[index].Value;
        if (obj == null || (entityId != 0 && obj->EntityId != entityId)) return false;

        // Hitbox height is huge on some bosses; keep numbers near the body, not in the sky.
        var height = Math.Clamp(obj->Height, 1.2f, 3.4f);
        var position = obj->Position;
        world = new Vector3(position.X, position.Y + height * 0.9f, position.Z);
        anchorWorld[index] = world;
        anchorValid[index] = true;
        return true;
    }

    // ------------------------------------------------------------------
    // Drawing
    // ------------------------------------------------------------------

    private void DrawInstance(ImDrawListPtr dl, ref FlyInstance inst, Vector2 center, float scale, float alpha, float rotation, float flash, in RenderContext ctx)
    {
        var s = inst.Style;
        var preset = Preset!;

        var font = Fonts.Get(inst.WordFallback ? FontId.Interface : s.Font);
        var size = s.Size * scale;
        ReadOnlySpan<char> text = inst.Text[..inst.TextLength];
        var width = Measure(font, size, text, s.LetterSpacing * scale);

        var fill = s.Color;
        if (s.Tint == TintMode.DamageType)
        {
            fill = inst.DamageKind switch
            {
                DamageKind.Physical => preset.Physical,
                DamageKind.Magical => preset.Magical,
                DamageKind.Unique => preset.Unique,
                _ => fill,
            };
        }

        // Icon beside the number, sized to the text.
        var iconSize = inst.IconId != 0 && s.Icon != IconMode.None ? size * s.IconScale : 0f;
        var gap = iconSize > 0f ? size * 0.12f : 0f;
        var total = width + iconSize + gap;
        var left = center.X - total * 0.5f;
        var textX = s.Icon == IconMode.Left ? left + iconSize + gap : left;
        var origin = new Vector2(textX, center.Y - size * 0.5f);

        var vtxStart = dl.VtxBuffer.Size;

        // Ghost trail first so it sits behind the live number.
        if (s.Ghost && inst.GhostFilled > 1 && !ctx.ReducedMotion)
        {
            var count = Math.Min(s.GhostCount, inst.GhostFilled - 1);
            for (var k = count; k >= 1; k--)
            {
                var idx = (inst.GhostHead - 1 - k + FlyInstance.MaxGhosts * 2) % FlyInstance.MaxGhosts;
                var ghostCenter = inst.Ghosts[idx];
                var ghostOrigin = origin + (ghostCenter - center);
                var a = alpha * 0.28f * (1f - k / (float)(count + 1));
                DrawText(dl, font, size, ghostOrigin, text, Pack(fill, a), s.LetterSpacing * scale);
            }
        }

        if (s.Shadow)
            DrawText(dl, font, size, origin + s.ShadowOffset * scale, text, MulAlpha(s.ShadowColor, alpha), s.LetterSpacing * scale);

        if (s.OutlineWidth > 0f)
        {
            var ow = s.OutlineWidth * Math.Max(scale, 0.6f);
            var outline = MulAlpha(s.OutlineColor, alpha);
            var dirs = ow >= 1.5f ? 8 : 4;
            for (var d = 0; d < dirs; d++)
                DrawText(dl, font, size, origin + OutlineDirs8[d] * ow, text, outline, s.LetterSpacing * scale);
        }

        var fillStart = dl.VtxBuffer.Size;
        DrawText(dl, font, size, origin, text, Pack(fill, alpha), s.LetterSpacing * scale);
        if (s.Gradient)
        {
            ImGuiP.ShadeVertsLinearColorGradientKeepAlpha(dl, fillStart, dl.VtxBuffer.Size,
                origin, origin + new Vector2(0f, size),
                Pack(fill, 1f), Pack(s.GradientColor, 1f));
        }

        if (flash > 0f)
            DrawText(dl, font, size, origin, text, Pack(Vector4.One, alpha * flash * 0.9f), s.LetterSpacing * scale);

        if (iconSize > 0f)
        {
            var wrap = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(inst.IconId)).GetWrapOrEmpty();
            var iconX = s.Icon == IconMode.Left ? left : left + width + gap;
            var min = new Vector2(iconX, center.Y - iconSize * 0.5f);
            dl.AddImage(wrap.Handle, min, min + new Vector2(iconSize, iconSize), Vector2.Zero, Vector2.One, Pack(Vector4.One, alpha));
        }

        // Caption line: the style's fixed label ("DoT Tick") wins over the action name.
        var caption = s.Label.Length > 0 ? s.Label : s.ShowName ? inst.Name : null;
        if (!string.IsNullOrEmpty(caption))
        {
            var fallback = s.Label.Length > 0 ? s.LabelFallback : inst.NameFallback;
            var nameFont = Fonts.Get(fallback ? FontId.Interface : s.Font);
            var nameSize = size * s.NameScale;
            var nameWidth = Measure(nameFont, nameSize, caption, 0f);
            var nameOrigin = new Vector2(center.X - nameWidth * 0.5f, center.Y + size * 0.42f);
            var nameAlpha = alpha * 0.92f;

            if (s.OutlineWidth > 0f)
            {
                var ow = Math.Max(1f, s.OutlineWidth * 0.6f * Math.Max(scale, 0.6f));
                var outline = MulAlpha(s.OutlineColor, nameAlpha);
                for (var d = 0; d < 4; d++)
                    dl.AddText(nameFont, nameSize, nameOrigin + OutlineDirs8[d] * ow, outline, caption);
            }

            dl.AddText(nameFont, nameSize, nameOrigin, Pack(Vector4.Lerp(fill, Vector4.One, 0.35f), nameAlpha), caption);
        }

        if (MathF.Abs(rotation) > 0.001f)
            Rotate(dl, vtxStart, center, rotation);
    }

    /// <summary>Draws a string, one call normally or glyph by glyph when letter spacing is set.</summary>
    private static void DrawText(ImDrawListPtr dl, ImFontPtr font, float size, Vector2 origin, ReadOnlySpan<char> text, uint color, float spacing)
    {
        if (spacing == 0f)
        {
            dl.AddText(font, size, origin, color, text);
            return;
        }

        var k = size / font.FontSize;
        var x = origin.X;
        for (var i = 0; i < text.Length; i++)
        {
            dl.AddText(font, size, new Vector2(x, origin.Y), color, text.Slice(i, 1));
            var glyph = font.FindGlyph(text[i]);
            if (glyph != null) x += glyph->AdvanceX * k + spacing;
        }
    }

    private static float Measure(ImFontPtr font, float size, ReadOnlySpan<char> text, float spacing)
    {
        if (text.IsEmpty) return 0f;

        var k = size / font.FontSize;
        var width = 0f;
        foreach (var c in text)
        {
            var glyph = font.FindGlyph(c);
            if (glyph != null) width += glyph->AdvanceX * k;
        }

        return width + spacing * (text.Length - 1);
    }

    /// <summary>Rotates every vertex emitted since <paramref name="start"/> around a pivot.</summary>
    private static void Rotate(ImDrawListPtr dl, int start, Vector2 pivot, float radians)
    {
        var (sin, cos) = MathF.SinCos(radians);
        ref var buffer = ref dl.VtxBuffer;
        var end = buffer.Size;
        var data = buffer.Data;
        for (var i = start; i < end; i++)
        {
            var p = data[i].Pos - pivot;
            data[i].Pos = new Vector2(p.X * cos - p.Y * sin, p.X * sin + p.Y * cos) + pivot;
        }
    }

    // ------------------------------------------------------------------
    // Particles
    // ------------------------------------------------------------------

    private void EmitSparks(Vector2 at, CompiledStyle s, float scale)
    {
        var remaining = s.SparkCount;
        for (var i = 0; i < particles.Length && remaining > 0; i++)
        {
            ref var p = ref particles[i];
            if (p.Active) continue;

            // Mostly upward fan, a few falling off the sides.
            var angle = -MathF.PI * 0.5f + (NextFloat() * 2f - 1f) * MathF.PI * 0.75f;
            var speed = s.SparkSpeed * (0.45f + NextFloat() * 0.75f) * scale;
            p.Active = true;
            p.Pos = at;
            p.Vel = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
            p.Age = 0f;
            p.Life = 0.32f + NextFloat() * 0.3f;
            p.Size = (1.6f + NextFloat() * 1.6f) * scale;
            p.Color = s.SparkColor;
            remaining--;
        }
    }

    private void DrawParticles(ImDrawListPtr dl, in RenderContext ctx)
    {
        var dt = ctx.DeltaTime;
        for (var i = 0; i < particles.Length; i++)
        {
            ref var p = ref particles[i];
            if (!p.Active) continue;

            p.Age += dt;
            if (p.Age >= p.Life)
            {
                p.Active = false;
                continue;
            }

            p.Vel.Y += 900f * ctx.Scale * dt;
            p.Vel *= 1f - Math.Min(1f, 2.6f * dt);
            p.Pos += p.Vel * dt;

            if (!ctx.Visible) continue;

            var life = 1f - p.Age / p.Life;
            var tail = p.Pos - p.Vel * 0.035f;
            dl.AddLine(tail, p.Pos, MulAlpha(p.Color, life * ctx.Opacity), p.Size * (0.4f + life * 0.6f));
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private float NextFloat()
    {
        // xorshift32: cheap, deterministic and allocation-free.
        rng ^= rng << 13;
        rng ^= rng >> 17;
        rng ^= rng << 5;
        return (rng & 0xFFFFFF) / (float)0x1000000;
    }

    private static uint Pack(Vector4 c, float alpha)
        => ImGui.ColorConvertFloat4ToU32(new Vector4(c.X, c.Y, c.Z, Math.Clamp(c.W * alpha, 0f, 1f)));

    private static uint MulAlpha(uint color, float alpha)
    {
        var a = (uint)Math.Clamp((color >> 24) * alpha, 0f, 255f);
        return (color & 0x00FFFFFF) | (a << 24);
    }
}
