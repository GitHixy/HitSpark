using System;
using System.Collections.Generic;
using System.Diagnostics;
using Dalamud.Game.Gui.FlyText;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.ImGuiNotification;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.STD;
using HitSpark.Models;
using HitSpark.Presets;
using HitSpark.Rendering;
using HitSpark.Services;

namespace HitSpark.Capture;

/// <summary>
/// Turns screen-log entries into spawn requests. For each entry it decides whether HitSpark
/// replaces, hides or leaves the native text, and it is the one place that knows when to
/// back off: native mode, a tripped circuit breaker, or a renderer that stopped drawing.
/// </summary>
public sealed unsafe class EventPipeline : IDisposable
{
    private const int BreakerLimit = 5;
    private static readonly TimeSpan BreakerWindow = TimeSpan.FromSeconds(60);
    private const double RendererTimeout = 2.0;

    private readonly ConfigurationService configService;
    private readonly PresetService presets;
    private readonly FlyRenderer renderer;
    private readonly MultiHitService multiHit;
    private readonly ScreenLogHooks hooks;
    private readonly PositionalTracker positionals;
    private readonly Queue<DateTime> recentErrors = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double lastRendererFrame = double.NegativeInfinity;

    // Counters for the Diagnostics page.
    public long Captured { get; private set; }
    public long Replaced { get; private set; }
    public long Hidden { get; private set; }
    public long TooFar { get; private set; }
    public long LeftNative { get; private set; }
    public long Errors { get; private set; }

    /// <summary>Set when the circuit breaker gave everything back to the game.</summary>
    public string? TripReason { get; private set; }
    public string? HookFailure => hooks.FailureReason;
    public bool HooksActive => hooks.IsActive;
    public bool TickHookActive => hooks.TickHookActive;
    public bool EffectHookActive => positionals.HookActive;

    public EventPipeline(ConfigurationService configService, PresetService presets, FlyRenderer renderer, MultiHitService multiHit)
    {
        this.configService = configService;
        this.presets = presets;
        this.renderer = renderer;
        this.multiHit = multiHit;

        hooks = new ScreenLogHooks(HandleEntry, OnHookError);
        if (!hooks.Enable())
        {
            Notify("HitSpark couldn't attach to the game's combat text, so the game's own numbers stay on. " +
                   "This usually means a game patch; check Settings → Diagnostics.");
        }

        positionals = new PositionalTracker(() => Config.LogEvents);
        Plugin.FlyTextGui.FlyTextCreated += OnFlyTextCreated;
    }

    private Configuration Config => configService.Configuration;

    /// <summary>The world renderer calls this every frame it draws.</summary>
    public void Heartbeat() => lastRendererFrame = clock.Elapsed.TotalSeconds;

    private bool RendererAlive => clock.Elapsed.TotalSeconds - lastRendererFrame < RendererTimeout;

    /// <summary>Clears a tripped breaker, for the button on the Diagnostics page.</summary>
    public void ResetBreaker()
    {
        TripReason = null;
        recentErrors.Clear();
        Plugin.Log.Info("Circuit breaker reset; HitSpark is replacing combat text again");
    }

    // ------------------------------------------------------------------
    // Entry handling
    // ------------------------------------------------------------------

