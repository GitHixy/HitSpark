using System;
using System.Collections.Generic;
using System.Numerics;
using Newtonsoft.Json;

namespace HitSpark.Models;

// ----------------------------------------------------------------------
// Style vocabulary. These are the editable, serialisable settings; the
// renderer never reads them directly - StyleCompiler flattens them first.
// ----------------------------------------------------------------------

public enum FontId
{
    /// <summary>The game's own serif numerals (read from the game files, not bundled).</summary>
    GameJupiter,
    /// <summary>The game's condensed display face.</summary>
    GameTrumpGothic,
    /// <summary>Dalamud's UI font, with the client language's glyphs.</summary>
    Interface,
    BebasNeue,
    Anton,
    RussoOne,
    BarlowCondensed,
    Barlow,
    LilitaOne,
    CinzelDecorative,
    PressStart2P,
}

public enum Trajectory
{
    /// <summary>Drifts straight up.</summary>
    Rise,
    /// <summary>Drops down, as if knocked off the target.</summary>
    Fall,
    /// <summary>Leaves sideways and curves up then down.</summary>
    Arc,
    /// <summary>Launched upward with spread, pulled back by gravity.</summary>
    Fountain,
    /// <summary>Scrolls steadily in a column - the classic incoming-damage lane.</summary>
    Lane,
    /// <summary>Stays where it spawned.</summary>
    Fixed,
}

public enum ArcDirection
{
    Random,
    Alternate,
    Left,
    Right,
}

public enum Easing
{
    Linear,
    QuadIn,
    QuadOut,
    QuadInOut,
    CubicIn,
    CubicOut,
    CubicInOut,
    QuartOut,
    ExpoOut,
    BackOut,
    ElasticOut,
    BounceOut,
}

public enum AnchorMode
{
    /// <summary>Above the actor the text is about (the one hit, healed or affected).</summary>
    Actor,
    /// <summary>Above your own character.</summary>
    Self,
    /// <summary>A column to the left of your character.</summary>
    SelfLeft,
    /// <summary>A column to the right of your character.</summary>
    SelfRight,
}

public enum IconMode
{
    None,
    Left,
    Right,
}

public enum TintMode
{
    /// <summary>Always the style's own colour.</summary>
    None,
    /// <summary>Physical, magical and unique damage use the preset palette.</summary>
    DamageType,
}

public enum AggregateKey
{
    /// <summary>Hits from the same action on the same target merge.</summary>
    Action,
    /// <summary>Everything in this category on the same target merges.</summary>
    Target,
}

public sealed class TextStyle
{
    public FontId Font { get; set; } = FontId.BebasNeue;
    public float Size { get; set; } = 34f;
    public Vector4 Color { get; set; } = new(1f, 1f, 1f, 1f);
    public TintMode Tint { get; set; } = TintMode.None;

    /// <summary>Blend toward <see cref="GradientColor"/> from top to bottom of the glyphs.</summary>
    public bool Gradient { get; set; }
    public Vector4 GradientColor { get; set; } = new(0.8f, 0.8f, 0.8f, 1f);

    public float OutlineWidth { get; set; } = 2f;
    public Vector4 OutlineColor { get; set; } = new(0f, 0f, 0f, 0.9f);

    public bool Shadow { get; set; } = true;
    public Vector2 ShadowOffset { get; set; } = new(0f, 2f);
    public Vector4 ShadowColor { get; set; } = new(0f, 0f, 0f, 0.55f);

    public float LetterSpacing { get; set; }

    /// <summary>Fixed caption under the number, e.g. "DoT Tick". Takes the place of the action name.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Show the action or status name next to the number, when the game provides one.</summary>
    public bool ShowName { get; set; }
    public float NameScale { get; set; } = 0.5f;
    public IconMode Icon { get; set; } = IconMode.None;
    public float IconScale { get; set; } = 0.85f;

    public bool Separators { get; set; } = true;
    public bool Abbreviate { get; set; }
    public long AbbreviateFrom { get; set; } = 100_000;
    public string Prefix { get; set; } = string.Empty;
    public string Suffix { get; set; } = string.Empty;
}

