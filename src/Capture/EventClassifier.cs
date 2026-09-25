using Dalamud.Game.Gui.FlyText;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using HitSpark.Models;

namespace HitSpark.Capture;

/// <summary>
/// Maps the game's screen-log entries onto HitSpark categories. The game's kind numbering is
/// the same one Dalamud exposes as <see cref="FlyTextKind"/>.
/// </summary>
public static class EventClassifier
{
    /// <summary>ActionKind value for a regular action (the Action sheet).</summary>
    private const byte ActionKindAction = 1;

    /// <summary>
    /// Damage and heals over time share their kind with auto-attacks and plain heals. Ticks come
    /// from a status update rather than an action, so they carry no action; anything with an
    /// action attached - the player's Attack/Shot, a pet's or companion's attack - is a hit.
    /// Erring this way keeps pet auto-attacks from being labelled as ticks.
    /// (Hypothesis H4 in docs/DESIGN.md; confirm with the event recorder.)
    /// </summary>
    public static bool IsOverTime(byte actionKind, uint actionId)
        => actionKind != ActionKindAction || actionId == 0;

    public static Category Classify(
        FlyTextKind kind, ScreenLogOption option, byte actionKind, uint actionId,
        ScreenLogRelationKind source, ScreenLogRelationKind target)
    {
        var incoming = target == ScreenLogRelationKind.LocalPlayer && source != ScreenLogRelationKind.LocalPlayer;
        var toYou = target == ScreenLogRelationKind.LocalPlayer;

        switch (kind)
        {
            case FlyTextKind.AutoAttackOrDot:
            case FlyTextKind.AutoAttackOrDotDh:
            case FlyTextKind.AutoAttackOrDotCrit:
            case FlyTextKind.AutoAttackOrDotCritDh:
                if (incoming)
                    return IncomingDamage(kind, option);
                if (IsOverTime(actionKind, actionId))
                    return Category.DamageOverTime;
                return kind switch
                {
                    FlyTextKind.AutoAttackOrDotDh => Category.DirectHit,
                    FlyTextKind.AutoAttackOrDotCrit => Category.CriticalHit,
                    FlyTextKind.AutoAttackOrDotCritDh => Category.CriticalDirectHit,
                    _ => Category.AutoAttack,
                };

            case FlyTextKind.Damage:
            case FlyTextKind.DamageDh:
            case FlyTextKind.DamageCrit:
            case FlyTextKind.DamageCritDh:
                if (incoming)
                    return IncomingDamage(kind, option);
                return kind switch
                {
                    FlyTextKind.DamageDh => Category.DirectHit,
                    FlyTextKind.DamageCrit => Category.CriticalHit,
                    FlyTextKind.DamageCritDh => Category.CriticalDirectHit,
                    _ => Category.Ability,
                };

            case FlyTextKind.Miss:
            case FlyTextKind.NamedMiss:
            case FlyTextKind.Dodge:
            case FlyTextKind.NamedDodge:
                return incoming || kind is FlyTextKind.Dodge or FlyTextKind.NamedDodge ? Category.Evade : Category.Miss;

            case FlyTextKind.Healing:
                if (toYou) return Category.HealReceived;
                return IsOverTime(actionKind, actionId) ? Category.HealOverTime : Category.Heal;

            case FlyTextKind.HealingCrit:
                return toYou ? Category.HealReceived : Category.CriticalHeal;

            case FlyTextKind.Buff:
                return Category.StatusGained;
            case FlyTextKind.Debuff:
                return Category.DebuffGained;
            case FlyTextKind.BuffFading:
                return Category.StatusLost;
            case FlyTextKind.DebuffFading:
                return Category.DebuffLost;
            case FlyTextKind.DebuffNoEffect:
            case FlyTextKind.DebuffResisted:
            case FlyTextKind.DebuffInvulnerable:
                return Category.StatusResisted;

            case FlyTextKind.Invulnerable:
            case FlyTextKind.FullyResisted:
            case FlyTextKind.HasNoEffect:
            case FlyTextKind.Resist:
            case FlyTextKind.Reflect:
            case FlyTextKind.Reflected:
                return Category.Immune;

            case FlyTextKind.Interrupted:
            case FlyTextKind.Incapacitated:
                return Category.Interrupted;

            case FlyTextKind.MpDrain:
            case FlyTextKind.NamedTp:
            case FlyTextKind.MpRegen:
            case FlyTextKind.EpRegen:
            case FlyTextKind.CpRegen:
            case FlyTextKind.GpRegen:
            case FlyTextKind.NamedCriticalHitWithMp:
            case FlyTextKind.NamedCriticalHitWithTp:
            case FlyTextKind.HpDrain:
                return Category.Resource;

            case FlyTextKind.Exp:
            case FlyTextKind.IslandExp:
            case FlyTextKind.Dataset:
            case FlyTextKind.Knowledge:
            case FlyTextKind.PhantomExp:
                return Category.Experience;

            case FlyTextKind.CraftingProgress:
            case FlyTextKind.CraftingQuality:
            case FlyTextKind.CraftingQualityCrit:
            case FlyTextKind.CraftingQualityDh:
            case FlyTextKind.CraftingQualityCritDh:
            case FlyTextKind.Collectability:
            case FlyTextKind.CollectabilityCrit:
                return Category.Crafting;

            case FlyTextKind.LootedItem:
                return Category.Loot;

            default:
                return Category.Other;
        }
    }

