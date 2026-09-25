# HitSpark — Technical Design

Status: **approved** (M0), implementation in progress. Everything marked *Hypothesis* is verified in game during M1
before later milestones build on it.

API facts below were checked on 2026-09-25 against Dalamud `master` (API 15, installed build
15.0.3.5) and FFXIVClientStructs `main` of the same day — not from memory.

---

## 1. How the game produces combat text

```
server packet ──► ActionEffectHandler.Receive (per action, all targets)
                     │
                     ▼
        BattleLog.AddToScreenLogWithScreenLogKind(target, source, kind, option,
                                                  actionKind, actionId, v1, v2, v3)
                     │   (also AddToScreenLogWithLogMessageId → converts to a kind)
                     ▼
        ScreenLog.AddScreenLogEntry(StdDeque<ScreenLogEntry>* queue, ScreenLogEntry* entry)
                     │   queue = Character::GetScreenLogManager() of some actor
                     │   entry = { kind, SourceRelation, TargetRelation, option,
                     │             actionKind, actionId, v1, v2, v3, timestamp }
                     ▼
        AgentScreenLog drains the queues (throttled, staggered) ─┬─► _FlyText   (side lanes, local player)
                                                                  └─► _PopUpText (above other actors)
                                                                            │
        AddonFlyText.AddFlyText ─► AddonFlyText.CreateFlyText  ◄── Dalamud IFlyTextGui.FlyTextCreated
```

Key facts that shape the design:

| Fact | Source | Consequence |
|---|---|---|
| `IFlyTextGui.FlyTextCreated` hooks **only** `AddonFlyText.CreateFlyText`. | Dalamud `FlyTextGui.cs` | It never sees **pop-up text** (numbers over other actors). It cannot be the only suppression point. |
| `FlyTextCreated` fires during the addon update, **not** synchronously inside the screen-log call. | same | Correlating it with source/target needs a matching queue (what DamageInfo does), not a "current event" field. |
| `BattleLog.AddToScreenLogWithScreenLogKind` and `ScreenLog.AddScreenLogEntry` are in ClientStructs with maintained signatures. | ClientStructs `BattleLog.cs`, `UI/Misc/ScreenLog.cs` | Hook via `Addresses.X.Value` — no private signatures to maintain (CBT/DamageInfo/FlyTextFilter all hardcode the same sigs). |
| `ScreenLogEntry` already carries `SourceRelation` / `TargetRelation` (`LocalPlayer`, `PartyMember`, `Other`, `Enemy`) as computed by the game. | `ScreenLog.cs` | The You/Party/Others filter and the **fly vs pop-up** split come from the game itself: fly text when source or target is `LocalPlayer`, pop-up otherwise. |
| Every `Character` owns its own screen-log deque. | `Character.GetScreenLogManager()` (vfunc 83) | The queue pointer tells us which actor the text belongs to. |
| Damage type is in `v3` (1 physical, 2 magical, 3 unique/darkness); crit/DH is encoded in the kind. | DamageInfo, FlyTextFilter notes | No `ActionEffectHandler` hook needed for M1–M3. |
| Skipping the original `AddToScreenLogWithScreenLogKind` suppresses both fly and pop-up text. | FlyTextFilter behaviour | This is the established suppression technique for pop-ups. |
| DoT/HoT ticks reach the screen log with the afflicted actor as their own source (`src=Enemy tgt=Enemy`, source = target). Verified in game 2026-09-25 (AST Combust III on a dummy: `action=0:0`, status id in `v1` of the Debuff entry). | in-game event log | Ticks are attributed through `StatusManager.ProcessHotDot` (optional hook, carries `sourceEntityId`), falling back to debuffs on the target applied by you or your party. |

### Verification log

