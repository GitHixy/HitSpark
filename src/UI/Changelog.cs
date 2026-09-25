using System;
using System.Collections.Generic;
using System.Linq;

namespace HitSpark.UI;

/// <summary>What a single release note is about, which drives its colour and tag.</summary>
public enum NoteKind
{
    Added,
    Changed,
    Improved,
    Fixed,
    Removed,
}

/// <summary>One line in a release's notes.</summary>
public readonly record struct Note(NoteKind Kind, string Text);

/// <summary>One published version and everything that went into it.</summary>
public readonly record struct Release(string Version, string Headline, Note[] Notes);

/// <summary>
/// The release notes shown after an update. Newest first - add a new entry at the top
/// whenever the assembly version in HitSpark.csproj changes.
/// </summary>
public static class Changelog
{
    public static readonly Release[] Releases =
    [
        new("0.1.0", "Welcome to HitSpark",
        [
            new(NoteKind.Added, "HitSpark redraws the game's combat text with its own renderer: damage you deal and take, "
                                + "healing, misses and evades, status effects, and everything else the game floats, "
                                + "including experience, crafting and loot."),
            new(NoteKind.Added, "Eight built-in presets, each with its own fonts, colours and motion: Classic+, Heroic, Tally, "
                                + "Onslaught, Elemental, Grim, Minimal and Retro. Pick one in Settings → Presets."),
            new(NoteKind.Added, "Real motion: rising, falling, arcing and fountain paths, scrolling columns for what hits you, "
                                + "and a pop, punch, shake, flash, sparks or a trail when a crit lands."),
            new(NoteKind.Added, "Multi-hits and ticks can merge into one number that rolls up as the hits land."),
            new(NoteKind.Added, "Damage over time has its own colour and a \"DoT Tick\" caption, and each tick is credited to "
                                + "whoever applied it, so your DoTs always count as yours."),
            new(NoteKind.Added, "Other players' numbers can be shown smaller, or hidden, separately for your party and for "
                                + "everyone else (Settings → General)."),
            new(NoteKind.Added, "A style editor for every category of text - font, colours, outline, shadow, icons, captions, "
                                + "path, timing, impact and merging - with a live preview as you drag."),
            new(NoteKind.Added, "Share a preset as a short code, and import anyone's from the clipboard."),
            new(NoteKind.Added, "Test in world plays sample numbers of every kind on you and your target, even out of combat."),
            new(NoteKind.Added, "HitSpark follows the game's own \"Display flying text\" and \"Display pop-up text\" options, "
                                + "hides in cutscenes and GPose, and never shows more than the game would."),
            new(NoteKind.Added, "If a game update ever breaks it, HitSpark steps aside and the game's own numbers come back. "
                                + "/hitspark native does the same by hand."),
            new(NoteKind.Added, "A Diagnostics page with a live log, an event recorder and Copy report, plus buttons to report "
                                + "a bug or suggest a feature on GitHub."),
        ]),
    ];

    /// <summary>The running assembly's version, formatted the way the notes list them.</summary>
    public static string CurrentVersion
    {
        get
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    /// <summary>The newest release, or null when the list is empty.</summary>
    public static Release? Latest => Releases.Length > 0 ? Releases[0] : null;

    /// <summary>
    /// Everything published since <paramref name="sinceVersion"/>. An unknown or missing
    /// version yields just the newest release, so an upgrade never dumps the whole history.
    /// </summary>
    public static IReadOnlyList<Release> Since(string sinceVersion)
    {
        if (!Version.TryParse(sinceVersion, out var seen))
            return Latest.HasValue ? [Latest.Value] : [];

        return Releases
            .Where(r => Version.TryParse(r.Version, out var v) && v > seen)
            .ToList();
    }

    /// <summary>True when the user has not yet dismissed the notes for the running version.</summary>
    public static bool HasUnseenNotes(string lastSeenVersion)
        => !string.Equals(lastSeenVersion, CurrentVersion, StringComparison.Ordinal)
           && Since(lastSeenVersion).Count > 0;

    public static string KindLabel(NoteKind kind) => kind switch
    {
        NoteKind.Added => "NEW",
        NoteKind.Changed => "CHANGED",
        NoteKind.Improved => "IMPROVED",
        NoteKind.Fixed => "FIXED",
        NoteKind.Removed => "REMOVED",
        _ => "NOTE",
    };

    public static System.Numerics.Vector4 KindColor(NoteKind kind) => kind switch
    {
        NoteKind.Added => Theme.Good,
        NoteKind.Changed => Theme.Crystal,
        NoteKind.Improved => Theme.Gold,
        NoteKind.Fixed => Theme.Warn,
        NoteKind.Removed => Theme.Bad,
        _ => Theme.TextMuted,
    };
}
