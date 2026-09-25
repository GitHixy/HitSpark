using Dalamud.Game.Config;

namespace HitSpark.Capture;

/// <summary>
/// The game's own switches for combat text (Character Configuration → UI Settings), read live.
/// HitSpark takes events before the game checks these, so it must check them itself.
/// </summary>
public static class GameDisplay
{
    /// <summary>"Display flying text": text involving you, around your character.</summary>
    public static bool FlyText => Read(UiControlOption.FlyTextDisp);

    /// <summary>"Display pop-up text": text over other characters.</summary>
    public static bool PopUpText => Read(UiControlOption.PopUpTextDisp);

    /// <summary>Raw values for the Diagnostics report.</summary>
    public static string Describe()
        => $"flying text {Raw(UiControlOption.FlyTextDisp)}, pop-up text {Raw(UiControlOption.PopUpTextDisp)}";

    // An unreadable option counts as on: failing open keeps HitSpark working like the game's default.
    private static bool Read(UiControlOption option)
        => !Plugin.GameConfig.TryGet(option, out uint value) || value != 0;

    private static string Raw(UiControlOption option)
        => Plugin.GameConfig.TryGet(option, out uint value) ? value.ToString() : "unreadable";
}