    private static Category IncomingDamage(FlyTextKind kind, ScreenLogOption option)
    {
        if (option == ScreenLogOption.Blocked) return Category.Blocked;
        if (option == ScreenLogOption.Parried) return Category.Parried;

        return kind is FlyTextKind.AutoAttackOrDotCrit or FlyTextKind.AutoAttackOrDotCritDh
            or FlyTextKind.DamageCrit or FlyTextKind.DamageCritDh
            ? Category.CriticalDamageTaken
            : Category.DamageTaken;
    }

    public static Involvement Involvement(ScreenLogRelationKind source, ScreenLogRelationKind target)
    {
        if (source == ScreenLogRelationKind.LocalPlayer || target == ScreenLogRelationKind.LocalPlayer)
            return Models.Involvement.You;
        if (source == ScreenLogRelationKind.PartyMember || target == ScreenLogRelationKind.PartyMember)
            return Models.Involvement.Party;
        return Models.Involvement.Others;
    }

    /// <summary>
    /// The fixed word shown for text-only kinds, or null when the kind shows a number.
    /// These are static literals, so returning them never allocates.
    /// </summary>
    public static string? WordFor(FlyTextKind kind) => kind switch
    {
        FlyTextKind.Miss or FlyTextKind.NamedMiss => "Miss",
        FlyTextKind.Dodge or FlyTextKind.NamedDodge => "Evade",
        FlyTextKind.Invulnerable => "Invulnerable",
        FlyTextKind.DebuffInvulnerable => "Invulnerable",
        FlyTextKind.FullyResisted or FlyTextKind.Resist or FlyTextKind.DebuffResisted => "Resisted",
        FlyTextKind.HasNoEffect or FlyTextKind.DebuffNoEffect => "No Effect",
        FlyTextKind.Reflect => "Reflect",
        FlyTextKind.Reflected => "Reflected",
        FlyTextKind.Interrupted => "Interrupted",
        FlyTextKind.Incapacitated => "Incapacitated",
        _ => null,
    };

    /// <summary>Unit suffix for resource and progress kinds.</summary>
    public static string? UnitFor(FlyTextKind kind) => kind switch
    {
        FlyTextKind.MpDrain or FlyTextKind.MpRegen or FlyTextKind.NamedCriticalHitWithMp => " MP",
        FlyTextKind.NamedTp or FlyTextKind.NamedCriticalHitWithTp => " TP",
        FlyTextKind.EpRegen => " EP",
        FlyTextKind.CpRegen => " CP",
        FlyTextKind.GpRegen => " GP",
        FlyTextKind.HpDrain => " HP",
        FlyTextKind.Exp => " EXP",
        FlyTextKind.IslandExp => " Island EXP",
        FlyTextKind.PhantomExp => " Phantom EXP",
        FlyTextKind.Knowledge => " Knowledge",
        FlyTextKind.Dataset => " Data",
        _ => null,
    };

    /// <summary>Prefix word for crafting kinds, which read better as "Quality +123".</summary>
    public static string? CraftingLabel(FlyTextKind kind) => kind switch
    {
        FlyTextKind.CraftingProgress => "Progress",
        FlyTextKind.CraftingQuality or FlyTextKind.CraftingQualityCrit or FlyTextKind.CraftingQualityDh
            or FlyTextKind.CraftingQualityCritDh => "Quality",
        FlyTextKind.Collectability or FlyTextKind.CollectabilityCrit => "Collectability",
        _ => null,
    };
}
