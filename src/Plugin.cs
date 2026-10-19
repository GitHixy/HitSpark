using System;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using HitSpark.Capture;
using HitSpark.Models;
using HitSpark.Presets;
using HitSpark.Rendering;
using HitSpark.Services;
using HitSpark.UI;
using HitSpark.Windows;

namespace HitSpark;

/// <summary>
/// Main plugin class for HitSpark
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    public string Name => "HitSpark";

    private const string CommandName = "/hitspark";
    private const string CommandAlt = "/hs";

    // Dalamud Services
    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] public static IGameGui GameGui { get; private set; } = null!;
    [PluginService] public static IDataManager DataManager { get; private set; } = null!;
    [PluginService] public static IPluginLog DalamudLog { get; private set; } = null!;
    [PluginService] public static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] public static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] public static ICondition Condition { get; private set; } = null!;
    [PluginService] public static IFlyTextGui FlyTextGui { get; private set; } = null!;
    [PluginService] public static INotificationManager NotificationManager { get; private set; } = null!;
    [PluginService] public static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] public static IPartyList PartyList { get; private set; } = null!;
    [PluginService] public static IGameConfig GameConfig { get; private set; } = null!;

    /// <summary>Plugin-wide logger: writes to Dalamud's log and keeps a copy for the Diagnostics page.</summary>
    public static DiagnosticsLog Log { get; } = new(() => DalamudLog);

    // Plugin Services
    public ConfigurationService ConfigService { get; }
    public FontService Fonts { get; }
    public PresetService Presets { get; }
    public FlyRenderer WorldRenderer { get; }
    public EventPipeline Pipeline { get; }
    public TestEventSource WorldTest { get; }

    // Windows
    public ConfigWindow ConfigWindow { get; }
    public ChangelogWindow ChangelogWindow { get; }

    public Configuration Configuration => ConfigService.Configuration;

    /// <summary>Spawns the test script around you and your target. Not saved: off on every load.</summary>
    public bool TestMode { get; set; }

    private readonly Stopwatch clock = Stopwatch.StartNew();

    public Plugin()
    {
        try
        {
            Log.Info("HitSpark plugin initializing...");

            ConfigService = new ConfigurationService();
            Fonts = new FontService();
            Presets = new PresetService(ConfigService);

            WorldRenderer = new FlyRenderer(Fonts, worldSpace: true) { Preset = Presets.Compiled };
            Presets.Changed += compiled => WorldRenderer.Preset = compiled;
            WorldTest = new TestEventSource();

            Pipeline = new EventPipeline(ConfigService, Presets, WorldRenderer);

            ConfigWindow = new ConfigWindow(this);
            ChangelogWindow = new ChangelogWindow(this);

            CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open HitSpark settings\n→ /hitspark test - Toggle test numbers around you\n→ /hitspark native - Toggle native mode (hand everything back to the game)"
            });
            CommandManager.AddHandler(CommandAlt, new CommandInfo(OnCommand) { ShowInHelp = false });

            PluginInterface.UiBuilder.Draw += Draw;
            PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
            PluginInterface.UiBuilder.OpenMainUi += OpenConfigUi;

            ShowChangelogIfUpdated();

            Log.Info("HitSpark plugin initialized successfully!");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize HitSpark plugin");
            throw;
        }
    }

    public void Dispose()
    {
        try
        {
            Log.Info("HitSpark plugin disposing...");

            PluginInterface.UiBuilder.Draw -= Draw;
            PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
            PluginInterface.UiBuilder.OpenMainUi -= OpenConfigUi;

            // Hooks go first so the game's own text is back before anything else unloads.
            Pipeline?.Dispose();

            ConfigWindow?.Dispose();
            ChangelogWindow?.Dispose();
            Fonts?.Dispose();
            ConfigService?.Dispose();

            CommandManager.RemoveHandler(CommandName);
            CommandManager.RemoveHandler(CommandAlt);

            Log.Info("HitSpark plugin disposed successfully!");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error disposing HitSpark plugin");
        }
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "test":
                TestMode = !TestMode;
                Log.Info("Test mode {State}", TestMode ? "on" : "off");
                break;
            case "native":
                Configuration.NativeMode = !Configuration.NativeMode;
                ConfigService.Save();
                Log.Info("Native mode {State}", Configuration.NativeMode ? "on" : "off");
                break;
            default:
                ConfigWindow.IsOpen = !ConfigWindow.IsOpen;
                break;
        }
    }

    private void OpenConfigUi() => ConfigWindow.IsOpen = true;

    private void ShowChangelogIfUpdated()
    {
        var current = Changelog.CurrentVersion;
        if (ConfigService.IsNewConfiguration)
        {
            Configuration.LastSeenVersion = current;
            ConfigService.Save();
            return;
        }

        if (Configuration.LastSeenVersion != current)
            ChangelogWindow.ShowSince(Configuration.LastSeenVersion);
    }

    // ------------------------------------------------------------------
    // Frame
    // ------------------------------------------------------------------

    public double Now => clock.Elapsed.TotalSeconds;

    public bool ReducedMotion => Configuration.ReducedMotion switch
    {
        ReducedMotionMode.On => true,
        ReducedMotionMode.Off => false,
        _ => PluginInterface.UiBuilder.ShouldUseReducedMotion,
    };

    /// <summary>Why world text is currently not drawn, or null when it is.</summary>
    public static string? HiddenReason
    {
        get
        {
            if (!ClientState.IsLoggedIn) return "not logged in";
            if (GameGui.GameUiHidden) return "game UI hidden";
            if (ClientState.IsGPosing) return "GPose";
            if (Condition[ConditionFlag.OccupiedInCutSceneEvent]) return "cutscene event";
            if (Condition[ConditionFlag.WatchingCutscene]) return "watching cutscene";
            if (Condition[ConditionFlag.WatchingCutscene78]) return "watching cutscene (78)";
            return null;
        }
    }

    /// <summary>Frames whose drawing threw, for the Diagnostics report.</summary>
    public int FrameErrors { get; private set; }
    private int loggedFrameErrors;

    private void Draw()
    {
        Fonts.BeginFrame();
        try
        {
            Presets.Update();
            DrawWorld();

            ConfigWindow.Draw();
            ChangelogWindow.Draw();
        }
        catch (Exception ex)
        {
            // A per-frame error would flood the log; the first few are enough to diagnose it.
            FrameErrors++;
            if (loggedFrameErrors++ < 5)
                Log.Error(ex, "Error during HitSpark frame");
        }
        finally
        {
            Fonts.EndFrame();
        }
    }

    private void DrawWorld()
    {
        Pipeline.Heartbeat();

        var dt = ImGui.GetIO().DeltaTime;
        if (TestMode && ClientState.IsLoggedIn)
            WorldTest.Update(dt, SpawnTest);

        var viewport = ImGui.GetMainViewport();
        var ctx = new RenderContext
        {
            Now = Now,
            DeltaTime = dt,
            Scale = Configuration.GlobalScale * Math.Max(viewport.Size.Y / 1080f, 0.5f),
            Opacity = Configuration.GlobalOpacity,
            ReducedMotion = ReducedMotion,
            MaxLive = Configuration.MaxLiveNumbers,
            Visible = HiddenReason is null,
            ClipMin = viewport.Pos,
            ClipMax = viewport.Pos + viewport.Size,
        };

        if (WorldRenderer.Trace != Configuration.LogEvents)
        {
            WorldRenderer.Trace = Configuration.LogEvents;
            WorldRenderer.ResetTrace();
        }

        WorldRenderer.Draw(ImGui.GetBackgroundDrawList(), ctx);
    }

    /// <summary>Test numbers land on your target if you have one, otherwise on you.</summary>
    private void SpawnTest(SpawnRequest request)
    {
        var self = ObjectTable.LocalPlayer;
        if (self is null || !Configuration.Allows(request.Involvement)) return;
        request.SizeScale = Configuration.ScaleFor(request.Involvement);

        var target = TargetManager.Target;
        var aboutYou = CategoryInfo.IsIncoming(request.Category)
                       || CategoryInfo.Group(request.Category) == CategoryGroup.Status && target is null;

        if (!aboutYou && target is not null)
        {
            request.ActorIndex = target.ObjectIndex;
            request.ActorEntityId = target.EntityId;
        }
        else
        {
            request.ActorIndex = self.ObjectIndex;
            request.ActorEntityId = self.EntityId;
        }

        WorldRenderer.Enqueue(request);
    }
}