| Hypothesis | Status (2026-09-25) |
|---|---|
| H1 — screen-log calls are nested and synchronous | ✅ Verified: every entry carried its outer call's source/target; `ProcessHotDot` also nests. |
| H2 — the queue owner is the actor the text is drawn on | ✅ Pop-up text on a target (`queue=target`) and heals on the local player verified in game. Incoming damage from a live enemy not yet observed. |
| H3 — native display settings are applied before queueing | ❌ **False** (re-tested 2026-09-25: HitSpark kept drawing with the game's flying text off; the first test was masked by an unrelated invisible-text issue). HitSpark now reads `UiControlOption.FlyTextDisp` / `PopUpTextDisp` via `IGameConfig` per event and hands events back to the game when the matching option is off (fly text = you are source or target, as the game classifies it). Verified: with flying text off and pop-up on, the game natively still shows your DoT ticks (they are logged enemy-on-enemy), and HitSpark matches it. |
| H4 — ticks carry no action; auto-attacks carry Attack/Shot | ✅ Verified on AST (Combust III ticks `action=0:0`, autos `action=1:7`, value 1 matches native). |
| Status id in `v1` of Buff/Debuff entries | ✅ Verified (Combust III → 1881). |
| Tick status id | ❌ Server sends 0; resolved from the source's sole debuff on the target. |

## 2. Event capture and actor correlation

### 2.1 Hooks

| Hook | Purpose | Required |
|---|---|---|
| `ScreenLog.AddScreenLogEntry` | **Primary.** The point where the game commits to showing a text. Gives kind, relations, action, values and (via the queue) the owning actor. Skipping `Original` suppresses the native text. | yes |
| `BattleLog.AddToScreenLogWithScreenLogKind` | Enrichment: real `source`/`target` pointers (pets, "from X" labels, aggregation keys). Stores them in a one-slot "current call" context read by the nested `AddScreenLogEntry`. | yes |
| `IFlyTextGui.FlyTextCreated` | Safety net + diagnostics: logs everything that still reaches native fly text, and suppresses stragglers for owned categories. Also gives us the already-localised `text1/text2` (status names) and `icon` for kinds we let through. | no (Dalamud API) |

All three run on the game's main thread; `AddScreenLogEntry` is called synchronously from
`AddToScreenLogWithScreenLogKind`, so a plain field (not a queue) carries the context between
them. *Hypothesis H1: the call is synchronous and nested* — logged in M1.

### 2.2 Queue → actor

A small cache maps `StdDeque*` → `(EntityId, ObjectIndex)`. Built lazily: on a miss, walk the
object table characters (≤ ~600 slots, only on the first event per actor) and compare
`GetScreenLogManager()`. Invalidated on territory change. No per-frame cost.

*Hypothesis H2: the deque owner is the actor the native text is drawn on (target for pop-up,
local player for fly text).* M1 logs owner vs. source vs. target for every event.

### 2.3 "Only what the game shows"

*Hypothesis H3: the game applies the user's Character Configuration flytext/pop-up toggles
before `AddScreenLogEntry`* (i.e. entries are only queued if they would be displayed).

- If H3 holds, owning events at `AddScreenLogEntry` is automatically D17-safe.
- If it doesn't, HitSpark reads the same options through `IGameConfig.UiConfig` (`FlyTextDisp`,
  `PopUpTextDisp` and the per-relation toggles) and applies them before owning an event.
  M1 includes a test checklist that flips each native toggle and compares logs.

Either way HitSpark never *creates* information: every number it draws comes from an entry the
game queued for display, and it drops pop-ups beyond the game's own display distance.

### 2.4 Classification

`EventClassifier` maps `(FlyTextKind, option, SourceRelation, TargetRelation)` to a HitSpark
`Category` and `Channel`:

- **Channel**: `Outgoing` (source = you/your pet), `Incoming` (target = you), `PartyPopup`,
  `OtherPopup`.
- **Category** (the user-facing list): AutoAttack, Ability, DoT, Crit, DirectHit, CritDirectHit,
  Heal, HoT, CritHeal, DamageTakenPhysical/Magical/Unique, HealReceived, Miss, Dodge, Parry,
  Block, Resist/Invulnerable/Reflect, StatusGained, StatusLost, StatusResisted/NoEffect,
  Mp/Tp/Gp/Cp gain & drain, Experience (Exp, Island, Knowledge, Phantom, Dataset),
  Crafting (Progress, Quality, Quality crit/DH, Collectability), LootedItem, Interrupted,
  Incapacitated. Every value of `FlyTextKind` maps to exactly one category (unit-tested with a
  switch that the compiler checks for exhaustiveness).
