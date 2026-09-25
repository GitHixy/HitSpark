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

    // Other players' numbers (pop-up text the game shows for party members and others)
    public bool ShowParty { get; set; } = true;
    public bool ShowOthers { get; set; } = true;
    /// <summary>Size of party members' numbers relative to your own.</summary>
    public float PartyScale { get; set; } = 0.85f;
    /// <summary>Size of everyone else's numbers relative to your own.</summary>
    public float OthersScale { get; set; } = 0.75f;

    // Behaviour
    /// <summary>Hard cap on numbers alive at once. Oldest, least important ones make room.</summary>
    public int MaxLiveNumbers { get; set; } = 160;
    /// <summary>User-requested kill switch: hand every category back to the game.</summary>
    public bool NativeMode { get; set; }

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