    private bool HandleEntry(StdDeque<ScreenLogEntry>* queue, ScreenLogEntry* entry, in ScreenLogHooks.CallContext context, in ScreenLogHooks.TickContext tick)
    {
        Captured++;

        var kind = (FlyTextKind)entry->ScreenLogKind;
        var option = (ScreenLogOption)entry->Option;
        var category = EventClassifier.Classify(kind, option, entry->ActionId, tick.Valid, entry->SourceRelation, entry->TargetRelation);
        var style = presets.Compiled[category];

        var active = !Config.NativeMode && TripReason is null && RendererAlive;
        var involvement = EventClassifier.Involvement(entry->SourceRelation, entry->TargetRelation);

        // Your own actions, whatever happens to their text: an interruptible split stops when
        // the next one lands.
        var byYou = entry->SourceRelation == ScreenLogRelationKind.LocalPlayer && entry->ActionKind == 1;
        if (byYou)
            MultiHitService.NoteAction(renderer, entry->ActionId);

        // The game applies its own "display flying text / pop-up text" options after this point,
        // so HitSpark has to honour them itself: when the game wouldn't show it, neither do we.
        var isFlyText = entry->SourceRelation == ScreenLogRelationKind.LocalPlayer
                        || entry->TargetRelation == ScreenLogRelationKind.LocalPlayer;
        var gameShows = isFlyText ? GameDisplay.FlyText : GameDisplay.PopUpText;

        // Ticks name the afflicted actor as their own source, so the game's relations say "enemy
        // on enemy" even for your own DoT. Work out who really applied it.
        var tickSource = "-";
        uint tickStatus = 0;
        if (category is Category.DamageOverTime or Category.HealOverTime)
            involvement = AttributeTick(context, tick, category == Category.DamageOverTime, involvement, out tickStatus, out tickSource);

        // The text belongs to whoever it happened to: the target of the call, else the queue's owner.
        var actor = context.Valid && context.Target != null ? (GameObject*)context.Target : FindQueueOwner(queue, context);

        var damageKind = kind is FlyTextKind.Damage or FlyTextKind.DamageCrit or FlyTextKind.DamageDh or FlyTextKind.DamageCritDh;

        string decision;
        bool owned;
        if (!gameShows)
        {
            // Handed back untouched; the game then drops it, exactly as without HitSpark.
            LeftNative++;
            owned = false;
            decision = isFlyText ? "off in game (flying text)" : "off in game (pop-up text)";
        }
        else if (!active || style.Mode == CategoryMode.Native)
        {
            LeftNative++;
            owned = false;
            decision = active ? "native" : "native (inactive)";
        }
        else if (involvement != Involvement.You && IsTooFar(actor))
        {
            // Someone else's fight across the map (a FATE, another alliance): not worth a number.
            TooFar++;
            Hidden++;
            owned = true;
            decision = "too far";
        }
        else if (style.Mode == CategoryMode.Hide || (style.Show & involvement) == 0 || !Config.Allows(involvement))
        {
            Hidden++;
            owned = true;
            decision = "hidden";
        }
        else
        {
            var request = BuildRequest(entry, actor, kind, category);
            if (tickStatus != 0)
            {
                // Name and icon of the DoT/HoT itself ("Combust III"), for styles that show them.
                var status = GameSheets.Status(tickStatus);
                request.Name = status.Name.Length > 0 ? status.Name : null;
                request.IconId = status.Icon;
            }

            request.Involvement = involvement;
            request.SizeScale = Config.ScaleFor(involvement);

            // Multi-hit applies to your action damage on others, the same numbers MultiHit splits.
            var splittable = byYou && damageKind && entry->TargetRelation != ScreenLogRelationKind.LocalPlayer;
            if (!splittable || !multiHit.TrySplit(request, entry->ActionId, renderer))
                renderer.Enqueue(request);
            Replaced++;
            owned = true;
            decision = "replaced";
        }

        // Positionals ride along with your damage, whoever draws the number itself.
        if (gameShows && active && byYou && damageKind && actor != null && entry->TargetRelation != ScreenLogRelationKind.LocalPlayer)
            SpawnPositional(entry->ActionId, actor);

        if (Config.LogEvents)
            LogEntry(queue, entry, context, kind, category, involvement, decision, tickSource, tickStatus);

        return owned;
    }

    /// <summary>Whether the actor is beyond the "other players" distance limit from you.</summary>
    private bool IsTooFar(GameObject* actor)
    {
        var limit = Config.MaxDistance;
        if (limit <= 0f || actor == null) return false;

        var self = (GameObject*)(Plugin.ObjectTable.LocalPlayer?.Address ?? 0);
        if (self == null) return false;

        var dx = actor->Position.X - self->Position.X;
        var dy = actor->Position.Y - self->Position.Y;
        var dz = actor->Position.Z - self->Position.Z;
        return dx * dx + dy * dy + dz * dz > limit * limit;
    }

    private void SpawnPositional(uint actionId, GameObject* actor)
    {
        if (!PositionalTracker.IsPositional(actionId, out _)) return;
        if (!positionals.TryResolve(actionId, actor->EntityId, out var side, out var landed)) return;

        var category = landed ? Category.PositionalHit : Category.PositionalMiss;
        var style = presets.Compiled[category];
        if (style.Mode != CategoryMode.Replace || (style.Show & Involvement.You) == 0) return;

        var r = new SpawnRequest
        {
            Category = category,
            Word = PositionalWord(side, landed),
            ActorEntityId = actor->EntityId,
            ActorIndex = actor->ObjectIndex,
            Involvement = Involvement.You,
            SizeScale = 1f,
            Separate = true,
        };
        r.AggregateTargetKey = ((ulong)category << 56) ^ actor->EntityId;
        r.AggregateActionKey = r.AggregateTargetKey ^ ((ulong)actionId << 24);
        renderer.Enqueue(r);

        if (Config.LogEvents)
            Plugin.Log.Debug("[positional] {Action} on {Target}: {Side} {Result}", actionId, Describe(actor), side, landed ? "landed" : "missed");
    }