- AutoAttack vs DoT (and Heal vs HoT) share kinds. Ticks come from a status update, not an
  action, so an entry with no action (`actionKind != 1` or `actionId == 0`) is a tick; anything
  carrying an action - Attack/Shot, pet and companion attacks - is a hit. This errs on the side
  of never labelling a pet's auto-attack as a DoT. *Hypothesis H4*, confirmed in M1 from the
  event recorder (`action=kind:id` on DoT ticks against a striking dummy).
- Block/Parry come from `option` (`ScreenLogOption.Blocked/Parried`), not from a kind.

### 2.5 Ownership and fallback

For each captured entry: if the category is set to **Replace** and the pipeline is healthy,
HitSpark enqueues a `SpawnRequest` and returns **without** calling `Original`. Otherwise it
calls `Original` and the game shows its own text (**Leave native**). A third mode, **Hide**,
suppresses without drawing.

Failure handling (never crash, never lose text silently):

- Hooks are created in one `try` block per hook. If a *required* hook fails to resolve or
  enable, HitSpark enters **native mode**: no hooks enabled, nothing suppressed, a banner in
  Settings and a Dalamud notification once, and the Diagnostics log names the failing address.
- Every detour body is wrapped in `try/catch`; on exception it calls `Original` (native text
  shows) and increments an error counter. A circuit breaker switches to native mode after
  5 exceptions in 60 s.
- If the renderer hasn't drawn a frame for 2 s while events arrive (UI not prepared, overlay
  blocked), ownership is released until it draws again.
- A `/hitspark native` command toggles native mode by hand.

## 3. Data model

```
Configuration (IPluginConfiguration, Version-ed)
 ├─ ActivePresetId
 ├─ UserPresets : List<Preset>               ← built-ins live in code, copied on "Duplicate"
 ├─ Global: MaxLiveNumbers, ReducedMotion(auto/on/off), HideInCombat=false,
 │          Scale, Opacity, NativeMode, DiagnosticsVerbose
 └─ (stretch) PresetRules: job / content type → preset id

Preset
 ├─ Id (Guid), Name, Author, BasedOn, SchemaVersion
 ├─ Palette   : named colours referenced by styles (Physical, Magical, Unique, Heal, Crit…)
 └─ Categories: Dictionary<Category, CategoryStyle>   (missing = inherit preset default)

CategoryStyle
 ├─ Mode        : Replace | LeaveNative | Hide
 ├─ Filter      : Sources {You, Pet, Party, Others} × Targets {You, Party, Others}
 ├─ Text        : Font, Size, Fill(solid|vertical gradient), Outline(width, colour),
 │                Shadow(offset, blur-ish passes, colour), Tint by damage type,
 │                Format(separators, abbreviate ≥ N, decimals), Prefix/Suffix, ShowName,
 │                Icon(none|action|status, side, size)
 ├─ Motion      : Trajectory + Timeline (see §4)
 ├─ Emphasis    : Punch, Shake, Flash, Sparks, Ghost (each optional with parameters)
 ├─ Aggregation : Off | Merge(window ms, key = action|source|category, roll-up ms)
 └─ Layout      : Anchor (actor head | actor centre | side lane L/R | screen column),
                  offsets, spread, lane spacing, overlap strategy
```

Styles are plain classes in config (Newtonsoft, as Dalamud config). At runtime each
`CategoryStyle` is **compiled** into an immutable `CompiledStyle` (flat arrays of keyframes,
resolved `ImFontPtr`, packed `uint` colours) so the hot path never touches config objects,
dictionaries or strings. Recompiled only when the preset changes.

**Share string**: `HS1:` + base64url(Deflate(UTF-8 JSON of the Preset)). `HS1` is the format
version; the JSON has its own `SchemaVersion` for migrations. Import validates ranges and
clamps every numeric field, so a hostile string can at worst produce an ugly preset.

## 4. Animation

**Timeline** = a set of optional tracks over normalised time `t ∈ [0,1]` of `Duration`:
`Scale`, `Alpha`, `Rotation`, `OffsetX`, `OffsetY`, `ColorMix` (base → accent). Each track is a
short array of keyframes `(t, value, easing-to-next)`. Evaluation is a linear scan of ≤ 8 keys —
cheaper than a binary search at this size and allocation-free.

**Easing library**: linear, quad/cubic/quart in-out, expo out, back (overshoot parameter),
elastic (amplitude/period), bounce, steps(n) for Retro. Pure static functions.