public sealed class MotionStyle
{
    public AnchorMode Anchor { get; set; } = AnchorMode.Actor;
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }

    /// <summary>Total lifetime in seconds.</summary>
    public float Duration { get; set; } = 1.25f;

    public Trajectory Trajectory { get; set; } = Trajectory.Rise;
    public Easing PathEasing { get; set; } = Easing.CubicOut;
    /// <summary>How far the path travels, in pixels at scale 1.</summary>
    public float Distance { get; set; } = 70f;
    /// <summary>Sideways reach of arcs and fountains, or random jitter for the others.</summary>
    public float Spread { get; set; } = 30f;
    public ArcDirection ArcDirection { get; set; } = ArcDirection.Random;

    /// <summary>Scale at spawn, easing to 1 over <see cref="PopTime"/>.</summary>
    public float PopScale { get; set; } = 1.35f;
    public float PopTime { get; set; } = 0.14f;
    public Easing PopEasing { get; set; } = Easing.BackOut;

    public float FadeInTime { get; set; } = 0.05f;
    public float FadeOutTime { get; set; } = 0.35f;
    /// <summary>Scale reached at the very end of the life, eased in with the fade.</summary>
    public float EndScale { get; set; } = 0.9f;

    /// <summary>Random tilt in degrees applied at spawn (±).</summary>
    public float Tilt { get; set; }
    /// <summary>Quantise motion to this many updates per second for a stepped look. 0 = smooth.</summary>
    public float StepRate { get; set; }
}

public sealed class EmphasisStyle
{
    public bool Punch { get; set; }
    public float PunchScale { get; set; } = 1.45f;
    public float PunchTime { get; set; } = 0.24f;

    public bool Shake { get; set; }
    public float ShakeAmount { get; set; } = 5f;
    public float ShakeTime { get; set; } = 0.28f;

    public bool Flash { get; set; }
    public float FlashTime { get; set; } = 0.16f;

    public bool Sparks { get; set; }
    public int SparkCount { get; set; } = 10;
    public float SparkSpeed { get; set; } = 260f;
    public Vector4 SparkColor { get; set; } = new(1f, 0.85f, 0.45f, 1f);

    public bool Ghost { get; set; }
    public int GhostCount { get; set; } = 4;
}

public sealed class AggregationStyle
{
    public bool Enabled { get; set; }
    public float WindowMs { get; set; } = 450f;
    public AggregateKey Key { get; set; } = AggregateKey.Action;
}

public sealed class CategoryStyle
{
    public CategoryMode Mode { get; set; } = CategoryMode.Replace;
    public Involvement Show { get; set; } = Involvement.All;
    public TextStyle Text { get; set; } = new();
    public MotionStyle Motion { get; set; } = new();
    public EmphasisStyle Emphasis { get; set; } = new();
    public AggregationStyle Aggregation { get; set; } = new();

    public CategoryStyle Clone()
        => JsonConvert.DeserializeObject<CategoryStyle>(JsonConvert.SerializeObject(this)) ?? new CategoryStyle();
}

public sealed class Palette
{
    public Vector4 Physical { get; set; } = new(1f, 0.93f, 0.85f, 1f);
    public Vector4 Magical { get; set; } = new(0.75f, 0.88f, 1f, 1f);
    public Vector4 Unique { get; set; } = new(0.85f, 0.72f, 1f, 1f);
}

public sealed class Preset
{
    public const int CurrentSchema = 1;

    public int SchemaVersion { get; set; } = CurrentSchema;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New preset";
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    /// <summary>Name of the preset this one was duplicated from, shown in the list.</summary>
    public string BasedOn { get; set; } = string.Empty;

    public Palette Palette { get; set; } = new();
    public Dictionary<Category, CategoryStyle> Styles { get; set; } = new();

    /// <summary>Built-ins live in code and are rebuilt on load, so they are never saved.</summary>
    [JsonIgnore]
    public bool IsBuiltIn { get; set; }

    /// <summary>The style for a category, creating a plain one if the preset never defined it.</summary>
    public CategoryStyle Get(Category category)
    {
        if (!Styles.TryGetValue(category, out var style))
        {
            style = new CategoryStyle();
            Styles[category] = style;
        }

        return style;
    }

    public Preset Clone()
    {
        var copy = JsonConvert.DeserializeObject<Preset>(JsonConvert.SerializeObject(this)) ?? new Preset();
        copy.IsBuiltIn = false;
        return copy;
    }
}
