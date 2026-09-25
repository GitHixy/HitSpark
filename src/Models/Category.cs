using System;

namespace HitSpark.Models;

/// <summary>
/// The user-facing kinds of combat text. Every game FlyTextKind maps to exactly one of these
/// (see <see cref="Capture.EventClassifier"/>), and each one has its own style in a preset.
/// </summary>
public enum Category
{
    // Damage you deal
    AutoAttack,
    Ability,
    DamageOverTime,
    CriticalHit,
    DirectHit,
    CriticalDirectHit,

    // Healing
    Heal,
    HealOverTime,
    CriticalHeal,
    HealReceived,

    // Damage you take
    DamageTaken,
    CriticalDamageTaken,
    Blocked,
    Parried,

    // Avoidance and immunity
    Miss,
    Evade,
    Immune,

    // Status effects
    StatusGained,
    StatusLost,
    DebuffGained,
    DebuffLost,
    StatusResisted,

    // Everything else the game floats
    Resource,
    Experience,
    Crafting,
    Loot,
    Interrupted,
    Other,
}

/// <summary>Groups used to lay categories out in the settings.</summary>
public enum CategoryGroup
{
    Outgoing,
    Healing,
    Incoming,
    Avoidance,
    Status,
    Misc,
}

/// <summary>What HitSpark does with a category.</summary>
public enum CategoryMode
{
    /// <summary>HitSpark draws it and the native text is suppressed.</summary>
    Replace,
    /// <summary>The game draws its own text, untouched.</summary>
    Native,
    /// <summary>Suppressed and not drawn at all.</summary>
    Hide,
}

/// <summary>Who an event involves, as the game classifies it.</summary>
[Flags]
public enum Involvement
{
    None = 0,
    You = 1,
    Party = 2,
    Others = 4,
    All = You | Party | Others,
}

public static class CategoryInfo
{
    public static readonly Category[] All = Enum.GetValues<Category>();

    public static int Count => All.Length;

    public static CategoryGroup Group(Category c) => c switch
    {
        Category.AutoAttack or Category.Ability or Category.DamageOverTime or Category.CriticalHit
            or Category.DirectHit or Category.CriticalDirectHit => CategoryGroup.Outgoing,
        Category.Heal or Category.HealOverTime or Category.CriticalHeal or Category.HealReceived => CategoryGroup.Healing,
        Category.DamageTaken or Category.CriticalDamageTaken or Category.Blocked or Category.Parried => CategoryGroup.Incoming,
        Category.Miss or Category.Evade or Category.Immune => CategoryGroup.Avoidance,
        Category.StatusGained or Category.StatusLost or Category.DebuffGained or Category.DebuffLost
            or Category.StatusResisted => CategoryGroup.Status,
        _ => CategoryGroup.Misc,
    };

    public static string GroupName(CategoryGroup g) => g switch
    {
        CategoryGroup.Outgoing => "Damage dealt",
        CategoryGroup.Healing => "Healing",
        CategoryGroup.Incoming => "Damage taken",
        CategoryGroup.Avoidance => "Avoidance",
        CategoryGroup.Status => "Status effects",
        _ => "Other",
    };

    public static string Name(Category c) => c switch
    {
        Category.AutoAttack => "Auto-attacks",
        Category.Ability => "Abilities",
        Category.DamageOverTime => "Damage over time",
        Category.CriticalHit => "Critical hits",
        Category.DirectHit => "Direct hits",
        Category.CriticalDirectHit => "Critical direct hits",
        Category.Heal => "Heals",
        Category.HealOverTime => "Heals over time",
        Category.CriticalHeal => "Critical heals",
        Category.HealReceived => "Heals received",
        Category.DamageTaken => "Damage taken",
        Category.CriticalDamageTaken => "Critical damage taken",
        Category.Blocked => "Blocked",
        Category.Parried => "Parried",
        Category.Miss => "Misses",
        Category.Evade => "Evades",
        Category.Immune => "Immune / resisted / reflected",
        Category.StatusGained => "Buffs gained",
        Category.StatusLost => "Buffs lost",
        Category.DebuffGained => "Debuffs gained",
        Category.DebuffLost => "Debuffs lost",
        Category.StatusResisted => "Status resisted / no effect",
        Category.Resource => "MP, TP, GP, CP",
        Category.Experience => "Experience",
        Category.Crafting => "Crafting",
        Category.Loot => "Looted items",
        Category.Interrupted => "Interrupted / incapacitated",
        _ => "Other",
    };

    /// <summary>Numbers that carry impact and deserve crit-level emphasis by default.</summary>
    public static bool IsCritical(Category c)
        => c is Category.CriticalHit or Category.CriticalDirectHit or Category.CriticalHeal or Category.CriticalDamageTaken;

    /// <summary>Categories whose text is about the local player and float near them by default.</summary>
    public static bool IsIncoming(Category c)
        => c is Category.HealReceived or Category.DamageTaken or Category.CriticalDamageTaken or Category.Blocked
            or Category.Parried or Category.Evade or Category.Resource or Category.Experience or Category.Crafting
            or Category.Loot;
}
