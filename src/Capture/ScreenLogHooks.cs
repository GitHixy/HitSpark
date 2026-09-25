using System;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.STD;

namespace HitSpark.Capture;

/// <summary>
/// The two game hooks behind HitSpark (see docs/DESIGN.md §2).
///
/// <c>BattleLog.AddToScreenLogWithScreenLogKind</c> is where the game decides to show combat
/// text; we remember its source and target for the nested call. <c>ScreenLog.AddScreenLogEntry</c>
/// is where the entry joins an actor's queue; skipping it suppresses both fly text and pop-up
/// text. Addresses come from FFXIVClientStructs, so there are no private signatures to maintain.
///
/// A third, optional hook on <c>StatusManager.ProcessHotDot</c> recovers who applied a DoT or HoT:
/// the screen log itself reports ticks as coming from the afflicted actor.
/// </summary>
public sealed unsafe class ScreenLogHooks : IDisposable
{
    /// <summary>Decides for one entry; returns true when HitSpark takes it over.</summary>
    public delegate bool EntryHandler(StdDeque<ScreenLogEntry>* queue, ScreenLogEntry* entry, in CallContext context, in TickContext tick);

    /// <summary>What the game knew about a DoT/HoT tick while it was being processed.</summary>
    public readonly struct TickContext
    {
        public readonly bool Valid;
        public readonly uint SourceEntityId;
        public readonly uint StatusId;
        public readonly int TickMode;

        public TickContext(uint sourceEntityId, uint statusId, int tickMode)
        {
            Valid = true;
            SourceEntityId = sourceEntityId;
            StatusId = statusId;
            TickMode = tickMode;
        }
    }

    /// <summary>What the outer call knew about the event.</summary>
    public readonly struct CallContext
    {
        public readonly bool Valid;
        public readonly BattleChara* Target;
        public readonly BattleChara* Source;

        // The raw arguments, kept for the event recorder.
        public readonly int Kind;
        public readonly uint ActionId;
        public readonly int Value1;
        public readonly int Value2;
        public readonly int Value3;

        public CallContext(BattleChara* target, BattleChara* source, int kind, uint actionId, int value1, int value2, int value3)
        {
            Valid = true;
            Target = target;
            Source = source;
            Kind = kind;
            ActionId = actionId;
            Value1 = value1;
            Value2 = value2;
            Value3 = value3;
        }
    }

    private readonly EntryHandler handler;
    private readonly Action<Exception> onError;

    private Hook<BattleLog.Delegates.AddToScreenLogWithScreenLogKind>? kindHook;
    private Hook<ScreenLog.Delegates.AddScreenLogEntry>? entryHook;
    private Hook<StatusManager.Delegates.ProcessHotDot>? hotDotHook;

    private CallContext context;
    private TickContext tick;
    private int depth;

    /// <summary>Why the hooks are not running, or null when they are.</summary>
    public string? FailureReason { get; private set; }
    public bool IsActive => kindHook?.IsEnabled == true && entryHook?.IsEnabled == true;

    /// <summary>Whether DoT/HoT ticks can be attributed to whoever applied them.</summary>
    public bool TickHookActive => hotDotHook?.IsEnabled == true;

    public ScreenLogHooks(EntryHandler handler, Action<Exception> onError)
    {
        this.handler = handler;
        this.onError = onError;
    }

    /// <summary>Creates and enables both hooks. Any failure leaves the game untouched.</summary>
    public bool Enable()
    {
        try
        {
            var kindAddress = (nint)BattleLog.Addresses.AddToScreenLogWithScreenLogKind.Value;
            var entryAddress = (nint)ScreenLog.Addresses.AddScreenLogEntry.Value;

            if (kindAddress == 0 || entryAddress == 0)
            {
                FailureReason = $"Game function not found (AddToScreenLogWithScreenLogKind=0x{kindAddress:X}, AddScreenLogEntry=0x{entryAddress:X}).";
                Plugin.Log.Error("HitSpark hooks unavailable: {Reason}", FailureReason);
                return false;
            }

            kindHook = Plugin.GameInteropProvider.HookFromAddress<BattleLog.Delegates.AddToScreenLogWithScreenLogKind>(kindAddress, KindDetour);
            entryHook = Plugin.GameInteropProvider.HookFromAddress<ScreenLog.Delegates.AddScreenLogEntry>(entryAddress, EntryDetour);

            kindHook.Enable();
            entryHook.Enable();

            Plugin.Log.Info("Screen log hooks enabled (kind 0x{Kind:X}, entry 0x{Entry:X})", kindAddress, entryAddress);
            FailureReason = null;

            EnableTickHook();
            return true;
        }
        catch (Exception ex)
        {
            FailureReason = $"Hook setup failed: {ex.Message}";
            Plugin.Log.Error(ex, "HitSpark hooks unavailable; the game's own combat text stays on");
            Disable();
            return false;
        }
    }

    /// <summary>Optional: without it, ticks are attributed from the statuses on the target instead.</summary>
    private void EnableTickHook()
    {
        try
        {
            var address = (nint)StatusManager.Addresses.ProcessHotDot.Value;
            if (address == 0)
            {
                Plugin.Log.Warning("DoT/HoT tick hook unavailable; ticks will be attributed from target statuses");
                return;
            }

            hotDotHook = Plugin.GameInteropProvider.HookFromAddress<StatusManager.Delegates.ProcessHotDot>(address, HotDotDetour);
            hotDotHook.Enable();
            Plugin.Log.Info("DoT/HoT tick hook enabled (0x{Address:X})", address);
        }
        catch (Exception ex)
        {
            hotDotHook?.Dispose();
            hotDotHook = null;
            Plugin.Log.Warning(ex, "DoT/HoT tick hook failed; ticks will be attributed from target statuses");
        }
    }

    public void Disable()
    {
        kindHook?.Dispose();
        entryHook?.Dispose();
        hotDotHook?.Dispose();
        kindHook = null;
        entryHook = null;
        hotDotHook = null;
    }

    private void HotDotDetour(StatusManager* manager, BattleChara* target, uint statusId, int tickMode, uint value, uint sourceEntityId, int damageType)
    {
        var previous = tick;
        tick = new TickContext(sourceEntityId, statusId, tickMode);
        try
        {
            hotDotHook!.Original(manager, target, statusId, tickMode, value, sourceEntityId, damageType);
        }
        finally
        {
            tick = previous;
        }
    }

    private void KindDetour(BattleChara* target, BattleChara* source, int screenLogKind, byte option, byte actionKind, uint actionId, int value1, int value2, int value3)
    {
        // Remember who was involved for the nested AddScreenLogEntry call.
        var previous = context;
        context = new CallContext(target, source, screenLogKind, actionId, value1, value2, value3);
        depth++;
        try
        {
            kindHook!.Original(target, source, screenLogKind, option, actionKind, actionId, value1, value2, value3);
        }
        finally
        {
            depth--;
            context = depth > 0 ? previous : default;
        }
    }

    private void EntryDetour(StdDeque<ScreenLogEntry>* queue, ScreenLogEntry* entry)
    {
        var owned = false;
        try
        {
            owned = entry != null && handler(queue, entry, context, tick);
        }
        catch (Exception ex)
        {
            // Never lose text to our own bug: the game shows it natively instead.
            owned = false;
            onError(ex);
        }

        if (!owned)
            entryHook!.Original(queue, entry);
    }

    public void Dispose() => Disable();
}