    public static string PositionalWord(PositionalSide side, bool landed) => (side, landed) switch
    {
        (PositionalSide.Rear, true) => "Rear",
        (PositionalSide.Flank, true) => "Flank",
        (PositionalSide.Rear, false) => "Rear missed",
        _ => "Flank missed",
    };

    private SpawnRequest BuildRequest(ScreenLogEntry* entry, GameObject* actor, FlyTextKind kind, Category category)
    {
        var r = new SpawnRequest
        {
            Category = category,
            ActorIndex = SpawnRequest.NoActor,
        };

        if (actor != null)
        {
            r.ActorEntityId = actor->EntityId;
            r.ActorIndex = actor->ObjectIndex;
        }

        var value1 = entry->Value1;
        var actionId = entry->ActionId;

        switch (category)
        {
            case Category.StatusGained:
            case Category.StatusLost:
            case Category.DebuffGained:
            case Category.DebuffLost:
            {
                // Status kinds carry the status id in the first value.
                var status = GameSheets.Status((uint)value1);
                r.Word = status.Name.Length > 0 ? status.Name : GameSheets.StatusWord;
                r.IconId = status.Icon;
                break;
            }

            case Category.Loot:
            {
                var item = GameSheets.Item(actionId);
                r.Word = item.Name.Length > 0 ? item.Name : GameSheets.ItemWord;
                r.IconId = item.Icon;
                if (value1 > 1) r.Unit = $" ×{value1}";
                break;
            }

            case Category.Crafting:
                r.HasNumber = true;
                r.Value = value1;
                r.Word = EventClassifier.CraftingLabel(kind);
                break;

            default:
            {
                var word = EventClassifier.WordFor(kind);
                if (word is not null)
                {
                    r.Word = word;
                }
                else if (kind == FlyTextKind.Named && actionId != 0)
                {
                    r.Word = GameSheets.Action(actionId).Name;
                }
                else
                {
                    r.HasNumber = true;
                    r.Value = value1;
                    r.Unit = EventClassifier.UnitFor(kind);
                }

                if (entry->ActionKind == 1 && actionId != 0)
                {
                    var action = GameSheets.Action(actionId);
                    r.Name = action.Name.Length > 0 ? action.Name : null;
                    r.IconId = action.Icon;
                }

                break;
            }
        }

        if (CategoryInfo.Group(category) is CategoryGroup.Outgoing or CategoryGroup.Incoming)
            r.DamageKind = entry->Value3 is >= 1 and <= 3 ? (DamageKind)entry->Value3 : DamageKind.None;

        var target = (ulong)r.ActorEntityId;
        r.AggregateTargetKey = ((ulong)category << 56) ^ target;
        r.AggregateActionKey = r.AggregateTargetKey ^ ((ulong)actionId << 24);
        return r;
    }

    /// <summary>
    /// Ground and channelled DoTs. They put no debuff on the enemy: the only status involved is a
    /// beneficial one on whoever placed them (Salted Earth, Doton, Slipstream, Flamethrower).
    /// </summary>
    private static readonly HashSet<uint> CasterSideDots = [749, 3036, 501, 3187, 2706, 3225, 1205, 1455];

