using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HitSpark.Capture;
using HitSpark.Models;
using HitSpark.Rendering;
using Newtonsoft.Json;

namespace HitSpark.Services;

/// <summary>
/// Splits one damage number into several timed hits, for actions whose (often modded)
/// animation lands more than once. Rules use the format of the MultiHit plugin, so its
/// shared action groups and its own settings file import directly.
/// </summary>
public sealed class MultiHitService
{
    /// <summary>The player's auto-attacks; they never interrupt a split.</summary>
    private static readonly HashSet<uint> AutoAttacks = [7, 8];

    private readonly ConfigurationService configService;

    private Dictionary<uint, MultiHitAction> byId = new();
    private Dictionary<string, MultiHitAction> byName = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, string> nameCache = new();

    private bool? multiHitLoaded;
    private DateTime loadedCheckedAt;

    /// <summary>Damage numbers split so far this session.</summary>
    public long Splits { get; private set; }

    public MultiHitService(ConfigurationService configService)
    {
        this.configService = configService;
        Rebuild();
    }

    private Configuration Config => configService.Configuration;

    public List<MultiHitGroup> Groups => Config.MultiHitGroups;

    /// <summary>Call after any change to the groups; rebuilds the lookup and saves.</summary>
    public void Changed()
    {
        Rebuild();
        configService.Save();
    }

    private void Rebuild()
    {
        var ids = new Dictionary<uint, MultiHitAction>();
        var names = new Dictionary<string, MultiHitAction>(StringComparer.Ordinal);

        // First enabled rule for an action wins, as in MultiHit.
        foreach (var group in Config.MultiHitGroups.Where(g => g.Enabled))
        {
            foreach (var action in group.Actions.Where(a => a.Enabled && a.ActionId != 0))
            {
                ids.TryAdd(action.ActionId, action);
                var name = ActionName(action.ActionId);
                if (name.Length > 0)
                    names.TryAdd(name, action);
            }
        }

        byId = ids;
        byName = names;
    }

    /// <summary>Action name in the client's language, cached.</summary>
    public string ActionName(uint actionId)
    {
        if (!nameCache.TryGetValue(actionId, out var name))
        {
            name = GameSheets.Action(actionId).Name;
            nameCache[actionId] = name;
        }

        return name;
    }

    /// <summary>
    /// The rule for an action: by id, then by name, because MultiHit matches by name and so a
    /// rule also covers other actions that share it (PvP and quest variants).
    /// </summary>
    private MultiHitAction? Find(uint actionId)
    {
        if (byId.Count == 0) return null;
        if (byId.TryGetValue(actionId, out var rule)) return rule;
        var name = ActionName(actionId);
        return name.Length > 0 && byName.TryGetValue(name, out rule) ? rule : null;
    }

    /// <summary>Records an action you used, for interruptible splits.</summary>
    public static void NoteAction(FlyRenderer renderer, uint actionId)
    {
        if (actionId != 0 && !AutoAttacks.Contains(actionId))
            renderer.LastPlayerAction = actionId;
    }

    /// <summary>
    /// Queues the split hits for one of your damage numbers. Returns false when the action has no
    /// rule, so the caller queues the number as usual.
    /// </summary>
    public bool TrySplit(in SpawnRequest request, uint actionId, FlyRenderer renderer)
    {
        if (!Config.MultiHitEnabled || !request.HasNumber || request.Value <= 0) return false;

        var rule = Find(actionId);
        if (rule is null) return false;

        var name = string.IsNullOrWhiteSpace(rule.CustomName) ? request.Name : rule.CustomName;
        var interrupt = rule.Interruptible ? actionId : 0;
        var lastFrames = 0;

        for (var i = 0; i < rule.Hits.Count; i++)
        {
            var hit = rule.Hits[i];
            lastFrames = Math.Max(lastFrames, hit.Frames);

            var r = request;
            r.Value = (long)(request.Value * (Math.Clamp(hit.Percent, 0, 100) / 100.0));
            r.Name = rule.ShowHitNumber ? (name is null ? $"Hit #{i + 1}" : $"{name} #{i + 1}") : name;
            r.Delay = hit.Seconds;
            r.Color = hit.Color;
            r.Separate = true;
            r.InterruptAction = interrupt;
            renderer.Enqueue(r);
        }

        if (rule.ShowFinal || rule.Hits.Count == 0)
        {
            var r = request;
            r.Name = name;
            r.Delay = (lastFrames + Math.Max(rule.Final.Frames, 0)) / 30f;
            r.Color = rule.Final.Color;
            r.Separate = true;
            r.InterruptAction = interrupt;
            renderer.Enqueue(r);
        }

        Splits++;
        return true;
    }

    // ------------------------------------------------------------------
    // MultiHit interop
    // ------------------------------------------------------------------

    /// <summary>MultiHit's own settings file, when it exists on this machine.</summary>
    public static string? MultiHitConfigPath
    {
        get
        {
            try
            {
                var dir = Plugin.PluginInterface.ConfigFile.DirectoryName;
                if (dir is null) return null;
                var path = Path.Combine(dir, "MultiHit.json");
                return File.Exists(path) ? path : null;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Whether MultiHit is loaded too. It hooks the game's text after HitSpark has already taken
    /// it, so while HitSpark draws your damage MultiHit never sees those numbers.
    /// </summary>
    public bool MultiHitLoaded
    {
        get
        {
            if (multiHitLoaded is null || DateTime.UtcNow - loadedCheckedAt > TimeSpan.FromSeconds(5))
            {
                loadedCheckedAt = DateTime.UtcNow;
                try
                {
                    multiHitLoaded = Plugin.PluginInterface.InstalledPlugins
                        .Any(p => p.IsLoaded && p.InternalName.Equals("MultiHit", StringComparison.OrdinalIgnoreCase));
                }
                catch
                {
                    multiHitLoaded = false;
                }
            }

            return multiHitLoaded.Value;
        }
    }

    public readonly record struct ImportResult(int Groups, int Actions, string? Error)
    {
        public bool Ok => Error is null;
    }

    public ImportResult ImportFile(string path)
    {
        try
        {
            return ImportJson(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Multi-hit import from {Path} failed", path);
            return new ImportResult(0, 0, $"Couldn't read the file: {ex.Message}");
        }
    }

    /// <summary>
    /// Imports MultiHit JSON: one exported group, a list of groups, or MultiHit's whole settings
    /// file. A group with the same name as an existing one replaces it.
    /// </summary>
    public ImportResult ImportJson(string json)
    {
        List<MultiHitGroup> groups;
        try
        {
            groups = MultiHitImport.Parse(json);
        }
        catch (JsonException)
        {
            return new ImportResult(0, 0, "That isn't MultiHit JSON.");
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Multi-hit import failed");
            return new ImportResult(0, 0, "That isn't MultiHit JSON.");
        }

        if (groups.Count == 0)
            return new ImportResult(0, 0, "No action groups found in it.");

        var actions = 0;
        foreach (var group in groups)
        {
            Config.MultiHitGroups.RemoveAll(g => g.Name.Equals(group.Name, StringComparison.OrdinalIgnoreCase));
            Config.MultiHitGroups.Add(group);
            actions += group.Actions.Count;
        }

        Changed();
        Plugin.Log.Info("Imported {Groups} multi-hit group(s), {Actions} action(s)", groups.Count, actions);
        return new ImportResult(groups.Count, actions, null);
    }
}
