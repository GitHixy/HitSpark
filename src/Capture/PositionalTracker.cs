using System;
using System.Collections.Generic;
using System.Diagnostics;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace HitSpark.Capture;

/// <summary>Which side of the target a positional action wants.</summary>
public enum PositionalSide : byte
{
    Rear,
    Flank,
}

/// <summary>
/// Works out whether your positional actions (rear and flank bonuses) landed.
///
/// The game never says so: there is no flag for it in the combat text. Instead, when the server's
/// result for one of your positional actions arrives (<c>ActionEffectHandler.Receive</c>, a little
/// before its damage number), HitSpark looks at where you stand relative to the way the target
/// faces, the same four 90° quadrants the game uses. True North always counts as landed.
/// If the hook is unavailable, the check is made when the damage number arrives instead.
/// </summary>
public sealed unsafe class PositionalTracker : IDisposable
{
    private const uint TrueNorth = 1250;
    private const double PendingLifetime = 4.0;

    /// <summary>
    /// Every player action with a rear or flank bonus, from the action descriptions in the game
    /// data (7.x). Combo variants and upgraded actions have their own ids.
    /// </summary>
    private static readonly Dictionary<uint, PositionalSide> Actions = new()
    {
        // Monk
        [56] = PositionalSide.Flank,     // Snap Punch
        [66] = PositionalSide.Rear,      // Demolish
        [36947] = PositionalSide.Flank,  // Pouncing Coeurl
        // Dragoon
        [88] = PositionalSide.Rear,      // Chaos Thrust
        [25772] = PositionalSide.Rear,   // Chaotic Spring
        [3554] = PositionalSide.Flank,   // Fang and Claw
        [3556] = PositionalSide.Rear,    // Wheeling Thrust
        // Ninja
        [2255] = PositionalSide.Rear,    // Aeolian Edge
        [3563] = PositionalSide.Flank,   // Armor Crush
        [2258] = PositionalSide.Rear,    // Trick Attack
        // Samurai
        [7481] = PositionalSide.Rear,    // Gekko
        [7482] = PositionalSide.Flank,   // Kasha
        // Reaper
        [24382] = PositionalSide.Flank,  // Gibbet
        [24383] = PositionalSide.Rear,   // Gallows
        [36970] = PositionalSide.Flank,  // Executioner's Gibbet
        [36971] = PositionalSide.Rear,   // Executioner's Gallows
        // Viper
        [34610] = PositionalSide.Flank,  // Flanksting Strike
        [34611] = PositionalSide.Flank,  // Flanksbane Fang
        [34612] = PositionalSide.Rear,   // Hindsting Strike
        [34613] = PositionalSide.Rear,   // Hindsbane Fang
        [34621] = PositionalSide.Flank,  // Hunter's Coil
        [34622] = PositionalSide.Rear,   // Swiftskin's Coil
    };

    private struct Pending
    {
        public uint ActionId;
        public uint TargetId;
        public bool Landed;
        public double Time;
    }

    private readonly Pending[] pending = new Pending[8];
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Func<bool> logEnabled;
    private Hook<ActionEffectHandler.Delegates.Receive>? receiveHook;
    private int next;

    public bool HookActive => receiveHook?.IsEnabled == true;

    public PositionalTracker(Func<bool> logEnabled)
    {
        this.logEnabled = logEnabled;
        try
        {
            var address = (nint)ActionEffectHandler.Addresses.Receive.Value;
            if (address == 0)
            {
                Plugin.Log.Warning("Action effect hook unavailable; positionals are judged when the damage number arrives");
                return;
            }

            receiveHook = Plugin.GameInteropProvider.HookFromAddress<ActionEffectHandler.Delegates.Receive>(address, ReceiveDetour);
            receiveHook.Enable();
            Plugin.Log.Info("Action effect hook enabled (0x{Address:X})", address);
        }
        catch (Exception ex)
        {
            receiveHook?.Dispose();
            receiveHook = null;
            Plugin.Log.Warning(ex, "Action effect hook failed; positionals are judged when the damage number arrives");
        }
    }

    public static bool IsPositional(uint actionId, out PositionalSide side) => Actions.TryGetValue(actionId, out side);

