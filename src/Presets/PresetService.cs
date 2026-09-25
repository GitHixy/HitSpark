using System;
using System.Collections.Generic;
using System.Linq;
using HitSpark.Models;
using HitSpark.Rendering;
using HitSpark.Services;

namespace HitSpark.Presets;

/// <summary>
/// Knows every preset (built-in and the user's), which one is active, and keeps a compiled copy
/// of it for the renderer. Editing a user preset calls <see cref="MarkDirty"/>, which recompiles
/// on the next frame so changes show up live.
/// </summary>
public sealed class PresetService
{
    private readonly ConfigurationService configService;
    private bool dirty = true;

    public CompiledPreset Compiled { get; private set; } = null!;
    public Preset Active { get; private set; } = null!;

    /// <summary>Raised after the compiled preset changes, so renderers can pick it up.</summary>
    public event Action<CompiledPreset>? Changed;

    public PresetService(ConfigurationService configService)
    {
        this.configService = configService;
        Resolve();
        Recompile();
    }

    private Configuration Config => configService.Configuration;

    public IEnumerable<Preset> All => BuiltInPresets.All.Concat(Config.UserPresets);

    public Preset? Find(string id) => All.FirstOrDefault(p => p.Id == id);

    public void Activate(Preset preset)
    {
        Config.ActivePresetId = preset.Id;
        configService.Save();
        Resolve();
        MarkDirty();
    }

    public Preset Duplicate(Preset source)
    {
        var copy = source.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = UniqueName(source.Name + " copy");
        copy.BasedOn = source.IsBuiltIn ? source.Name : source.BasedOn;
        copy.Author = string.Empty;
        Config.UserPresets.Add(copy);
        configService.Save();
        return copy;
    }

    public void Rename(Preset preset, string name)
    {
        if (preset.IsBuiltIn) return;
        name = name.Trim();
        if (name.Length == 0) return;
        preset.Name = name.Length > 40 ? name[..40] : name;
        configService.Save();
        MarkDirty();
    }

    public void Delete(Preset preset)
    {
        if (preset.IsBuiltIn) return;
        Config.UserPresets.RemoveAll(p => p.Id == preset.Id);
        if (Config.ActivePresetId == preset.Id)
            Config.ActivePresetId = BuiltInPresets.DefaultId;
        configService.Save();
        Resolve();
        MarkDirty();
    }

    public Preset Import(Preset preset)
    {
        preset.Name = UniqueName(preset.Name);
        Config.UserPresets.Add(preset);
        configService.Save();
        return preset;
    }

    public void MarkDirty() => dirty = true;

    /// <summary>Called once per frame; recompiles only when something changed.</summary>
    public void Update()
    {
        if (!dirty) return;
        Recompile();
    }

    private void Resolve()
    {
        Active = Find(Config.ActivePresetId) ?? BuiltInPresets.All.First(p => p.Id == BuiltInPresets.DefaultId);
    }

    private void Recompile()
    {
        dirty = false;
        try
        {
            Compiled = CompiledPreset.From(Active);
            Changed?.Invoke(Compiled);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not compile preset {Name}", Active.Name);
        }
    }

    private string UniqueName(string name)
    {
        var baseName = name.Length > 34 ? name[..34] : name;
        var candidate = baseName;
        var n = 2;
        while (All.Any(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            candidate = $"{baseName} {n++}";
        return candidate;
    }
}