**Trajectory** = a parametric path added to the anchor, evaluated from `t` and a per-instance
random seed: `Rise`, `Fall`, `Arc(dir: L/R/Random, height, width)`, `Fountain(spread, gravity)`,
`Lane(column, speed)` (WoW-style incoming column), `Fixed`. Randomness is a per-instance
xorshift seed, so replaying a preview is deterministic.

**Emphasis** (layered on top, each with its own short timeline):
- *Punch*: scale overshoot then settle (back-out), typically 1.0 → 1.6 → 1.15 in ~180 ms.
- *Shake*: screen-space offset from smooth value noise, amplitude decays over its duration.
- *Flash*: additive white copy of the glyphs whose alpha spikes then fades.
- *Sparks*: 6–16 particles from a shared particle pool, drawn as tapered quads.
- *Ghost*: trailing copies at the positions from the last N frames (ring buffer per instance,
  fixed size) with falling alpha.

`UiBuilder.ShouldUseReducedMotion` (and a config override) disables shake/sparks/ghost and
flattens arcs to fades.

## 5. Rendering and performance

- **Where**: `UiBuilder.Draw` → `ImGui.GetBackgroundDrawList()`; no ImGui window, no input
  capture. Hidden when `IGameGui.GameUiHidden`, `IClientState.IsGPosing`,
  `UiBuilder.CutsceneActive`, or the "occupied in cutscene" conditions are set; while hidden,
  instances keep ageing so nothing piles up.
- **Pool**: one `FlyInstance[]` array of structs (capacity 512) plus a free-index stack; the
  configurable hard cap (default 160) is enforced at spawn by recycling the oldest lowest-priority
  instance (crits last). Particles: separate struct pool (1024). Nothing allocates after
  startup; spawn requests go through a fixed ring buffer.
- **Text**: the number is formatted once at spawn into a fixed `char` buffer inside the
  instance (`TryFormat`, no strings). Names/status labels come from Lumina/`text1` once per
  spawn and are interned in a small LRU (the only allocation, off the per-frame path).
- **Text drawing**: Dalamud's ImGui fork spreads a font atlas over several textures (each glyph
  carries a texture index), so writing glyph quads by hand is fragile. HitSpark draws with
  `ImDrawList.AddText(font, size, …, ReadOnlySpan<char>)` — allocation-free, since `ImU8String`
  converts spans into a fixed stack buffer — and then post-processes the vertices it just
  emitted: rotation around the number's centre, and vertical gradients via
  `ShadeVertsLinearColorGradientKeepAlpha`. Outline = 8 offset passes (4 when thin); letter
  spacing switches to one call per glyph.
- **Anchors**: per frame, each distinct actor used by live instances is resolved once
  (object-table index → verify `EntityId` → `Position + (0, Height, 0)` → `WorldToScreen`),
  cached in a frame-local table. Dead/despawned actors freeze at the last known position.
  Off-screen: incoming/self numbers clamp to the screen edge; others are culled.
