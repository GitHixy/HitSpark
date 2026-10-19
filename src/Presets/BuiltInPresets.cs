using System;
using System.Collections.Generic;
using System.Numerics;
using HitSpark.Models;

namespace HitSpark.Presets;

/// <summary>
/// The presets that ship with HitSpark. Each is inspired by how a genre handles combat text,
/// but names, fonts and colours are HitSpark's own. They are rebuilt from code on every load,
/// so tuning here reaches every user; to customise one, duplicate it.
/// </summary>
public static class BuiltInPresets
{
    public const string DefaultId = "builtin.heroic";

    public static IReadOnlyList<Preset> All { get; } = Build();

    private static List<Preset> Build() =>
    [
        ClassicPlus(),
        Heroic(),
        Tally(),
        Onslaught(),
        Elemental(),
        Grim(),
        Minimal(),
        Retro(),
    ];

    // ------------------------------------------------------------------
    // Shared assembly
    // ------------------------------------------------------------------

    /// <summary>The colours a preset hands to its templates.</summary>
    private sealed record Tone(
        Vector4 Auto, Vector4 Hit, Vector4 Dot, Vector4 Direct, Vector4 Crit, Vector4 CritDirect,
        Vector4 Heal, Vector4 Incoming, Vector4 Buff, Vector4 Debuff, Vector4 Faded, Vector4 Word, Vector4 Misc);

    /// <summary>Templates for each family of categories; <see cref="Assemble"/> derives the rest.</summary>
    private sealed class Kit
    {
        public required Tone Tone;
        public required Action<CategoryStyle> Hit;
        public required Action<CategoryStyle> Crit;
        public required Action<CategoryStyle> Heal;
        public required Action<CategoryStyle> Incoming;
        public required Action<CategoryStyle> Word;
        public required Action<CategoryStyle> Status;
        public required Action<CategoryStyle> Misc;
        public string CritSuffix = string.Empty;
        public string CritDirectSuffix = string.Empty;
        public string DirectSuffix = string.Empty;
    }

    private static CategoryStyle From(Action<CategoryStyle> template, Action<CategoryStyle>? tweak = null)
    {
        var style = new CategoryStyle();
        template(style);
        tweak?.Invoke(style);
        return style;
    }

    private static Preset Assemble(string id, string name, string description, Palette palette, Kit k)
    {
        var t = k.Tone;
        var styles = new Dictionary<Category, CategoryStyle>
        {
            [Category.Ability] = From(k.Hit, s => s.Text.Color = t.Hit),
            [Category.AutoAttack] = From(k.Hit, s =>
            {
                s.Text.Color = t.Auto;
                s.Text.Size *= 0.84f;
                s.Motion.PopScale = 1f + (s.Motion.PopScale - 1f) * 0.6f;
            }),
            [Category.DamageOverTime] = From(k.Hit, s =>
            {
                // Ticks get their own colour and a caption, never the damage-type tint,
                // so they read as DoTs at a glance next to direct hits.
                s.Text.Color = t.Dot;
                s.Text.Tint = TintMode.None;
                s.Text.Gradient = false;
                s.Text.Size *= 0.88f;
                s.Text.ShowName = false;
                s.Text.Label = "DoT Tick";
                s.Text.NameScale = 0.68f;
                s.Motion.PopScale = 1f + (s.Motion.PopScale - 1f) * 0.4f;
                s.Motion.Distance *= 0.8f;
            }),
            [Category.DirectHit] = From(k.Hit, s =>
            {
                s.Text.Color = t.Direct;
                s.Text.Size *= 1.08f;
                s.Text.Suffix = k.DirectSuffix;
            }),
            [Category.CriticalHit] = From(k.Crit, s =>
            {
                s.Text.Color = t.Crit;
                s.Text.Suffix = k.CritSuffix;
            }),
            [Category.CriticalDirectHit] = From(k.Crit, s =>
            {
                s.Text.Color = t.CritDirect;
                s.Text.Size *= 1.12f;
                s.Text.Suffix = k.CritDirectSuffix;
                s.Emphasis.PunchScale += 0.15f;
            }),

            [Category.Heal] = From(k.Heal, s => s.Text.Color = t.Heal),
            [Category.HealOverTime] = From(k.Heal, s =>
            {
                s.Text.Color = t.Heal with { W = 0.85f };
                s.Text.Size *= 0.8f;
                s.Motion.PopScale = 1f + (s.Motion.PopScale - 1f) * 0.4f;
            }),
            [Category.CriticalHeal] = From(k.Heal, s =>
            {
                s.Text.Color = Vector4.Lerp(t.Heal, Vector4.One, 0.35f);
                s.Text.Size *= 1.3f;
                s.Text.Suffix = k.CritSuffix;
                s.Emphasis.Punch = true;
                s.Emphasis.PunchScale = 1.35f;
            }),
            [Category.HealReceived] = From(k.Incoming, s =>
            {
                s.Text.Color = t.Heal;
                s.Text.Prefix = "+";
                s.Motion.Anchor = AnchorMode.SelfRight;
            }),

            [Category.DamageTaken] = From(k.Incoming, s => s.Text.Color = t.Incoming),
            [Category.CriticalDamageTaken] = From(k.Incoming, s =>
            {
                s.Text.Color = Vector4.Lerp(t.Incoming, new Vector4(1f, 0.2f, 0.15f, 1f), 0.35f);
                s.Text.Size *= 1.25f;
                s.Text.Suffix = k.CritSuffix;
                s.Emphasis.Punch = true;
                s.Emphasis.PunchScale = 1.35f;
                s.Emphasis.Shake = true;
                s.Emphasis.ShakeAmount = 3f;
            }),
            [Category.Blocked] = From(k.Incoming, s =>
            {
                s.Text.Color = Vector4.Lerp(t.Incoming, new Vector4(0.8f, 0.85f, 0.95f, 1f), 0.5f);
                s.Text.Suffix = " Blocked";
            }),
            [Category.Parried] = From(k.Incoming, s =>
            {
                s.Text.Color = Vector4.Lerp(t.Incoming, new Vector4(0.8f, 0.85f, 0.95f, 1f), 0.5f);
                s.Text.Suffix = " Parried";
            }),

            [Category.Miss] = From(k.Word, s => s.Text.Color = t.Word),
            [Category.Evade] = From(k.Word, s =>
            {
                s.Text.Color = t.Word;
                s.Motion.Anchor = AnchorMode.Self;
            }),
            [Category.Immune] = From(k.Word, s => s.Text.Color = t.Word),

            [Category.StatusGained] = From(k.Status, s =>
            {
                s.Text.Color = t.Buff;
                s.Text.Prefix = "+ ";
            }),
            [Category.StatusLost] = From(k.Status, s =>
            {
                s.Text.Color = t.Faded;
                s.Text.Prefix = "- ";
            }),
            [Category.DebuffGained] = From(k.Status, s =>
            {
                s.Text.Color = t.Debuff;
                s.Text.Prefix = "+ ";
            }),
            [Category.DebuffLost] = From(k.Status, s =>
            {
                s.Text.Color = t.Faded;
                s.Text.Prefix = "- ";
            }),
            [Category.StatusResisted] = From(k.Word, s => s.Text.Color = t.Faded),

            [Category.Resource] = From(k.Misc, s => s.Text.Color = new Vector4(0.62f, 0.8f, 1f, 1f)),
            [Category.Experience] = From(k.Misc, s => s.Text.Color = new Vector4(1f, 0.86f, 0.5f, 1f)),
            [Category.Crafting] = From(k.Misc, s => s.Text.Color = new Vector4(0.8f, 0.95f, 0.75f, 1f)),
            [Category.Loot] = From(k.Misc, s =>
            {
                // The item's own icon says more at a glance than its name.
                s.Text.Color = t.Misc;
                s.Text.Icon = IconMode.Left;
                s.Text.IconScale = Math.Max(s.Text.IconScale, 1.15f);
                s.Motion.Duration = Math.Max(s.Motion.Duration, 2.2f);
            }),
            [Category.Interrupted] = From(k.Word, s => s.Text.Color = t.Incoming),
            [Category.Other] = From(k.Misc, s => s.Text.Color = t.Misc),
        };

        return new Preset
        {
            Id = id,
            Name = name,
            Description = description,
            Author = "HitSpark",
            Palette = palette,
            Styles = styles,
            IsBuiltIn = true,
        };
    }