    /// <summary>
    /// Who a DoT/HoT tick belongs to. Prefers the source the game passed while processing the
    /// tick; otherwise looks for a DoT (or HoT) on the target applied by you, then by your party.
    /// Only statuses that actually tick count: a Reprisal or Chain Stratagem never names a tick.
    /// </summary>
    private Involvement AttributeTick(in ScreenLogHooks.CallContext context, in ScreenLogHooks.TickContext tick,
        bool damage, Involvement fallback, out uint statusId, out string how)
    {
        statusId = 0;
        how = "relations";

        var localId = Plugin.ObjectTable.LocalPlayer?.EntityId ?? 0;
        if (localId == 0) return fallback;

        var manager = context.Valid && context.Target != null ? ((Character*)context.Target)->GetStatusManager() : null;

        if (tick.Valid && tick.SourceEntityId is not 0 and not 0xE0000000)
        {
            // The server usually leaves the status id out of the tick: if the source has exactly
            // one debuff on the target, that's the one ticking.
            statusId = tick.StatusId != 0 ? tick.StatusId : SoleTickingStatusFrom(manager, tick.SourceEntityId, damage);
            if (statusId == 0 && damage)
                statusId = CasterSideDotOf(tick.SourceEntityId);
            how = $"hook:{tick.SourceEntityId:X}";
            return InvolvementOf(tick.SourceEntityId, localId);
        }

        if (manager == null) return fallback;

        var best = fallback;
        for (var i = 0; i < 60; i++)
        {
            ref var status = ref manager->Status[i];
            if (status.StatusId == 0 || !GameSheets.Ticks(status.StatusId, damage)) continue;

            var who = InvolvementOf((uint)status.SourceObject.ObjectId, localId);
            if (who == Involvement.You)
            {
                statusId = status.StatusId;
                how = "status:you";
                return Involvement.You;
            }

            if (who == Involvement.Party && best != Involvement.Party)
            {
                best = Involvement.Party;
                statusId = status.StatusId;
                how = "status:party";
            }
        }

        return best;
    }

    /// <summary>
    /// The only DoT (or HoT) <paramref name="sourceId"/> has on this actor, or 0 if none or several:
    /// a Bard with both bites up can't be told apart from the tick alone.
    /// </summary>
    private uint SoleTickingStatusFrom(StatusManager* manager, uint sourceId, bool damage)
    {
        if (manager == null) return 0;

        uint found = 0;
        for (var i = 0; i < 60; i++)
        {
            ref var status = ref manager->Status[i];
            if (status.StatusId == 0 || (uint)status.SourceObject.ObjectId != sourceId) continue;
            if (!GameSheets.Ticks(status.StatusId, damage)) continue;
            if (found != 0) return 0;
            found = status.StatusId;
        }

        return found;
    }

    /// <summary>The ground or channelled DoT <paramref name="sourceId"/> is maintaining, or 0.</summary>
    private static uint CasterSideDotOf(uint sourceId)
    {
        var manager = GameObjectManager.Instance();
        if (manager == null) return 0;

        for (var i = 0; i < 200; i++)
        {
            var obj = manager->Objects.IndexSorted[i].Value;
            if (obj == null || obj->EntityId != sourceId) continue;
            if (!obj->IsCharacter()) return 0;

            var statuses = ((Character*)obj)->GetStatusManager();
            if (statuses == null) return 0;

            for (var s = 0; s < 60; s++)
            {
                var id = statuses->Status[s].StatusId;
                if (id != 0 && CasterSideDots.Contains(id)) return id;
            }

            return 0;
        }

        return 0;
    }

    /// <summary>You (or your pet), a party member, or someone else.</summary>
    private static Involvement InvolvementOf(uint entityId, uint localId)
    {
        if (entityId == localId) return Involvement.You;

        for (var i = 0; i < Plugin.PartyList.Length; i++)
        {
            if (Plugin.PartyList[i]?.EntityId == entityId)
                return Involvement.Party;
        }

        // Pets and summons count as their owner. Trust and duty support NPCs are "owned" by the
        // player too, but they fight on their own: they are your party, not you.
        var manager = GameObjectManager.Instance();
        if (manager != null)
        {
            for (var i = 0; i < 200; i++)
            {
                var obj = manager->Objects.IndexSorted[i].Value;
                if (obj == null || obj->EntityId != entityId) continue;
                if (obj->ObjectKind == ObjectKind.BattleNpc && obj->SubKind == (byte)BattleNpcSubKind.NpcPartyMember)
                    return Involvement.Party;
                if (obj->OwnerId == localId) return Involvement.You;
                break;
            }
        }

        return Involvement.Others;
    }