- **Overlap**: lanes first (per-anchor slot allocator: a new number takes the slot least recently
  used, so bursts fan out instead of stacking), then an optional relaxation pass on a uniform
  screen grid (64 px cells, fixed arrays — cheaper than CBT's quad-tree and allocation-free)
  that nudges overlapping instances apart for 2 iterations.
- **Budget target**: < 0.3 ms CPU per frame with 160 live numbers; measured in the Diagnostics
  page (Stopwatch timings, peak live count, pool high-water mark) and in the M5 pass in 24/48-man
  content.

## 6. Aggregation

Key = `(category, sourceId, targetId, actionId-or-0)` hashed into a fixed open-addressing table
that points at live instances. A new hit within `window` of an open instance adds to its
`TargetValue`, extends its life, re-triggers a small punch, and the displayed value rolls up
(`Displayed = lerp(Displayed, Target, easeOut)` over `roll-up ms`). DoT/HoT ticks aggregate per
target by default in *Tally*; multi-hit abilities aggregate per action.

## 7. Fonts and icons

- **Game fonts** (from the user's game files, not bundled): `IFontAtlas.NewGameFontHandle`
  (`JupiterNumeric`, `Jupiter`, `TrumpGothic`, `Axis`) — the Classic+ look.
- **Bundled OFL fonts** (TTF + OFL.txt in `Assets/Fonts/`): loaded through one
  `UiBuilder.FontAtlas.NewDelegateFontHandle` per family with `AddFontFromFile`, restricted
  glyph ranges (Latin + Latin-1 + Latin Extended-A) baked once at 52 px without oversampling —
  numbers are mostly drawn at 20–60 px and down-scaling stays crisp, while the atlas stays small.
  The interface face (client-language glyphs) is baked at 26 px because it carries CJK sets. Candidates, all SIL OFL (final pick in M4): *Bebas Neue* or
  *Anton* (Heroic/Onslaught), *Barlow Condensed* (Tally), *Fredoka* (Elemental), *Cinzel*
  (Grim), *Inter* (Minimal), *Press Start 2P* or *Silkscreen* (Retro).
- Status/action names in JP/CN/KR fall back to Dalamud's default font with the language
  glyphs attached.
- **Icons**: `ITextureProvider.GetFromGameIcon(new GameIconLookup(id))`, cached per id at spawn.
  Status icons come from `v1`/`icon`, action icons from the Lumina `Action` sheet.

## 8. Project structure (mirrors LootView)

```
HitSpark.csproj  (Dalamud.NET.Sdk/15.0.0)   HitSpark.json   LICENSE (GPL-3.0)   README.md
Assets/Fonts/*.ttf + OFL.txt
docs/DESIGN.md
src/
  Plugin.cs                       services, commands, lifecycle
  Models/     Configuration, Preset, CategoryStyle, Category, enums
  Capture/    ScreenLogHooks, ActorResolver, EventClassifier, CombatEvent (struct)
  Rendering/  FlyRenderer, FlyInstance, InstancePool, GlyphEmitter, AnchorCache,
              OverlapSolver, ParticlePool, Easing, Timeline, Trajectory, Aggregator
  Presets/    BuiltInPresets, PresetCodec (share strings), StyleCompiler
  Services/   ConfigurationService, DiagnosticsLog, FontService, IconCache, VisibilityService
  UI/         Theme, Changelog, PreviewCanvas
  Windows/    ConfigWindow (+ editor tabs), ChangelogWindow, Window base
```

**Attribution**: where CBT (MIT) code is adapted, the file header says so and the README
credits it. DamageInfo and MultiHit (GPL-3.0) are read as references. FlyTextFilter (AGPL-3.0)
was read for understanding only; none of its code is used.

## 9. Test mode and preview

`TestEventSource` synthesises `SpawnRequest`s for every category on a timer (burst patterns:
single, multi-hit, DoT stream, crit chains, incoming burst). It feeds the same pipeline after
capture, so nothing touches the game. Two targets:
- **In world**: anchored to your character and current target (works outside combat).
- **Preview canvas** in the settings window: a virtual anchor inside a child region, rendered
  by the same `FlyRenderer` with a clip rect and its own instance pool.

## 10. Milestone checks (what "done" means in game)

- **M1** — hooks + Diagnostics event log (kind, relations, option, action, values, queue owner,
  source, target, and whether FlyTextCreated later saw it). Verify H1–H4. Replace **one** category
  (outgoing Ability damage) with a plain number on the right actor.
- **M2** — every `FlyTextKind` classified; fly vs pop-up, incoming vs outgoing correct; native
  fallback and circuit breaker proven by forcing an exception.
- **M3** — timeline/easing/trajectory, test mode, *Classic+* and *Heroic*.
- **M4** — remaining 6 presets, aggregation, emphasis, bundled fonts.
- **M5** — editor, share strings, polish, perf pass in 24/48-man content.
- **M6** — README, icon, manifest, D17 PR.

## 11. Open questions for Michael

1. **Suppression point**: own events at `AddScreenLogEntry` (needed for pop-ups) and accept that
   HitSpark then fully replaces those categories — agreed?
2. **Pop-up text for other players' actions on enemies** (party member hitting the boss): the
   game shows it if enabled in Character Configuration. HitSpark will replicate it only when the
   game would show it (H3). Default in presets: *Leave native* or *Replace*?
3. **Draw layer**: background draw list means numbers render above native HUD elements
   (hotbars, party list) — same as CBT. Acceptable, or do you want a "stay out of HUD
   rectangles" option later?
4. **Fonts**: OK to pick among the OFL candidates in §7, or do you have favourites?
