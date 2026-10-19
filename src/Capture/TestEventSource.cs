using System;
using HitSpark.Models;
using HitSpark.Services;

namespace HitSpark.Capture;

/// <summary>
/// Fake combat for the test mode and the preview canvas. Plays a looping script that covers
/// every category - including multi-hit bursts, DoT streams and crit chains - through the same
/// renderer as real events. Nothing here touches the game.
/// </summary>
public sealed class TestEventSource
{
    private readonly record struct Beat(Category Category, float Delay, int Hits = 1);

    // A short "rotation": openers, a crit chain, a multi-hit, incoming burst, heals, statuses.
    private static readonly Beat[] Script =
    [
        new(Category.StatusGained, 0.2f),
        new(Category.Ability, 0.45f),
        new(Category.AutoAttack, 0.3f),
        new(Category.DirectHit, 0.4f),
        new(Category.DamageOverTime, 0.25f),
        new(Category.CriticalHit, 0.55f),
        new(Category.Ability, 0.3f, 3),
        new(Category.DamageTaken, 0.35f),
        new(Category.DamageTaken, 0.2f),
        new(Category.Blocked, 0.3f),
        new(Category.CriticalDirectHit, 0.6f),
        new(Category.Miss, 0.35f),
        new(Category.HealReceived, 0.35f),
        new(Category.DamageOverTime, 0.25f),
        new(Category.DebuffGained, 0.4f),
        new(Category.Heal, 0.35f),
        new(Category.CriticalHeal, 0.5f),
        new(Category.HealOverTime, 0.3f),
        new(Category.Evade, 0.4f),
        new(Category.CriticalDamageTaken, 0.5f),
        new(Category.AutoAttack, 0.3f),
        new(Category.Resource, 0.35f),
        new(Category.StatusLost, 0.45f),
        new(Category.Experience, 0.6f),
    ];

    // Real ids so names and icons come from the game data.
    private static readonly uint[] DamageActions = [9, 141, 142, 144, 24, 53];
    private static readonly uint[] HealActions = [120, 124, 135, 190];
    private static readonly uint[] BuffStatuses = [158, 1204, 76, 150];
    private static readonly uint[] DebuffStatuses = [143, 179, 144, 3];

    private readonly Random random = new(1234);
    private float timer;
    private int index;
    private int burstLeft;
    private Beat burst;

    /// <summary>When set, only this category is spawned (the editor's live preview).</summary>
    public Category? Focus { get; set; }

    /// <summary>Speed multiplier for the script.</summary>
    public float Rate { get; set; } = 1f;

    public void Reset()
    {
        timer = 0f;
        index = 0;
        burstLeft = 0;
    }

    /// <summary>Advances the script and emits whatever is due.</summary>
    public void Update(float deltaTime, Action<SpawnRequest> emit)
    {
        // After a pause (window closed, test mode off) start fresh rather than catching up in a burst.
        timer = Math.Max(timer - deltaTime * Rate, -0.5f);

        while (timer <= 0f)
        {
            if (burstLeft > 0)
            {
                // Multi-hit: same action, rapid fire, so aggregation has something to merge.
                burstLeft--;
                emit(Make(burst.Category, burstAction));
                timer += 0.09f;
                continue;
            }

            Beat beat;
            if (Focus is { } focus)
            {
                beat = new Beat(focus, 0.55f, random.Next(4) == 0 ? 3 : 1);
            }
            else
            {
                beat = Script[index];
                index = (index + 1) % Script.Length;
            }

            burstAction = PickAction(beat.Category);
            emit(Make(beat.Category, burstAction));
            if (beat.Hits > 1)
            {
                burst = beat;
                burstLeft = beat.Hits - 1;
                timer += 0.09f;
            }
            else
            {
                timer += beat.Delay;
            }
        }
    }

    private uint burstAction;

    private uint PickAction(Category category) => CategoryInfo.Group(category) == CategoryGroup.Healing
        ? HealActions[random.Next(HealActions.Length)]
        : DamageActions[random.Next(DamageActions.Length)];

    private SpawnRequest Make(Category category, uint actionId)
    {
        var r = new SpawnRequest
        {
            Category = category,
            ActorIndex = SpawnRequest.NoActor,
            DamageKind = (DamageKind)random.Next(1, 4),
            Involvement = Involvement.You,
        };

        // Some of the hits and heals on the target come from the party or from strangers,
        // so the "other players" size and visibility settings can be judged in the preview.
        if (category is Category.Ability or Category.AutoAttack or Category.CriticalHit or Category.DirectHit or Category.Heal)
        {
            var roll = random.NextDouble();
            r.Involvement = roll < 0.3 ? Involvement.Party : roll < 0.42 ? Involvement.Others : Involvement.You;
        }

        switch (category)
        {
            case Category.StatusGained:
            case Category.StatusLost:
            case Category.DebuffGained:
            case Category.DebuffLost:
            {
                var pool = category is Category.DebuffGained or Category.DebuffLost ? DebuffStatuses : BuffStatuses;
                var status = GameSheets.Status(pool[random.Next(pool.Length)]);
                r.Word = status.Name.Length > 0 ? status.Name : GameSheets.StatusWord;
                r.IconId = status.Icon;
                r.DamageKind = DamageKind.None;
                return r;
            }

            case Category.Miss:
                r.Word = GameSheets.Miss;
                break;
            case Category.Evade:
                r.Word = GameSheets.Dodge;
                break;
            case Category.Immune:
                r.Word = GameSheets.Invulnerable;
                break;
            case Category.StatusResisted:
                r.Word = GameSheets.Resist;
                break;
            case Category.Interrupted:
                r.Word = GameSheets.Interrupted;
                break;
            case Category.Loot:
                r.Word = GameSheets.ItemWord;
                break;
            case Category.Crafting:
                r.Word = GameSheets.Quality;
                r.HasNumber = true;
                r.Value = random.Next(180, 620);
                break;
            case Category.Resource:
                r.HasNumber = true;
                r.Value = random.Next(4, 11) * 100;
                r.Unit = GameSheets.Mp;
                break;
            case Category.Experience:
                r.HasNumber = true;
                r.Value = random.Next(12_000, 95_000);
                r.Unit = GameSheets.Exp;
                break;
            default:
                r.HasNumber = true;
                r.Value = RollValue(category);
                break;
        }

        if (r.HasNumber && CategoryInfo.Group(category) is CategoryGroup.Outgoing or CategoryGroup.Healing)
        {
            var action = GameSheets.Action(actionId);
            r.Name = action.Name.Length > 0 ? action.Name : null;
            r.IconId = action.Icon;
        }

        r.AggregateTargetKey = (ulong)category << 56;
        r.AggregateActionKey = r.AggregateTargetKey ^ ((ulong)actionId << 24);
        return r;
    }

    private long RollValue(Category category)
    {
        var (min, max) = category switch
        {
            Category.AutoAttack => (2_800, 6_200),
            Category.DamageOverTime => (1_400, 4_200),
            Category.DirectHit => (14_000, 26_000),
            Category.CriticalHit => (22_000, 42_000),
            Category.CriticalDirectHit => (38_000, 68_000),
            Category.Heal or Category.HealReceived => (6_000, 15_000),
            Category.HealOverTime => (1_500, 3_800),
            Category.CriticalHeal => (14_000, 24_000),
            Category.DamageTaken or Category.Blocked or Category.Parried => (4_000, 16_000),
            Category.CriticalDamageTaken => (18_000, 34_000),
            _ => (9_000, 19_000),
        };

        return random.Next(min, max);
    }
}
