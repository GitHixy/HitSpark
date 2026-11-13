using System;
using System.Linq;
using System.Numerics;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace HitSpark.Services;

/// <summary>
/// What HitSpark needs to know about the game's own interface: whether the game has put its HUD
/// away (cutscenes, including the ones in the middle of a duty), and where its windows are, so
/// numbers can pass under them.
/// </summary>
public static unsafe class GameUi
{
    /// <summary>
    /// Windows numbers always stay over: text and effect layers the game draws across the screen
    /// (no background), and the bars you read together with the numbers - your target's and focus
    /// target's (with their status and cast bars) and your own HP/MP.
    /// </summary>
    private static readonly byte[][] StayOver = new[]
    {
        "NamePlate", "_FlyText", "_PopUpText", "_ScreenText", "_TextError", "_WideText", "_TextClassChange",
        "_LocationTitle", "_LocationTitleShort", "_AreaText", "_MiniTalk", "_TargetCursor", "_TargetCursorGround",
        "_ScreenInfoFront", "_ScreenInfoBack", "FadeMiddle", "FadeBack", "_Image", "_Image3", "_ScreenFrame",
        "_ParameterWidget", "_FocusTargetInfo",
    }.Select(Encoding.ASCII.GetBytes).ToArray();

    /// <summary>Name prefixes for families of windows numbers stay over (_TargetInfo, _TargetInfoCastBar...).</summary>
    private static readonly byte[][] StayOverPrefixes = new[] { "_TargetInfo" }.Select(Encoding.ASCII.GetBytes).ToArray();

    /// <summary>
    /// True while the game itself hides its HUD: the nameplate layer is loaded but switched off.
    /// That happens in every cutscene, including scripted scenes in the middle of a fight, which
    /// don't always raise the cutscene conditions.
    /// </summary>
    public static bool HudHiddenByGame
    {
        get
        {
            var nameplates = Plugin.GameGui.GetAddonByName("NamePlate");
            return !nameplates.IsNull && nameplates.IsReady && !nameplates.IsVisible;
        }
    }

    /// <summary>
    /// Fills <paramref name="into"/> with the screen rectangles (min x, min y, max x, max y) of the
    /// game's visible windows and HUD elements. Returns how many were written.
    /// </summary>
    public static int CollectWindows(Span<Vector4> into, Vector2 screenPos, Vector2 screenSize)
    {
        var manager = RaptureAtkUnitManager.Instance();
        if (manager == null) return 0;

        var screenArea = screenSize.X * screenSize.Y;
        ref var list = ref manager->AllLoadedUnitsList;
        var entries = list.Entries;
        var count = Math.Min((int)list.Count, entries.Length);
        var written = 0;

        for (var i = 0; i < count && written < into.Length; i++)
        {
            var unit = entries[i].Value;
            if (unit == null || !unit->IsVisible || unit->Alpha == 0) continue;

            var root = unit->RootNode;
            if (root == null || !root->IsVisible()) continue;

            var width = root->Width * root->ScaleX;
            var height = root->Height * root->ScaleY;
            if (width < 4f || height < 4f) continue;

            // Full-screen layers (fades, overlays) would swallow everything.
            if (width * height > screenArea * 0.35f) continue;
            if (StaysOver(unit)) continue;

            var min = screenPos + new Vector2(unit->X, unit->Y);
            into[written++] = new Vector4(min.X, min.Y, min.X + width, min.Y + height);
        }

        return written;
    }

    private static bool StaysOver(AtkUnitBase* unit)
    {
        var name = unit->Name;
        var length = name.IndexOf((byte)0);
        if (length < 0) length = name.Length;
        var trimmed = name[..length];

        foreach (var candidate in StayOver)
        {
            if (trimmed.SequenceEqual(candidate)) return true;
        }

        foreach (var prefix in StayOverPrefixes)
        {
            if (trimmed.StartsWith(prefix)) return true;
        }

        return false;
    }
}
