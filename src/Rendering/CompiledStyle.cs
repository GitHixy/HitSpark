using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using HitSpark.Models;

namespace HitSpark.Rendering;

/// <summary>
/// A category style flattened for the hot path: packed colours, pre-converted angles and no
/// references back into the configuration. Rebuilt whenever the preset changes.
/// </summary>
public sealed class CompiledStyle
{
    public Category Category;
    public CategoryMode Mode;
    public Involvement Show;
    public int Priority;

    // Text
    public FontId Font;
    public float Size;
    public Vector4 Color;
    public TintMode Tint;
    public bool Gradient;
    public Vector4 GradientColor;
    public float OutlineWidth;
    public uint OutlineColor;
    public bool Shadow;
    public Vector2 ShadowOffset;
    public uint ShadowColor;
    public float LetterSpacing;
    public bool ShowName;
    public string Label = string.Empty;
    /// <summary>The label has characters beyond Latin and must use the interface face.</summary>
    public bool LabelFallback;
    public float NameScale;
    public IconMode Icon;
    public float IconScale;
    public bool Separators;
    public bool Abbreviate;
    public long AbbreviateFrom;
    public string Prefix = string.Empty;
    public string Suffix = string.Empty;

    // Motion
    public AnchorMode Anchor;
    public Vector2 Offset;
    public float Duration;
    public Trajectory Trajectory;
    public Easing PathEasing;
    public float Distance;
    public float Spread;
    public ArcDirection ArcDirection;
    public float PopScale;
    public float PopTime;
    public Easing PopEasing;
    public float FadeInTime;
    public float FadeOutTime;
    public float EndScale;
    public float TiltRadians;
    public float StepRate;

    // Emphasis
    public bool Punch;
    public float PunchScale;
    public float PunchTime;
    public bool Shake;
    public float ShakeAmount;
    public float ShakeTime;
    public bool Flash;
    public float FlashTime;
    public bool Sparks;
    public int SparkCount;
    public float SparkSpeed;
    public uint SparkColor;
    public bool Ghost;
    public int GhostCount;

    // Aggregation
    public bool Aggregate;
    public double AggregateWindow;
    public AggregateKey AggregateKey;

    public static CompiledStyle From(Category category, CategoryStyle s)
    {
        var t = s.Text;
        var m = s.Motion;
        var e = s.Emphasis;
        var a = s.Aggregation;

        return new CompiledStyle
        {
            Category = category,
            Mode = s.Mode,
            Show = s.Show,
            Priority = CategoryInfo.IsCritical(category) ? 2 : CategoryInfo.Group(category) is CategoryGroup.Status or CategoryGroup.Misc ? 0 : 1,

            Font = t.Font,
            Size = Math.Clamp(t.Size, 6f, 160f),
            Color = t.Color,
            Tint = t.Tint,
            Gradient = t.Gradient,
            GradientColor = t.GradientColor,
            OutlineWidth = Math.Clamp(t.OutlineWidth, 0f, 6f),
            OutlineColor = ImGui.ColorConvertFloat4ToU32(t.OutlineColor),
            Shadow = t.Shadow,
            ShadowOffset = t.ShadowOffset,
            ShadowColor = ImGui.ColorConvertFloat4ToU32(t.ShadowColor),
            LetterSpacing = Math.Clamp(t.LetterSpacing, -10f, 30f),
            ShowName = t.ShowName,
            Label = t.Label ?? string.Empty,
            LabelFallback = (t.Label ?? string.Empty).Any(c => c > 0x17F),
            NameScale = Math.Clamp(t.NameScale, 0.2f, 1.5f),
            Icon = t.Icon,
            IconScale = Math.Clamp(t.IconScale, 0.2f, 2f),
            Separators = t.Separators,
            Abbreviate = t.Abbreviate,
            AbbreviateFrom = Math.Max(1000, t.AbbreviateFrom),
            Prefix = t.Prefix ?? string.Empty,
            Suffix = t.Suffix ?? string.Empty,

            Anchor = m.Anchor,
            Offset = new Vector2(m.OffsetX, m.OffsetY),
            Duration = Math.Clamp(m.Duration, 0.2f, 8f),
            Trajectory = m.Trajectory,
            PathEasing = m.PathEasing,
            Distance = Math.Clamp(m.Distance, 0f, 600f),
            Spread = Math.Clamp(m.Spread, 0f, 600f),
            ArcDirection = m.ArcDirection,
            PopScale = Math.Clamp(m.PopScale, 0.1f, 4f),
            PopTime = Math.Clamp(m.PopTime, 0f, 2f),
            PopEasing = m.PopEasing,
            FadeInTime = Math.Clamp(m.FadeInTime, 0f, 2f),
            FadeOutTime = Math.Clamp(m.FadeOutTime, 0.01f, 4f),
            EndScale = Math.Clamp(m.EndScale, 0.05f, 4f),
            TiltRadians = Math.Clamp(m.Tilt, 0f, 90f) * MathF.PI / 180f,
            StepRate = Math.Clamp(m.StepRate, 0f, 60f),

            Punch = e.Punch,
            PunchScale = Math.Clamp(e.PunchScale, 1f, 4f),
            PunchTime = Math.Clamp(e.PunchTime, 0.05f, 2f),
            Shake = e.Shake,
            ShakeAmount = Math.Clamp(e.ShakeAmount, 0f, 40f),
            ShakeTime = Math.Clamp(e.ShakeTime, 0.05f, 2f),
            Flash = e.Flash,
            FlashTime = Math.Clamp(e.FlashTime, 0.02f, 1f),
            Sparks = e.Sparks,
            SparkCount = Math.Clamp(e.SparkCount, 1, 40),
            SparkSpeed = Math.Clamp(e.SparkSpeed, 20f, 1000f),
            SparkColor = ImGui.ColorConvertFloat4ToU32(e.SparkColor),
            Ghost = e.Ghost,
            GhostCount = Math.Clamp(e.GhostCount, 1, FlyInstance.MaxGhosts),

            Aggregate = a.Enabled,
            AggregateWindow = Math.Clamp(a.WindowMs, 50f, 3000f) / 1000.0,
            AggregateKey = a.Key,
        };
    }
}

/// <summary>A whole preset, compiled: one style per category plus the damage palette.</summary>
public sealed class CompiledPreset
{
    public readonly CompiledStyle[] Styles = new CompiledStyle[CategoryInfo.Count];
    public Vector4 Physical;
    public Vector4 Magical;
    public Vector4 Unique;
    public string Name = string.Empty;

    public CompiledStyle this[Category category] => Styles[(int)category];

    public static CompiledPreset From(Preset preset)
    {
        var compiled = new CompiledPreset
        {
            Name = preset.Name,
            Physical = preset.Palette.Physical,
            Magical = preset.Palette.Magical,
            Unique = preset.Palette.Unique,
        };

        foreach (var category in CategoryInfo.All)
            compiled.Styles[(int)category] = CompiledStyle.From(category, preset.Get(category));

        return compiled;
    }
}
