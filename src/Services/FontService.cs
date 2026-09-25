using System;
using System.IO;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using HitSpark.Models;

namespace HitSpark.Services;

/// <summary>
/// Owns every font HitSpark draws with. Bundled OFL faces are baked large (so crit pops stay
/// crisp when scaled up) with a Latin-only glyph range to keep the atlas small; the game's own
/// faces come from the user's game files. Everything is locked once per frame around drawing.
/// </summary>
public sealed class FontService : IDisposable
{
    /// <summary>
    /// Bake size for display faces. Most numbers are drawn at 20-60 px and down-scaling looks far
    /// better than up; larger bakes cost atlas memory for little visible gain.
    /// </summary>
    private const float BakeSizePx = 52f;

    /// <summary>The interface face carries the client language's full glyph set, so it stays small.</summary>
    private const float InterfaceBakeSizePx = 26f;

    /// <summary>Basic Latin, Latin-1 and Latin Extended-A (covers EN/DE/FR names), dashes and ellipsis.</summary>
    private static readonly ushort[] LatinRanges =
    [
        0x0020, 0x007E,
        0x00A0, 0x017F,
        0x2013, 0x2026,
        0,
    ];

    private readonly IFontHandle?[] handles = new IFontHandle?[Enum.GetValues<FontId>().Length];
    private readonly ILockedImFont?[] locked = new ILockedImFont?[Enum.GetValues<FontId>().Length];
    private readonly ImFontPtr[] frameFonts = new ImFontPtr[Enum.GetValues<FontId>().Length];
    private bool inFrame;

    public FontService()
    {
        var atlas = Plugin.PluginInterface.UiBuilder.FontAtlas;
        atlas.BuildStepChange += OnBuildStep;
        var dir = Path.Combine(Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty, "Assets", "Fonts");

        foreach (var id in Enum.GetValues<FontId>())
        {
            try
            {
                handles[(int)id] = id switch
                {
                    FontId.GameJupiter => atlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.Jupiter, BakeSizePx)),
                    FontId.GameTrumpGothic => atlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.TrumpGothic, BakeSizePx)),
                    FontId.Interface => atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
                    {
                        var font = tk.AddDalamudDefaultFont(InterfaceBakeSizePx);
                        tk.AttachExtraGlyphsForDalamudLanguage(new SafeFontConfig { SizePx = InterfaceBakeSizePx, MergeFont = font });
                    })),
                    _ => BundledHandle(atlas, Path.Combine(dir, FileName(id))),
                };
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Could not create font handle for {Font}", id);
            }
        }
    }

    /// <summary>Rebuilds happen on UI scale and language changes; logged so odd font states can be traced.</summary>
    private static void OnBuildStep(IFontAtlasBuildToolkit toolkit)
    {
        if (toolkit.BuildStep == FontAtlasBuildStep.PostBuild)
            Plugin.Log.Debug("Font atlas rebuilt");
    }

    private static IFontHandle BundledHandle(IFontAtlas atlas, string path)
        => atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            if (!File.Exists(path))
            {
                Plugin.Log.Warning("Font file missing: {Path}", path);
                tk.AddDalamudDefaultFont(BakeSizePx);
                return;
            }

            tk.AddFontFromFile(path, new SafeFontConfig
            {
                SizePx = BakeSizePx,
                GlyphRanges = LatinRanges,
                OversampleH = 1,
                OversampleV = 1,
            });
        }));

    public static string FileName(FontId id) => id switch
    {
        FontId.BebasNeue => "BebasNeue-Regular.ttf",
        FontId.Anton => "Anton-Regular.ttf",
        FontId.RussoOne => "RussoOne-Regular.ttf",
        FontId.BarlowCondensed => "BarlowCondensed-Bold.ttf",
        FontId.Barlow => "Barlow-SemiBold.ttf",
        FontId.LilitaOne => "LilitaOne-Regular.ttf",
        FontId.CinzelDecorative => "CinzelDecorative-Bold.ttf",
        FontId.PressStart2P => "PressStart2P-Regular.ttf",
        _ => string.Empty,
    };

    public static string DisplayName(FontId id) => id switch
    {
        FontId.GameJupiter => "Game serif (Jupiter)",
        FontId.GameTrumpGothic => "Game condensed (Trump Gothic)",
        FontId.Interface => "Interface",
        FontId.BebasNeue => "Bebas Neue",
        FontId.Anton => "Anton",
        FontId.RussoOne => "Russo One",
        FontId.BarlowCondensed => "Barlow Condensed",
        FontId.Barlow => "Barlow",
        FontId.LilitaOne => "Lilita One",
        FontId.CinzelDecorative => "Cinzel Decorative",
        FontId.PressStart2P => "Press Start 2P",
        _ => id.ToString(),
    };

    /// <summary>Whether a face covers characters beyond Latin (names in Japanese, Chinese, Korean).</summary>
    public static bool IsLatinOnly(FontId id) => id is not FontId.Interface;

    /// <summary>Locks every ready font for the duration of a frame's drawing.</summary>
    public void BeginFrame()
    {
        if (inFrame) return;
        inFrame = true;

        var fallback = ImGui.GetFont();
        for (var i = 0; i < handles.Length; i++)
        {
            frameFonts[i] = fallback;
            var handle = handles[i];
            if (handle is null || !handle.Available) continue;

            try
            {
                locked[i] = handle.Lock();
                frameFonts[i] = locked[i]!.ImFont;
            }
            catch (Exception)
            {
                locked[i] = null;
            }
        }

        // Any face that failed falls back to the interface face if that one is ready.
        var ui = locked[(int)FontId.Interface] is not null ? frameFonts[(int)FontId.Interface] : fallback;
        for (var i = 0; i < handles.Length; i++)
        {
            if (locked[i] is null)
                frameFonts[i] = ui;
        }
    }

    public void EndFrame()
    {
        if (!inFrame) return;
        inFrame = false;

        for (var i = 0; i < locked.Length; i++)
        {
            locked[i]?.Dispose();
            locked[i] = null;
        }
    }

    /// <summary>The font to draw with this frame. Only valid between BeginFrame and EndFrame.</summary>
    public ImFontPtr Get(FontId id) => frameFonts[(int)id];

    public bool IsReady(FontId id) => handles[(int)id]?.Available ?? false;

    public void Dispose()
    {
        Plugin.PluginInterface.UiBuilder.FontAtlas.BuildStepChange -= OnBuildStep;
        EndFrame();
        foreach (var handle in handles)
            handle?.Dispose();
    }
}
