using Dalamud.Game.Gui.FlyText;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using HitSpark.Models;
using HitSpark.Services;

namespace HitSpark.Capture;

/// <summary>
/// Maps the game's screen-log entries onto HitSpark categories. The game's kind numbering is
/// the same one Dalamud exposes as <see cref="FlyTextKind"/>.
/// </summary>
public static class EventClassifier
{
    /// <summary>
    /// Damage and heals over time share their kind with auto-attacks and plain heals. A tick comes
    /// from a status update rather than an action: it either arrives while the game processes a
    /// DoT/HoT (<paramref name="inTick"/>, from the ProcessHotDot hook) or carries no action at all
    /// (<c>action=0:0</c>, verified on AST Combust III). Anything with an action attached is a hit,
    /// whatever sheet the action comes from: the player's Attack/Shot, a pet's or automaton's
    /// attack, a companion's action or an item. (Hypothesis H4 in docs/DESIGN.md.)
    /// </summary>
    public static bool IsOverTime(uint actionId, bool inTick)
        => inTick || actionId == 0;

    public static Category Classify(
        FlyTextKind kind, ScreenLogOption option, uint actionId, bool inTick,
        ScreenLogRelationKind source, ScreenLogRelationKind target)
    {
        var toYou = target == ScreenLogRelationKind.LocalPlayer;
        var overTime = IsOverTime(actionId, inTick);

        // Ticks name the afflicted actor as their own source, so a DoT ticking on you reads as
        // "you on you": that's damage you take, not a DoT you applied.
        var incoming = toYou && (source != ScreenLogRelationKind.LocalPlayer || overTime);

        switch (kind)
        {
            case FlyTextKind.AutoAttackOrDot:
            case FlyTextKind.AutoAttackOrDotDh:
            case FlyTextKind.AutoAttackOrDotCrit:
            case FlyTextKind.AutoAttackOrDotCritDh:
                if (incoming)
                    return IncomingDamage(kind, option);
                if (overTime)
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
                return overTime ? Category.HealOverTime : Category.Heal;

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
    /// The word shown for text-only kinds, or null when the kind shows a number.
    /// Taken from the game's sheets, so it is in the client's language.
    /// </summary>
    public static string? WordFor(FlyTextKind kind) => kind switch
    {
        FlyTextKind.Miss or FlyTextKind.NamedMiss => GameSheets.Miss,
        FlyTextKind.Dodge or FlyTextKind.NamedDodge => GameSheets.Dodge,
        FlyTextKind.Invulnerable => GameSheets.Invulnerable,
        FlyTextKind.DebuffInvulnerable => GameSheets.Invulnerable,
        FlyTextKind.FullyResisted or FlyTextKind.Resist or FlyTextKind.DebuffResisted => GameSheets.Resist,
        FlyTextKind.HasNoEffect or FlyTextKind.DebuffNoEffect => GameSheets.NoEffect,
        FlyTextKind.Reflect => GameSheets.Reflect,
        FlyTextKind.Reflected => GameSheets.Reflected,
        FlyTextKind.Interrupted => GameSheets.Interrupted,
        FlyTextKind.Incapacitated => GameSheets.Incapacitated,
        _ => null,
    };

    /// <summary>Unit suffix for resource and progress kinds.</summary>
    public static string? UnitFor(FlyTextKind kind) => kind switch
    {
        FlyTextKind.MpDrain or FlyTextKind.MpRegen or FlyTextKind.NamedCriticalHitWithMp => GameSheets.Mp,
        FlyTextKind.NamedTp or FlyTextKind.NamedCriticalHitWithTp => GameSheets.Tp,
        FlyTextKind.EpRegen => GameSheets.Ep,
        FlyTextKind.CpRegen => GameSheets.Cp,
        FlyTextKind.GpRegen => GameSheets.Gp,
        FlyTextKind.HpDrain => GameSheets.Hp,
        FlyTextKind.Exp => GameSheets.Exp,
        FlyTextKind.IslandExp => GameSheets.IslandExp,
        FlyTextKind.PhantomExp => GameSheets.PhantomExp,
        FlyTextKind.Knowledge => GameSheets.Knowledge,
        FlyTextKind.Dataset => GameSheets.Data,
        _ => null,
    };

    /// <summary>Prefix word for crafting kinds, which read better as "Quality +123".</summary>
    public static string? CraftingLabel(FlyTextKind kind) => kind switch
    {
        FlyTextKind.CraftingProgress => GameSheets.Progress,
        FlyTextKind.CraftingQuality or FlyTextKind.CraftingQualityCrit or FlyTextKind.CraftingQualityDh
            or FlyTextKind.CraftingQualityCritDh => GameSheets.Quality,
        FlyTextKind.Collectability or FlyTextKind.CollectabilityCrit => GameSheets.Collectability,
        _ => null,
    };
}