    private static Vector4 Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);

    // ------------------------------------------------------------------
    // Classic+ - the game's own language, refined
    // ------------------------------------------------------------------

    private static Preset ClassicPlus()
    {
        var tone = new Tone(
            Auto: Rgb(0xF2, 0xEE, 0xE6), Hit: Rgb(0xFF, 0xFF, 0xFF), Dot: Rgb(0xDC, 0x9C, 0xF4),
            Direct: Rgb(0xFF, 0xF4, 0xD6), Crit: Rgb(0xFF, 0xD8, 0x6A), CritDirect: Rgb(0xFF, 0xC2, 0x3D),
            Heal: Rgb(0x8C, 0xF0, 0x8C), Incoming: Rgb(0xFF, 0x74, 0x66),
            Buff: Rgb(0xC9, 0xE8, 0xFF), Debuff: Rgb(0xF0, 0xA8, 0xE0), Faded: Rgb(0xA8, 0xAE, 0xBA),
            Word: Rgb(0xE8, 0xE8, 0xE8), Misc: Rgb(0xE8, 0xE0, 0xCC));

        void Hit(CategoryStyle s)
        {
            s.Text.Font = FontId.GameJupiter;
            s.Text.Size = 32f;
            s.Text.OutlineWidth = 2f;
            s.Text.OutlineColor = Rgb(0x14, 0x10, 0x0C, 0.95f);
            s.Text.ShadowOffset = new Vector2(0f, 2.5f);
            s.Text.ShadowColor = new Vector4(0f, 0f, 0f, 0.5f);
            s.Motion.Anchor = AnchorMode.Actor;
            s.Motion.OffsetY = -10f;
            s.Motion.Duration = 1.3f;
            s.Motion.Trajectory = Trajectory.Rise;
            s.Motion.PathEasing = Easing.CubicOut;
            s.Motion.Distance = 58f;
            s.Motion.Spread = 26f;
            s.Motion.PopScale = 1.22f;
            s.Motion.PopTime = 0.13f;
            s.Motion.PopEasing = Easing.BackOut;
            s.Motion.FadeOutTime = 0.35f;
            s.Motion.EndScale = 0.92f;
        }

        return Assemble("builtin.classic", "Classic+",
            "The game's own look, refined: its serif numerals, a cleaner outline and a gentle lift.",
            new Palette(),
            new Kit
            {
                Tone = tone,
                CritSuffix = "!",
                CritDirectSuffix = "!!",
                Hit = Hit,
                Crit = s =>
                {
                    Hit(s);
                    s.Text.Size = 42f;
                    s.Motion.PopScale = 1f;
                    s.Emphasis.Punch = true;
                    s.Emphasis.PunchScale = 1.4f;
                    s.Emphasis.PunchTime = 0.26f;
                },
                Heal = s =>
                {
                    Hit(s);
                    s.Text.Prefix = "+";
                },
                Incoming = s =>
                {
                    Hit(s);
                    s.Text.Size = 30f;
                    s.Motion.Anchor = AnchorMode.SelfLeft;
                    s.Motion.Trajectory = Trajectory.Rise;
                    s.Motion.Distance = 70f;
                    s.Motion.Spread = 10f;
                },
                Word = s =>
                {
                    Hit(s);
                    s.Text.Font = FontId.GameTrumpGothic;
                    s.Text.Size = 30f;
                    s.Motion.PopScale = 1.1f;
                },
                Status = s =>
                {
                    Hit(s);
                    s.Text.Font = FontId.Interface;
                    s.Text.Size = 20f;
                    s.Text.Icon = IconMode.Left;
                    s.Text.IconScale = 1.25f;
                    s.Motion.OffsetY = -40f;
                    s.Motion.Duration = 1.6f;
                    s.Motion.PopScale = 1.05f;
                },
                Misc = s =>
                {
                    Hit(s);
                    s.Text.Font = FontId.GameTrumpGothic;
                    s.Text.Size = 26f;
                    s.Motion.Anchor = AnchorMode.SelfRight;
                    s.Motion.Duration = 1.8f;
                    s.Motion.PopScale = 1.08f;
                },
            });
    }

    // ------------------------------------------------------------------
    // Heroic - scrolling combat text: arcs over targets, side columns, a big crit pop
    // ------------------------------------------------------------------

    private static Preset Heroic()
    {
        var tone = new Tone(
            Auto: Rgb(0xFF, 0xFF, 0xFF), Hit: Rgb(0xFF, 0xE4, 0x4D), Dot: Rgb(0xE0, 0x70, 0xFF),
            Direct: Rgb(0xFF, 0xEE, 0x8A), Crit: Rgb(0xFF, 0xC8, 0x1F), CritDirect: Rgb(0xFF, 0x9E, 0x1A),
            Heal: Rgb(0x4D, 0xF5, 0x5A), Incoming: Rgb(0xFF, 0x3B, 0x30),
            Buff: Rgb(0x7F, 0xD8, 0xFF), Debuff: Rgb(0xD0, 0x6A, 0xFF), Faded: Rgb(0x9A, 0x9A, 0xA0),
            Word: Rgb(0xFF, 0xFF, 0xFF), Misc: Rgb(0xFF, 0xF0, 0xB8));

        void Hit(CategoryStyle s)
        {
            s.Text.Font = FontId.RussoOne;
            s.Text.Size = 30f;
            s.Text.OutlineWidth = 2.2f;
            s.Text.OutlineColor = new Vector4(0f, 0f, 0f, 1f);
            s.Text.ShadowOffset = new Vector2(1.5f, 3f);
            s.Text.ShadowColor = new Vector4(0f, 0f, 0f, 0.45f);
            s.Motion.Anchor = AnchorMode.Actor;
            s.Motion.OffsetY = -20f;
            s.Motion.Duration = 1.35f;
            s.Motion.Trajectory = Trajectory.Arc;
            s.Motion.ArcDirection = ArcDirection.Alternate;
            s.Motion.PathEasing = Easing.QuadOut;
            s.Motion.Distance = 95f;
            s.Motion.Spread = 110f;
            s.Motion.PopScale = 1.5f;
            s.Motion.PopTime = 0.16f;
            s.Motion.PopEasing = Easing.BackOut;
            s.Motion.FadeOutTime = 0.4f;
            s.Motion.EndScale = 0.85f;
        }

        return Assemble("builtin.heroic", "Heroic",
            "Classic scrolling combat text: yellow numbers arc off your targets, crits slam in huge, and what hits you scrolls up a column beside you.",
            new Palette(),
            new Kit
            {
                Tone = tone,
                Hit = Hit,
                Crit = s =>
                {
                    Hit(s);
                    s.Text.Size = 46f;
                    s.Text.OutlineWidth = 2.8f;
                    s.Motion.Trajectory = Trajectory.Rise;
                    s.Motion.Distance = 50f;
                    s.Motion.Spread = 20f;
                    s.Motion.Duration = 1.5f;
                    s.Motion.PopScale = 1f;
                    s.Emphasis.Punch = true;
                    s.Emphasis.PunchScale = 1.75f;
                    s.Emphasis.PunchTime = 0.3f;
                    s.Emphasis.Flash = true;
                    s.Emphasis.FlashTime = 0.12f;
                },
                Heal = s =>
                {
                    Hit(s);
                    s.Text.Prefix = "+";
                    s.Motion.Trajectory = Trajectory.Rise;
                    s.Motion.Distance = 70f;
                },
                Incoming = s =>
                {
                    Hit(s);
                    s.Text.Size = 28f;
                    s.Text.Prefix = "-";
                    s.Motion.Anchor = AnchorMode.SelfLeft;
                    s.Motion.OffsetY = 20f;
                    s.Motion.Trajectory = Trajectory.Lane;
                    s.Motion.Distance = 150f;
                    s.Motion.Duration = 1.7f;
                    s.Motion.PopScale = 1.25f;
                    s.Motion.PopTime = 0.1f;
                },
                Word = s =>
                {
                    Hit(s);
                    s.Text.Size = 26f;
                    s.Motion.Trajectory = Trajectory.Rise;
                    s.Motion.Distance = 50f;
                    s.Motion.PopScale = 1.2f;
                },
                Status = s =>
                {
                    Hit(s);
                    s.Text.Size = 22f;
                    s.Text.Icon = IconMode.Left;
                    s.Text.IconScale = 1.2f;
                    s.Motion.OffsetY = -60f;
                    s.Motion.Trajectory = Trajectory.Rise;
                    s.Motion.Distance = 40f;
                    s.Motion.PopScale = 1.1f;
                    s.Motion.Duration = 1.6f;
                },
                Misc = s =>
                {
                    Hit(s);
                    s.Text.Size = 22f;
                    s.Motion.Anchor = AnchorMode.SelfRight;
                    s.Motion.Trajectory = Trajectory.Lane;
                    s.Motion.Distance = 120f;
                    s.Motion.Duration = 2f;
                    s.Motion.PopScale = 1.1f;
                },
            });
    }

    // ------------------------------------------------------------------
    // Tally - aggregation first, colour per damage source, compact
    // ------------------------------------------------------------------

    private static Preset Tally()
    {
        var tone = new Tone(
            Auto: Rgb(0xE8, 0xE8, 0xE8), Hit: Rgb(0xFF, 0xFF, 0xFF), Dot: Rgb(0xD0, 0x84, 0xFF),
            Direct: Rgb(0xFF, 0xFF, 0xFF), Crit: Rgb(0xFF, 0xA8, 0x3A), CritDirect: Rgb(0xFF, 0x82, 0x2E),
            Heal: Rgb(0x6C, 0xE8, 0x8A), Incoming: Rgb(0xF0, 0x55, 0x55),
            Buff: Rgb(0x9C, 0xD4, 0xFF), Debuff: Rgb(0xE0, 0x8C, 0xFF), Faded: Rgb(0x94, 0x9A, 0xA6),
            Word: Rgb(0xD8, 0xDC, 0xE4), Misc: Rgb(0xE0, 0xDA, 0xC8));

        void Hit(CategoryStyle s)
        {
            s.Text.Font = FontId.BarlowCondensed;
            s.Text.Size = 27f;
            s.Text.Tint = TintMode.DamageType;
            s.Text.OutlineWidth = 1.8f;
            s.Text.OutlineColor = new Vector4(0.03f, 0.03f, 0.05f, 0.95f);
            s.Text.Shadow = false;
            s.Text.Icon = IconMode.Left;
            s.Text.IconScale = 0.9f;
            s.Text.Abbreviate = true;
            s.Text.AbbreviateFrom = 1_000_000;
            s.Motion.Anchor = AnchorMode.Actor;
            s.Motion.Duration = 1.15f;
            s.Motion.Trajectory = Trajectory.Rise;
            s.Motion.PathEasing = Easing.QuartOut;
            s.Motion.Distance = 42f;
            s.Motion.Spread = 60f;
            s.Motion.PopScale = 1.18f;
            s.Motion.PopTime = 0.1f;
            s.Motion.PopEasing = Easing.CubicOut;
            s.Motion.FadeOutTime = 0.3f;
            s.Motion.EndScale = 0.95f;
            s.Emphasis.Punch = true;
            s.Emphasis.PunchScale = 1.12f;
            s.Emphasis.PunchTime = 0.16f;
            s.Aggregation.Enabled = true;
            s.Aggregation.WindowMs = 600f;
            s.Aggregation.Key = AggregateKey.Action;
        }

        var preset = Assemble("builtin.tally", "Tally",
            "Compact and informative: multi-hits and ticks roll up into one number, tinted by damage type, with the action icon.",
            new Palette
            {
                Physical = Rgb(0xFF, 0xE6, 0xC8),
                Magical = Rgb(0x8F, 0xD6, 0xFF),
                Unique = Rgb(0xC9, 0x9C, 0xFF),
            },
            new Kit
            {
                Tone = tone,
                Hit = Hit,
                Crit = s =>
                {
                    Hit(s);
                    s.Text.Size = 34f;
                    s.Text.Tint = TintMode.None;
                    s.Emphasis.PunchScale = 1.35f;
                    s.Emphasis.PunchTime = 0.22f;
                },
                Heal = s =>
                {
                    Hit(s);
                    s.Text.Tint = TintMode.None;
                    s.Text.Prefix = "+";
                },
                Incoming = s =>
                {
                    Hit(s);
                    s.Text.Tint = TintMode.None;
                    s.Text.Prefix = "-";
                    s.Motion.Anchor = AnchorMode.SelfLeft;
                    s.Motion.Trajectory = Trajectory.Lane;
                    s.Motion.Distance = 110f;
                    s.Motion.Duration = 1.5f;
                    s.Aggregation.WindowMs = 350f;
                },
                Word = s =>
                {
                    Hit(s);
                    s.Text.Tint = TintMode.None;
                    s.Aggregation.Enabled = false;
                },
                Status = s =>
                {
                    Hit(s);
                    s.Text.Tint = TintMode.None;
                    s.Text.Size = 21f;
                    s.Text.IconScale = 1.2f;
                    s.Motion.OffsetY = -44f;
                    s.Motion.Duration = 1.5f;
                    s.Aggregation.Enabled = false;
                },
                Misc = s =>
                {
                    Hit(s);
                    s.Text.Tint = TintMode.None;
                    s.Text.Size = 22f;
                    s.Motion.Anchor = AnchorMode.SelfRight;
                    s.Motion.Duration = 1.8f;
                    s.Aggregation.Key = AggregateKey.Target;
                    s.Aggregation.WindowMs = 1200f;
                },
            });

        // Ticks gather per target so a stack of DoTs reads as one climbing number.
        foreach (var category in new[] { Category.DamageOverTime, Category.HealOverTime })
        {
            var s = preset.Styles[category];
            s.Aggregation.Key = AggregateKey.Target;
            s.Aggregation.WindowMs = 1400f;
        }

        // Each EXP gain is its own event, as in the chat log: two kills landing together must not
        // read as one sum, and the exact figure matters more than a short one.
        var exp = preset.Styles[Category.Experience];
        exp.Aggregation.Enabled = false;
        exp.Text.Abbreviate = false;

        return preset;
    }

    // ------------------------------------------------------------------
    // Onslaught - huge bold crits, shake and sparks, stacked combos
    // ------------------------------------------------------------------

    private static Preset Onslaught()
    {
        var tone = new Tone(
            Auto: Rgb(0xF4, 0xF4, 0xF4), Hit: Rgb(0xFF, 0xFF, 0xFF), Dot: Rgb(0xD8, 0x6A, 0xFF),
            Direct: Rgb(0xFF, 0xF1, 0xC4), Crit: Rgb(0xFF, 0xD9, 0x3B), CritDirect: Rgb(0xFF, 0xB3, 0x1F),
            Heal: Rgb(0x6D, 0xF2, 0x7C), Incoming: Rgb(0xFF, 0x3E, 0x36),
            Buff: Rgb(0x8A, 0xD6, 0xFF), Debuff: Rgb(0xE0, 0x6F, 0xFF), Faded: Rgb(0x9E, 0x9E, 0xA6),
            Word: Rgb(0xFF, 0xFF, 0xFF), Misc: Rgb(0xFF, 0xEE, 0xC0));

        void Hit(CategoryStyle s)
        {
            s.Text.Font = FontId.Anton;
            s.Text.Size = 36f;
            s.Text.OutlineWidth = 3f;
            s.Text.OutlineColor = Rgb(0x10, 0x08, 0x04, 1f);
            s.Text.ShadowOffset = new Vector2(2f, 4f);
            s.Text.ShadowColor = new Vector4(0f, 0f, 0f, 0.5f);
            s.Text.Gradient = true;
            s.Text.GradientColor = Rgb(0xC8, 0xC8, 0xC8);
            s.Motion.Anchor = AnchorMode.Actor;
            s.Motion.OffsetY = -10f;
            s.Motion.Duration = 1.05f;
            s.Motion.Trajectory = Trajectory.Rise;
            s.Motion.PathEasing = Easing.ExpoOut;
            s.Motion.Distance = 34f;
            s.Motion.Spread = 70f;
            s.Motion.PopScale = 1.7f;
            s.Motion.PopTime = 0.09f;
            s.Motion.PopEasing = Easing.CubicOut;
            s.Motion.FadeOutTime = 0.22f;
            s.Motion.EndScale = 1.1f;
            s.Emphasis.Punch = true;
            s.Emphasis.PunchScale = 1.15f;
            s.Emphasis.PunchTime = 0.14f;
        }

        return Assemble("builtin.onslaught", "Onslaught",
            "Action-game impact: heavy numbers that slam in, crits that shake and throw sparks, combos stacking on the target.",
            new Palette(),
            new Kit
            {
                Tone = tone,
                Hit = Hit,
                Crit = s =>
                {
                    Hit(s);
                    s.Text.Size = 60f;
                    s.Text.OutlineWidth = 3.5f;
                    s.Text.GradientColor = Rgb(0xFF, 0x6A, 0x10);
                    s.Motion.PopScale = 1f;
                    s.Motion.Duration = 1.2f;
                    s.Motion.Tilt = 6f;
                    s.Emphasis.PunchScale = 1.9f;
                    s.Emphasis.PunchTime = 0.3f;
                    s.Emphasis.Shake = true;
                    s.Emphasis.ShakeAmount = 7f;
                    s.Emphasis.ShakeTime = 0.3f;
                    s.Emphasis.Flash = true;
                    s.Emphasis.FlashTime = 0.1f;
                    s.Emphasis.Sparks = true;
                    s.Emphasis.SparkCount = 14;
                    s.Emphasis.SparkSpeed = 380f;
                    s.Emphasis.SparkColor = Rgb(0xFF, 0xC4, 0x4A);
                    s.Emphasis.Ghost = true;
                    s.Emphasis.GhostCount = 3;
                },
                Heal = s =>
                {
                    Hit(s);
                    s.Text.Prefix = "+";
                    s.Text.GradientColor = Rgb(0x2E, 0xB8, 0x50);
                },
                Incoming = s =>
                {
                    Hit(s);
                    s.Text.Size = 32f;
                    s.Text.GradientColor = Rgb(0xA0, 0x10, 0x10);
                    s.Motion.Anchor = AnchorMode.SelfLeft;
                    s.Motion.Trajectory = Trajectory.Fall;
                    s.Motion.Distance = 40f;
                    s.Motion.Spread = 30f;
                    s.Emphasis.Shake = true;
                    s.Emphasis.ShakeAmount = 3f;
                },
                Word = s =>
                {
                    Hit(s);
                    s.Text.Size = 30f;
                    s.Text.Gradient = false;
                },
                Status = s =>
                {
                    Hit(s);
                    s.Text.Size = 22f;
                    s.Text.Gradient = false;
                    s.Text.OutlineWidth = 2f;
                    s.Text.Icon = IconMode.Left;
                    s.Text.IconScale = 1.2f;
                    s.Motion.OffsetY = -60f;
                    s.Motion.Duration = 1.5f;
                    s.Motion.PopScale = 1.2f;
                    s.Emphasis.Punch = false;
                },
                Misc = s =>
                {
                    Hit(s);
                    s.Text.Size = 24f;
                    s.Text.Gradient = false;
                    s.Motion.Anchor = AnchorMode.SelfRight;
                    s.Motion.Duration = 1.8f;
                    s.Emphasis.Punch = false;
                },
            });
    }

    // ------------------------------------------------------------------
    // Elemental - bouncy fountains, soft colours, playful easing
    // ------------------------------------------------------------------

    private static Preset Elemental()
    {
        var tone = new Tone(
            Auto: Rgb(0xFF, 0xFF, 0xFF), Hit: Rgb(0xFF, 0xFF, 0xFF), Dot: Rgb(0xE8, 0xA8, 0xFF),
            Direct: Rgb(0xFF, 0xF6, 0xE0), Crit: Rgb(0xFF, 0xD4, 0x7A), CritDirect: Rgb(0xFF, 0xB8, 0x8A),
            Heal: Rgb(0x9C, 0xF5, 0xA8), Incoming: Rgb(0xFF, 0x8A, 0x8A),
            Buff: Rgb(0xA8, 0xE6, 0xFF), Debuff: Rgb(0xE8, 0xB0, 0xFF), Faded: Rgb(0xB8, 0xBC, 0xC8),
            Word: Rgb(0xF0, 0xF0, 0xF8), Misc: Rgb(0xFF, 0xF0, 0xD0));

        void Hit(CategoryStyle s)
        {
            s.Text.Font = FontId.LilitaOne;
            s.Text.Size = 32f;
            s.Text.Tint = TintMode.DamageType;
            s.Text.OutlineWidth = 1.6f;
            s.Text.OutlineColor = new Vector4(0.12f, 0.1f, 0.2f, 0.75f);
            s.Text.ShadowOffset = new Vector2(0f, 3f);
            s.Text.ShadowColor = new Vector4(0.1f, 0.05f, 0.2f, 0.35f);
            s.Motion.Anchor = AnchorMode.Actor;
            s.Motion.OffsetY = -20f;
            s.Motion.Duration = 1.25f;
            s.Motion.Trajectory = Trajectory.Fountain;
            s.Motion.ArcDirection = ArcDirection.Random;
            s.Motion.Distance = 70f;
            s.Motion.Spread = 80f;
            s.Motion.PopScale = 0.35f;
            s.Motion.PopTime = 0.32f;
            s.Motion.PopEasing = Easing.ElasticOut;
            s.Motion.FadeOutTime = 0.3f;
            s.Motion.EndScale = 0.7f;
        }

        return Assemble("builtin.elemental", "Elemental",
            "Playful and bright: numbers spring out of targets with an elastic bounce, in soft colours for each damage type.",
            new Palette
            {
                Physical = Rgb(0xFF, 0xF4, 0xE4),
                Magical = Rgb(0x9E, 0xE6, 0xFF),
                Unique = Rgb(0xD8, 0xA8, 0xFF),
            },
            new Kit
            {
                Tone = tone,
                Hit = Hit,
                Crit = s =>
                {
                    Hit(s);
                    s.Text.Size = 42f;
                    s.Text.Tint = TintMode.None;
                    s.Text.Gradient = true;
                    s.Text.GradientColor = Rgb(0xFF, 0x9C, 0x6E);
                    s.Motion.Tilt = 9f;
                    s.Motion.Duration = 1.4f;
                    s.Emphasis.Sparks = true;
                    s.Emphasis.SparkCount = 9;
                    s.Emphasis.SparkSpeed = 220f;
                    s.Emphasis.SparkColor = Rgb(0xFF, 0xE8, 0xA8);
                },
                Heal = s =>
                {
                    Hit(s);
                    s.Text.Tint = TintMode.None;
                    s.Text.Prefix = "+";
                    s.Motion.Trajectory = Trajectory.Rise;
                    s.Motion.Distance = 60f;
                    s.Motion.PathEasing = Easing.BackOut;
                },
                Incoming = s =>
                {
                    Hit(s);
                    s.Text.Tint = TintMode.None;
                    s.Text.Size = 28f;
                    s.Motion.Anchor = AnchorMode.SelfLeft;
                    s.Motion.Trajectory = Trajectory.Fall;
                    s.Motion.PathEasing = Easing.BounceOut;
                    s.Motion.Distance = 36f;
                    s.Motion.Spread = 24f;
                },
                Word = s =>
                {
                    Hit(s);
                    s.Text.Tint = TintMode.None;
                    s.Text.Size = 26f;
                    s.Motion.Trajectory = Trajectory.Rise;
                    s.Motion.Distance = 40f;
                },
                Status = s =>
                {
                    Hit(s);
                    s.Text.Tint = TintMode.None;
                    s.Text.Size = 22f;
                    s.Text.Icon = IconMode.Left;
                    s.Text.IconScale = 1.2f;
                    s.Motion.OffsetY = -55f;
                    s.Motion.Trajectory = Trajectory.Rise;
                    s.Motion.Distance = 34f;
                    s.Motion.PopScale = 0.6f;
                    s.Motion.Duration = 1.5f;
                },
                Misc = s =>
                {
                    Hit(s);
                    s.Text.Tint = TintMode.None;
                    s.Text.Size = 22f;
                    s.Motion.Anchor = AnchorMode.SelfRight;
                    s.Motion.Trajectory = Trajectory.Rise;
                    s.Motion.Duration = 1.8f;
                    s.Motion.PopScale = 0.6f;
                },
            });
    }

    // ------------------------------------------------------------------
    // Grim - heavy numbers that shrink and fade, burning crits
    // ------------------------------------------------------------------

    private static Preset Grim()
    {
        var tone = new Tone(
            Auto: Rgb(0xD8, 0xD2, 0xC6), Hit: Rgb(0xF0, 0xEA, 0xDC), Dot: Rgb(0xB8, 0x6C, 0xE0),
            Direct: Rgb(0xF4, 0xE4, 0xC0), Crit: Rgb(0xFF, 0x8A, 0x1E), CritDirect: Rgb(0xFF, 0x62, 0x14),
            Heal: Rgb(0x7E, 0xD0, 0x6A), Incoming: Rgb(0xE0, 0x3A, 0x2A),
            Buff: Rgb(0xC8, 0xB8, 0x8C), Debuff: Rgb(0xB0, 0x5A, 0xC8), Faded: Rgb(0x86, 0x80, 0x78),
            Word: Rgb(0xC8, 0xC0, 0xB0), Misc: Rgb(0xD8, 0xC8, 0xA0));

        void Hit(CategoryStyle s)
        {
            s.Text.Font = FontId.CinzelDecorative;
            s.Text.Size = 34f;
            s.Text.OutlineWidth = 2.4f;
            s.Text.OutlineColor = Rgb(0x0C, 0x06, 0x02, 1f);
            s.Text.ShadowOffset = new Vector2(0f, 3f);
            s.Text.ShadowColor = new Vector4(0f, 0f, 0f, 0.6f);
            s.Motion.Anchor = AnchorMode.Actor;
            s.Motion.Duration = 1.1f;
            s.Motion.Trajectory = Trajectory.Rise;
            s.Motion.PathEasing = Easing.CubicOut;
            s.Motion.Distance = 26f;
            s.Motion.Spread = 60f;
            s.Motion.PopScale = 1.25f;
            s.Motion.PopTime = 0.12f;
            s.Motion.PopEasing = Easing.QuadOut;
            s.Motion.FadeOutTime = 0.6f;
            s.Motion.EndScale = 0.5f;
        }

        return Assemble("builtin.grim", "Grim",
            "Dark and weighty: carved numerals that shrink and fade where they land, with burning orange crits.",
            new Palette(),
            new Kit
            {
                Tone = tone,
                Hit = Hit,
                Crit = s =>
                {
                    Hit(s);
                    s.Text.Size = 46f;
                    s.Text.Gradient = true;
                    s.Text.GradientColor = Rgb(0xC4, 0x2A, 0x08);
                    s.Motion.PopScale = 1f;
                    s.Motion.Duration = 1.3f;
                    s.Emphasis.Punch = true;
                    s.Emphasis.PunchScale = 1.4f;
                    s.Emphasis.PunchTime = 0.28f;
                    s.Emphasis.Sparks = true;
                    s.Emphasis.SparkCount = 8;
                    s.Emphasis.SparkSpeed = 180f;
                    s.Emphasis.SparkColor = Rgb(0xFF, 0x7A, 0x1A);
                },
                Heal = s =>
                {
                    Hit(s);
                    s.Text.Prefix = "+";
                },
                Incoming = s =>
                {
                    Hit(s);
                    s.Text.Size = 30f;
                    s.Motion.Anchor = AnchorMode.SelfLeft;
                    s.Motion.Distance = 40f;
                    s.Motion.Spread = 16f;
                },
                Word = s =>
                {
                    Hit(s);
                    s.Text.Size = 26f;
                },
                Status = s =>
                {
                    Hit(s);
                    s.Text.Font = FontId.Interface;
                    s.Text.Size = 20f;
                    s.Text.Icon = IconMode.Left;
                    s.Text.IconScale = 1.25f;
                    s.Motion.OffsetY = -50f;
                    s.Motion.Duration = 1.6f;
                    s.Motion.EndScale = 0.8f;
                },
                Misc = s =>
                {
                    Hit(s);
                    s.Text.Size = 22f;
                    s.Motion.Anchor = AnchorMode.SelfRight;
                    s.Motion.Duration = 1.8f;
                    s.Motion.EndScale = 0.8f;
                },
            });
    }

    // ------------------------------------------------------------------
    // Minimal - small, quiet, fade only
    // ------------------------------------------------------------------

    private static Preset Minimal()
    {
        var tone = new Tone(
            Auto: Rgb(0xE6, 0xE6, 0xE6), Hit: Rgb(0xFF, 0xFF, 0xFF), Dot: Rgb(0xD8, 0xB8, 0xE8),
            Direct: Rgb(0xFF, 0xFF, 0xFF), Crit: Rgb(0xFF, 0xE0, 0x8A), CritDirect: Rgb(0xFF, 0xD0, 0x6A),
            Heal: Rgb(0x9E, 0xE8, 0xA6), Incoming: Rgb(0xFF, 0x9A, 0x90),
            Buff: Rgb(0xC0, 0xDC, 0xF0), Debuff: Rgb(0xE0, 0xC0, 0xE8), Faded: Rgb(0xA0, 0xA4, 0xAC),
            Word: Rgb(0xD8, 0xD8, 0xD8), Misc: Rgb(0xE0, 0xDC, 0xD0));

        void Hit(CategoryStyle s)
        {
            s.Text.Font = FontId.Barlow;
            s.Text.Size = 22f;
            s.Text.OutlineWidth = 1.4f;
            s.Text.OutlineColor = new Vector4(0f, 0f, 0f, 0.8f);
            s.Text.Shadow = false;
            s.Motion.Anchor = AnchorMode.Actor;
            s.Motion.OffsetY = -10f;
            s.Motion.Duration = 1.4f;
            s.Motion.Trajectory = Trajectory.Fixed;
            s.Motion.Distance = 0f;
            s.Motion.Spread = 40f;
            s.Motion.PopScale = 1f;
            s.Motion.PopTime = 0f;
            s.Motion.FadeInTime = 0.12f;
            s.Motion.FadeOutTime = 0.55f;
            s.Motion.EndScale = 1f;
        }

        return Assemble("builtin.minimal", "Minimal",
            "Small, quiet and still: numbers fade in and out without moving. Easy on the eyes and on motion sensitivity.",
            new Palette(),
            new Kit
            {
                Tone = tone,
                Hit = Hit,
                Crit = s =>
                {
                    Hit(s);
                    s.Text.Size = 27f;
                },
                Heal = s =>
                {
                    Hit(s);
                    s.Text.Prefix = "+";
                },
                Incoming = s =>
                {
                    Hit(s);
                    s.Text.Prefix = "-";
                    s.Motion.Anchor = AnchorMode.SelfLeft;
                    s.Motion.Spread = 12f;
                },
                Word = Hit,
                Status = s =>
                {
                    Hit(s);
                    s.Text.Font = FontId.Interface;
                    s.Text.Size = 17f;
                    s.Text.Icon = IconMode.Left;
                    s.Text.IconScale = 1.2f;
                    s.Motion.OffsetY = -36f;
                },
                Misc = s =>
                {
                    Hit(s);
                    s.Text.Size = 18f;
                    s.Motion.Anchor = AnchorMode.SelfRight;
                    s.Motion.Duration = 1.8f;
                },
            });
    }

    // ------------------------------------------------------------------
    // Retro - pixel font, stepped motion, hard shadows
    // ------------------------------------------------------------------

    private static Preset Retro()
    {
        var tone = new Tone(
            Auto: Rgb(0xFC, 0xFC, 0xFC), Hit: Rgb(0xFC, 0xFC, 0xFC), Dot: Rgb(0xD8, 0x00, 0xCC),
            Direct: Rgb(0xFC, 0xE0, 0xA8), Crit: Rgb(0xF8, 0xB8, 0x00), CritDirect: Rgb(0xF8, 0x38, 0x00),
            Heal: Rgb(0x58, 0xF8, 0x98), Incoming: Rgb(0xF8, 0x38, 0x00),
            Buff: Rgb(0x3C, 0xBC, 0xFC), Debuff: Rgb(0xD8, 0x00, 0xCC), Faded: Rgb(0x7C, 0x7C, 0x7C),
            Word: Rgb(0xFC, 0xFC, 0xFC), Misc: Rgb(0xF8, 0xD8, 0x78));

        void Hit(CategoryStyle s)
        {
            s.Text.Font = FontId.PressStart2P;
            s.Text.Size = 20f;
            s.Text.OutlineWidth = 0f;
            s.Text.Shadow = true;
            s.Text.ShadowOffset = new Vector2(3f, 3f);
            s.Text.ShadowColor = new Vector4(0f, 0f, 0f, 1f);
            s.Motion.Anchor = AnchorMode.Actor;
            s.Motion.OffsetY = -10f;
            s.Motion.Duration = 1.2f;
            s.Motion.Trajectory = Trajectory.Rise;
            s.Motion.PathEasing = Easing.Linear;
            s.Motion.Distance = 48f;
            s.Motion.Spread = 40f;
            s.Motion.PopScale = 1f;
            s.Motion.PopTime = 0f;
            s.Motion.FadeInTime = 0f;
            s.Motion.FadeOutTime = 0.2f;
            s.Motion.EndScale = 1f;
            s.Motion.StepRate = 12f;
        }

        return Assemble("builtin.retro", "Retro",
            "Eight-bit charm: a pixel font, hard drop shadows and motion that steps like an old console.",
            new Palette(),
            new Kit
            {
                Tone = tone,
                CritSuffix = "!",
                CritDirectSuffix = "!!",
                Hit = Hit,
                Crit = s =>
                {
                    Hit(s);
                    s.Text.Size = 28f;
                    s.Emphasis.Flash = true;
                    s.Emphasis.FlashTime = 0.25f;
                    s.Emphasis.Shake = true;
                    s.Emphasis.ShakeAmount = 4f;
                    s.Emphasis.ShakeTime = 0.25f;
                },
                Heal = s =>
                {
                    Hit(s);
                    s.Text.Prefix = "+";
                },
                Incoming = s =>
                {
                    Hit(s);
                    s.Text.Prefix = "-";
                    s.Motion.Anchor = AnchorMode.SelfLeft;
                    s.Motion.Trajectory = Trajectory.Lane;
                    s.Motion.Distance = 90f;
                },
                Word = s =>
                {
                    Hit(s);
                    s.Text.Size = 16f;
                },
                Status = s =>
                {
                    Hit(s);
                    s.Text.Size = 13f;
                    s.Text.Icon = IconMode.Left;
                    s.Text.IconScale = 1.8f;
                    s.Motion.OffsetY = -44f;
                    s.Motion.Distance = 30f;
                },
                Misc = s =>
                {
                    Hit(s);
                    s.Text.Size = 14f;
                    s.Motion.Anchor = AnchorMode.SelfRight;
                    s.Motion.Duration = 1.8f;
                },
            });
    }
}
