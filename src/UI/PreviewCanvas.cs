using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using HitSpark.Capture;
using HitSpark.Models;
using HitSpark.Rendering;

namespace HitSpark.UI;

/// <summary>
/// A small stage inside the settings window where the test script plays through the real
/// renderer: a target dummy on the right, "you" on the left. Works anywhere, in or out of combat.
/// </summary>
public sealed class PreviewCanvas
{
    private readonly Plugin plugin;
    private readonly FlyRenderer renderer;
    private readonly TestEventSource source;
    private readonly Action<SpawnRequest> emit;

    private string presetId = string.Empty;
    private Vector2 actorPoint;
    private Vector2 selfPoint;

    public PreviewCanvas(Plugin plugin)
    {
        this.plugin = plugin;
        renderer = new FlyRenderer(plugin.Fonts, worldSpace: false);
        source = new TestEventSource();
        emit = Emit;
    }

    /// <param name="presetId">Identity of the preset; switching presets clears the stage, re-compiles of the same one don't.</param>
    public void Draw(string id, Vector2 size, CompiledPreset preset, string presetId, Category? focus = null)
    {
        if (this.presetId != presetId)
        {
            this.presetId = presetId;
            renderer.Clear();
        }

        // Live edits recompile every frame; numbers already on stage finish with their old style.
        renderer.Preset = preset;

        if (source.Focus != focus)
        {
            source.Focus = focus;
            source.Reset();
            renderer.Clear();
        }

        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var w = size.X > 0 ? size.X : ImGui.GetContentRegionAvail().X;
        var h = size.Y;
        var max = p + new Vector2(w, h);

        ImGui.InvisibleButton(id, new Vector2(w, h));

        DrawStage(dl, p, max);

        // Anchors sit just above the figures' heads, like actor anchors in the world.
        actorPoint = p + new Vector2(w * 0.68f, h * 0.78f - 92f);
        selfPoint = p + new Vector2(w * 0.3f, h * 0.78f - 78f);

        var dt = ImGui.GetIO().DeltaTime;
        source.Update(dt, emit);

        var config = plugin.Configuration;
        var ctx = new RenderContext
        {
            Now = plugin.Now,
            DeltaTime = dt,
            Scale = config.GlobalScale * Math.Clamp(h / 360f, 0.6f, 1f),
            Opacity = config.GlobalOpacity,
            ReducedMotion = plugin.ReducedMotion,
            MaxLive = 120,
            Visible = true,
            ClipMin = p + Vector2.One,
            ClipMax = max - Vector2.One,
        };

        renderer.Draw(dl, ctx);
    }

    private void Emit(SpawnRequest request)
    {
        var config = plugin.Configuration;
        if (!config.Allows(request.Involvement)) return;
        request.SizeScale = config.ScaleFor(request.Involvement);

        request.UseScreenAnchor = true;
        request.ScreenSelf = selfPoint;
        request.ScreenActor = CategoryInfo.IsIncoming(request.Category) ? selfPoint : actorPoint;
        renderer.Enqueue(request);
    }

    private void DrawStage(ImDrawListPtr dl, Vector2 min, Vector2 max)
    {
        // Night-sky backdrop with a lit floor, so white and coloured numbers both read.
        dl.AddRectFilledMultiColor(min, max,
            Theme.U32(new Vector4(0.09f, 0.12f, 0.19f, 1f)), Theme.U32(new Vector4(0.11f, 0.13f, 0.2f, 1f)),
            Theme.U32(new Vector4(0.16f, 0.15f, 0.14f, 1f)), Theme.U32(new Vector4(0.13f, 0.14f, 0.17f, 1f)));
        dl.AddRect(min, max, Theme.U32(Theme.Line), Theme.Radius, ImDrawFlags.None, 1f);

        var floorY = min.Y + (max.Y - min.Y) * 0.78f;
        dl.AddLine(new Vector2(min.X + 12, floorY), new Vector2(max.X - 12, floorY), Theme.U32(Theme.Gold, 0.18f), 1f);

        var w = max.X - min.X;
        DrawFigure(dl, new Vector2(min.X + w * 0.68f, floorY), 1.25f, Theme.Bad, "Target");
        DrawFigure(dl, new Vector2(min.X + w * 0.3f, floorY), 1f, Theme.Crystal, "You");
    }

    /// <summary>A simple silhouette: shadow ellipse, body capsule and head, with a caption.</summary>
    private static void DrawFigure(ImDrawListPtr dl, Vector2 feet, float scale, Vector4 tint, string caption)
    {
        var body = Theme.U32(Theme.Mix(Theme.Surface, tint, 0.25f), 0.95f);
        var rim = Theme.U32(tint, 0.55f);

        dl.AddRectFilled(feet - new Vector2(26f, 5f) * scale, feet + new Vector2(26f, 5f) * scale, Theme.U32(new Vector4(0, 0, 0, 0.35f)), 5f * scale);
        var top = feet - new Vector2(0f, 62f * scale);
        dl.AddRectFilled(top - new Vector2(13f, 0f) * scale, feet + new Vector2(13f, -4f) * scale, body, 12f * scale);
        dl.AddRect(top - new Vector2(13f, 0f) * scale, feet + new Vector2(13f, -4f) * scale, rim, 12f * scale, ImDrawFlags.None, 1.2f);
        dl.AddCircleFilled(top - new Vector2(0f, 12f * scale), 10f * scale, body, 24);
        dl.AddCircle(top - new Vector2(0f, 12f * scale), 10f * scale, rim, 24, 1.2f);

        var ts = ImGui.CalcTextSize(caption);
        dl.AddText(feet + new Vector2(-ts.X * 0.5f, 9f), Theme.U32(Theme.TextFaint), caption);
    }
}