    /// <summary>
    /// The outcome for your positional action's damage on <paramref name="targetId"/>, recorded
    /// when the server resolved it, or judged now when nothing was recorded.
    /// </summary>
    public bool TryResolve(uint actionId, uint targetId, out PositionalSide side, out bool landed)
    {
        landed = false;
        if (!Actions.TryGetValue(actionId, out side)) return false;

        var now = clock.Elapsed.TotalSeconds;
        for (var i = 0; i < pending.Length; i++)
        {
            ref var p = ref pending[i];
            if (p.ActionId != actionId || p.TargetId != targetId || now - p.Time > PendingLifetime) continue;

            landed = p.Landed;
            p.ActionId = 0;
            return true;
        }

        var self = (GameObject*)(Plugin.ObjectTable.LocalPlayer?.Address ?? 0);
        var target = FindObject(targetId);
        if (self == null || target == null) return false;

        landed = Judge(self, target, side);
        return true;
    }

    private void ReceiveDetour(uint casterEntityId, Character* casterPtr, System.Numerics.Vector3* targetPos,
        ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects, GameObjectId* targetEntityIds)
    {
        try
        {
            Record(casterEntityId, casterPtr, header, effects, targetEntityIds);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Error reading an action effect");
        }

        receiveHook!.Original(casterEntityId, casterPtr, targetPos, header, effects, targetEntityIds);
    }

    private void Record(uint casterEntityId, Character* caster, ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects, GameObjectId* targetIds)
    {
        if (caster == null || header == null || targetIds == null || header->NumTargets == 0) return;
        if (!Actions.TryGetValue(header->ActionId, out var side)) return;

        var localId = Plugin.ObjectTable.LocalPlayer?.EntityId ?? 0;
        if (localId == 0 || casterEntityId != localId) return;

        var targetId = (uint)targetIds[0].ObjectId;
        var target = FindObject(targetId);
        if (target == null) return;

        var landed = Judge((GameObject*)caster, target, side);
        pending[next] = new Pending
        {
            ActionId = header->ActionId,
            TargetId = targetId,
            Landed = landed,
            Time = clock.Elapsed.TotalSeconds,
        };
        next = (next + 1) % pending.Length;

        if (logEnabled() && effects != null)
        {
            // Raw effect bytes, kept for checking the geometry against what the server applied.
            var e = effects->Effects;
            Plugin.Log.Debug("[positional] action={Action} side={Side} landed={Landed} target={Target:X} effect0=({T},{P0},{P1},{P2},{P3},{P4},{V})",
                header->ActionId, side, landed, targetId, e[0].Type, e[0].Param0, e[0].Param1, e[0].Param2, e[0].Param3, e[0].Param4, e[0].Value);
        }
    }

    /// <summary>The game's quadrants: rear is the 90° behind the target, flanks the 90° on each side.</summary>
    private static bool Judge(GameObject* self, GameObject* target, PositionalSide side)
    {
        if (HasTrueNorth(self)) return true;

        var dx = self->Position.X - target->Position.X;
        var dz = self->Position.Z - target->Position.Z;
        if (dx * dx + dz * dz < 0.0001f) return false;

        // Angle of you around the target, relative to where it faces (0 = in front).
        var angle = MathF.Atan2(dx, dz) - target->Rotation;
        while (angle > MathF.PI) angle -= MathF.Tau;
        while (angle < -MathF.PI) angle += MathF.Tau;

        var off = MathF.Abs(angle);
        return side switch
        {
            PositionalSide.Rear => off >= MathF.PI * 0.75f,
            _ => off > MathF.PI * 0.25f && off < MathF.PI * 0.75f,
        };
    }

    private static bool HasTrueNorth(GameObject* self)
    {
        if (!self->IsCharacter()) return false;
        var statuses = ((Character*)self)->GetStatusManager();
        if (statuses == null) return false;

        for (var i = 0; i < 60; i++)
        {
            if (statuses->Status[i].StatusId == TrueNorth) return true;
        }

        return false;
    }

    private static GameObject* FindObject(uint entityId)
    {
        if (entityId is 0 or 0xE0000000) return null;

        var manager = GameObjectManager.Instance();
        if (manager == null) return null;

        for (var i = 0; i < 200; i++)
        {
            var obj = manager->Objects.IndexSorted[i].Value;
            if (obj != null && obj->EntityId == entityId) return obj;
        }

        return null;
    }

    public void Dispose()
    {
        receiveHook?.Dispose();
        receiveHook = null;
    }
}
