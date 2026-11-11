using System;
using System.Collections.Generic;

namespace HitSpark.Models;

/// <summary>
/// A named set of split-hit rules, the same shape as a MultiHit "action group" so groups shared
/// for that plugin import as they are.
/// </summary>
[Serializable]
public class MultiHitGroup
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public List<MultiHitAction> Actions { get; set; } = new();
}

/// <summary>How one action's damage number is split into several hits.</summary>
[Serializable]
public class MultiHitAction
{
    /// <summary>Row of the game's Action sheet.</summary>
    public uint ActionId { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Drop the hits still to come when you use a different action before they land.</summary>
    public bool Interruptible { get; set; } = true;

    /// <summary>Number each hit ("#1", "#2"...) after the action name.</summary>
    public bool ShowHitNumber { get; set; } = true;

    /// <summary>Show the full amount once more after the last hit.</summary>
    public bool ShowFinal { get; set; }

    /// <summary>Shown instead of the action name when set.</summary>
    public string? CustomName { get; set; }

    public List<MultiHitStep> Hits { get; set; } = new();

    /// <summary>The total; its delay counts from the last hit.</summary>
    public MultiHitStep Final { get; set; } = new() { Frames = 1, Percent = 100 };
}

/// <summary>One hit of a split.</summary>
[Serializable]
public class MultiHitStep
{
    /// <summary>Delay after the real hit, in frames at 30 per second (MultiHit's unit).</summary>
    public int Frames { get; set; }

    /// <summary>Share of the real damage this hit shows.</summary>
    public int Percent { get; set; }

    /// <summary>Colour as 0xRRGGBBAA; an alpha of 0 keeps the style's colour.</summary>
    public uint Color { get; set; }

    public float Seconds => Math.Max(Frames, 0) / 30f;
}
