using System;
using System.Collections.Generic;

namespace HitSpark.Models;

public enum ReducedMotionMode
{
    /// <summary>Follow Dalamud's reduced motion setting.</summary>
    Auto,
    On,
    Off,
}

public enum ColorVision
{
    /// <summary>Colours as the preset defines them.</summary>
    Normal,
    /// <summary>Red-weak or red-blind.</summary>
    Protanopia,
    /// <summary>Green-weak or green-blind, the most common.</summary>
    Deuteranopia,
    /// <summary>Blue-yellow.</summary>
    Tritanopia,
}

/// <summary>
/// Configuration settings for the HitSpark plugin
/// </summary>
[Serializable]
public class Configuration
{
    public int Version { get; set; } = 1;

    // Presets
    public string ActivePresetId { get; set; } = "builtin.heroic";
    public List<Preset> UserPresets { get; set; } = new();

    // Global look
    /// <summary>Multiplies every size and distance, on top of the preset.</summary>
    public float GlobalScale { get; set; } = 1f;
    public float GlobalOpacity { get; set; } = 1f;
    public ReducedMotionMode ReducedMotion { get; set; } = ReducedMotionMode.Auto;

    /// <summary>Colour vision deficiency to correct every preset's colours for.</summary>
    public ColorVision ColorVision { get; set; } = ColorVision.Normal;
    /// <summary>How strongly colours are corrected, 0 to 1.</summary>
    public float ColorAssistStrength { get; set; } = 1f;

    // Other players' numbers (pop-up text the game shows for party members and others)
    public bool ShowParty { get; set; } = true;
    public bool ShowOthers { get; set; } = true;
    /// <summary>Size of party members' numbers relative to your own.</summary>
    public float PartyScale { get; set; } = 0.85f;
    /// <summary>Size of everyone else's numbers relative to your own.</summary>
    public float OthersScale { get; set; } = 0.75f;
    /// <summary>
    /// Numbers that don't involve you are dropped when they happen farther than this from you,
    /// in yalms. 0 means no limit.
    /// </summary>
    public float MaxDistance { get; set; } = 50f;

    // Behaviour
    /// <summary>Hard cap on numbers alive at once. Oldest, least important ones make room.</summary>
    public int MaxLiveNumbers { get; set; } = 160;
    /// <summary>User-requested kill switch: hand every category back to the game.</summary>
    public bool NativeMode { get; set; }
    /// <summary>Numbers pass under the game's windows and HUD instead of over them.</summary>
    public bool BelowGameUi { get; set; }

    // Multi-hit
    /// <summary>Split configured actions' damage into several hits.</summary>
    public bool MultiHitEnabled { get; set; } = true;
    public List<MultiHitGroup> MultiHitGroups { get; set; } = new();

    // Diagnostics
    /// <summary>Write every captured event to the Diagnostics log.</summary>
    public bool LogEvents { get; set; }

    // Release notes
    /// <summary>Last plugin version whose changelog the user dismissed.</summary>
    public string LastSeenVersion { get; set; } = string.Empty;

    /// <summary>Whether numbers involving this group are shown at all.</summary>
    public bool Allows(Involvement involvement) => involvement switch
    {
        Involvement.Party => ShowParty,
        Involvement.Others => ShowOthers,
        _ => true,
    };

    /// <summary>Size multiplier for numbers involving this group.</summary>
    public float ScaleFor(Involvement involvement) => involvement switch
    {
        Involvement.Party => Math.Clamp(PartyScale, 0.3f, 1.5f),
        Involvement.Others => Math.Clamp(OthersScale, 0.3f, 1.5f),
        _ => 1f,
    };
}
