using System;
using System.Collections.Generic;
using Lumina.Excel.Sheets;
using Action = Lumina.Excel.Sheets.Action;

namespace HitSpark.Services;

/// <summary>
/// Names and icons for actions, statuses and items, looked up once per id and kept.
/// Strings are created the first time an id is seen and then reused, so the capture path
/// allocates only on genuinely new ids. Game hooks and the draw loop both read it, hence the lock.
/// </summary>
public sealed class GameDataCache
{
    /// <param name="Detrimental">For statuses: a debuff (status category 2).</param>
    public readonly record struct Entry(string Name, uint Icon, bool Detrimental = false);

    private readonly Dictionary<uint, Entry> actions = new();
    private readonly Dictionary<uint, Entry> statuses = new();
    private readonly Dictionary<uint, Entry> items = new();
    private readonly object gate = new();

    public Entry Action(uint id)
    {
        lock (gate)
            return LookupAction(id);
    }

    private Entry LookupAction(uint id)
    {
        if (actions.TryGetValue(id, out var entry)) return entry;

        entry = new Entry(string.Empty, 0);
        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<Action>();
            if (sheet.TryGetRow(id, out var row))
                entry = new Entry(row.Name.ExtractText(), row.Icon);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Action {Id} lookup failed", id);
        }

        actions[id] = entry;
        return entry;
    }

    public Entry Status(uint id)
    {
        lock (gate)
            return LookupStatus(id);
    }

    private Entry LookupStatus(uint id)
    {
        if (statuses.TryGetValue(id, out var entry)) return entry;

        entry = new Entry(string.Empty, 0);
        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<Status>();
            if (sheet.TryGetRow(id, out var row))
                entry = new Entry(row.Name.ExtractText(), row.Icon, row.StatusCategory == 2);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Status {Id} lookup failed", id);
        }

        statuses[id] = entry;
        return entry;
    }

    public Entry Item(uint id)
    {
        lock (gate)
            return LookupItem(id);
    }

    private Entry LookupItem(uint id)
    {
        // High-quality (+1,000,000) and collectable (+500,000) items arrive with offsets on the id.
        var baseId = id % 500_000;
        if (items.TryGetValue(baseId, out var entry)) return entry;

        entry = new Entry(string.Empty, 0);
        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<Item>();
            if (sheet.TryGetRow(baseId, out var row))
                entry = new Entry(row.Name.ExtractText(), row.Icon);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Item {Id} lookup failed", id);
        }

        items[baseId] = entry;
        return entry;
    }
}
