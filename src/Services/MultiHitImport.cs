using System;
using System.Collections.Generic;
using HitSpark.Models;
using Newtonsoft.Json.Linq;

namespace HitSpark.Services;

/// <summary>
/// Reads MultiHit's JSON: one exported action group, a list of groups, or MultiHit's whole
/// settings file (which Dalamud writes with "$type" markers). Field names are MultiHit's.
/// </summary>
public static class MultiHitImport
{
    /// <summary>The groups in the text. Throws a JsonException when it isn't JSON at all.</summary>
    public static List<MultiHitGroup> Parse(string json)
    {
        var root = JToken.Parse(json);
        var result = new List<MultiHitGroup>();

        if (Array(root) is { } list)
        {
            foreach (var item in list)
                if (ParseGroup(item) is { } g) result.Add(g);
        }
        else if (root is JObject obj && Array(obj["actionGroups"]) is { } groups)
        {
            foreach (var item in groups)
                if (ParseGroup(item) is { } g) result.Add(g);
        }
        else if (ParseGroup(root) is { } single)
        {
            result.Add(single);
        }

        return result;
    }

    private static MultiHitGroup? ParseGroup(JToken token)
    {
        if (token is not JObject obj || Array(obj["actionList"]) is not { } list) return null;

        var group = new MultiHitGroup
        {
            Name = obj.Value<string>("name") is { Length: > 0 } n ? n : "Imported",
            Enabled = obj.Value<bool?>("enabled") ?? true,
        };

        foreach (var item in list)
        {
            if (item is not JObject a) continue;
            var id = a.Value<long?>("actionKey") ?? 0;
            if (id <= 0 || id > uint.MaxValue) continue;

            var action = new MultiHitAction
            {
                ActionId = (uint)id,
                Enabled = a.Value<bool?>("enabled") ?? true,
                Interruptible = a.Value<bool?>("interruptible") ?? true,
                ShowHitNumber = a.Value<bool?>("showHit") ?? true,
                ShowFinal = a.Value<bool?>("showFinal") ?? false,
                CustomName = (a.Value<bool?>("hasCustomName") ?? false) && a.Value<string>("customName") is { Length: > 0 } c ? c : null,
                Final = ParseStep(a["finalHit"]) ?? new MultiHitStep { Frames = 1, Percent = 100 },
            };

            if (Array(a["hitList"]) is { } hits)
            {
                foreach (var h in hits)
                    if (ParseStep(h) is { } step) action.Hits.Add(step);
            }

            group.Actions.Add(action);
        }

        return group;
    }

    private static MultiHitStep? ParseStep(JToken? token)
    {
        if (token is not JObject h) return null;
        return new MultiHitStep
        {
            Frames = Math.Max(h.Value<int?>("time") ?? 0, 0),
            Percent = Math.Clamp(h.Value<int?>("percent") ?? 0, 0, 100),
            Color = (uint)(h.Value<long?>("color") ?? 0),
        };
    }

    /// <summary>A JSON array, also when Dalamud wrapped it as {"$type", "$values"}.</summary>
    private static JArray? Array(JToken? token) => token switch
    {
        JArray a => a,
        JObject o when o["$values"] is JArray v => v,
        _ => null,
    };
}