    /// <summary>
    /// The actor whose screen-log queue this is. Cheap when the outer call told us the
    /// candidates; otherwise scans the character slots of the object table.
    /// </summary>
    private static GameObject* FindQueueOwner(StdDeque<ScreenLogEntry>* queue, in ScreenLogHooks.CallContext context)
    {
        if (context.Valid)
        {
            if (context.Target != null && ((Character*)context.Target)->GetScreenLogManager() == queue)
                return (GameObject*)context.Target;
            if (context.Source != null && ((Character*)context.Source)->GetScreenLogManager() == queue)
                return (GameObject*)context.Source;
        }

        var manager = GameObjectManager.Instance();
        if (manager == null) return null;

        // Characters live in the first 200 slots.
        for (var i = 0; i < 200; i++)
        {
            var obj = manager->Objects.IndexSorted[i].Value;
            if (obj == null || !obj->IsCharacter()) continue;
            if (((Character*)obj)->GetScreenLogManager() == queue) return obj;
        }

        return null;
    }

    // ------------------------------------------------------------------
    // Failure handling
    // ------------------------------------------------------------------

    private void OnHookError(Exception ex)
    {
        Errors++;
        Plugin.Log.Error(ex, "Error handling a combat text event; the game showed it natively");

        var now = DateTime.UtcNow;
        recentErrors.Enqueue(now);
        while (recentErrors.Count > 0 && now - recentErrors.Peek() > BreakerWindow)
            recentErrors.Dequeue();

        if (recentErrors.Count >= BreakerLimit && TripReason is null)
        {
            TripReason = $"{BreakerLimit} errors within {BreakerWindow.TotalSeconds:0} seconds";
            Plugin.Log.Error("Circuit breaker tripped ({Reason}); the game's own combat text is back on", TripReason);
            Notify("HitSpark hit repeated errors and handed combat text back to the game. " +
                   "Settings → Diagnostics has the details and a button to try again.");
        }
    }

    private static void Notify(string message)
    {
        try
        {
            Plugin.NotificationManager.AddNotification(new Notification
            {
                Title = "HitSpark",
                Content = message,
                Type = NotificationType.Warning,
                InitialDuration = TimeSpan.FromSeconds(12),
            });
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not show notification");
        }
    }

    // ------------------------------------------------------------------
    // Diagnostics
    // ------------------------------------------------------------------

    private void LogEntry(StdDeque<ScreenLogEntry>* queue, ScreenLogEntry* e, in ScreenLogHooks.CallContext context,
        FlyTextKind kind, Category category, Involvement involvement, string decision, string tickSource, uint tickStatus)
    {
        var owner = "unknown";
        if (context.Valid)
        {
            if (context.Target != null && ((Character*)context.Target)->GetScreenLogManager() == queue) owner = "target";
            else if (context.Source != null && ((Character*)context.Source)->GetScreenLogManager() == queue) owner = "source";
            else owner = "other";
        }
        else
        {
            owner = "no-context";
        }

        Plugin.Log.Debug(
            "[event] {Kind}({KindId}) → {Category} [{Decision}] src={Src} tgt={Tgt} ({Involvement}) opt={Option} action={ActionKind}:{ActionId} " +
            "v=({V1},{V2},{V3}) call={Call} queue={Owner} source={Source} target={Target} tick={Tick} status={Status}",
            kind, (int)kind, category, decision, e->SourceRelation, e->TargetRelation, involvement, (ScreenLogOption)e->Option,
            e->ActionKind, e->ActionId, e->Value1, e->Value2, e->Value3,
            context.Valid ? $"{context.Kind}/{context.ActionId}/({context.Value1},{context.Value2},{context.Value3})" : "-",
            owner,
            Describe(context.Valid ? (GameObject*)context.Source : null),
            Describe(context.Valid ? (GameObject*)context.Target : null), tickSource, tickStatus);
    }

    private static string Describe(GameObject* obj)
        => obj == null ? "-" : $"{obj->NameString}#{obj->ObjectIndex}";

    private void OnFlyTextCreated(ref FlyTextKind kind, ref int val1, ref int val2, ref SeString text1, ref SeString text2,
        ref uint color, ref uint icon, ref uint damageTypeIcon, ref float yOffset, ref bool handled)
    {
        if (!Config.LogEvents) return;
        Plugin.Log.Debug("[native flytext] {Kind}({KindId}) val1={Val1} val2={Val2} icon={Icon} dmgIcon={DamageIcon} text1=\"{Text1}\" text2=\"{Text2}\"",
            kind, (int)kind, val1, val2, icon, damageTypeIcon, text1.TextValue, text2.TextValue);
    }

    public void Dispose()
    {
        Plugin.FlyTextGui.FlyTextCreated -= OnFlyTextCreated;
        hooks.Dispose();
        positionals.Dispose();
    }
}
