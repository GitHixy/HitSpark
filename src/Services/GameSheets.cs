using System;
using Dalamud.Game;
using Lumina.Excel.Sheets;
using Action = Lumina.Excel.Sheets.Action;

namespace HitSpark.Services;

/// <summary>
/// Names, icons and words read straight from the game's sheets, so everything shown in game
/// follows the client's language. Lumina already caches sheet rows, so nothing is kept here.
/// </summary>
public static class GameSheets
{
    /// <param name="Detrimental">For statuses: a debuff (status category 2).</param>
    public readonly record struct Entry(string Name, uint Icon, bool Detrimental = false);

    private static readonly Entry Missing = new(string.Empty, 0);

    public static Entry Action(uint id)
    {
        try
        {
            if (Plugin.DataManager.GetExcelSheet<Action>().TryGetRow(id, out var row))
                return new Entry(row.Name.ExtractText(), row.Icon);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Action {Id} lookup failed", id);
        }

        return Missing;
    }

    public static Entry Status(uint id)
    {
        try
        {
            if (Plugin.DataManager.GetExcelSheet<Status>().TryGetRow(id, out var row))
                return new Entry(row.Name.ExtractText(), row.Icon, row.StatusCategory == 2);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Status {Id} lookup failed", id);
        }

        return Missing;
    }

    public static Entry Item(uint id)
    {
        // High-quality (+1,000,000) and collectable (+500,000) items arrive with offsets on the id.
        var baseId = id % 500_000;
        try
        {
            if (Plugin.DataManager.GetExcelSheet<Item>().TryGetRow(baseId, out var row))
                return new Entry(row.Name.ExtractText(), row.Icon);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Item {Id} lookup failed", id);
        }

        return Missing;
    }

    /// <summary>
    /// A DoT (<paramref name="detrimental"/>) or a HoT: a status that deals or restores HP over
    /// time, as opposed to a debuff such as Reprisal or Chain Stratagem that only modifies damage.
    /// The sheet has no flag for it, but every DoT and HoT says so in its English description
    /// ("Sustaining damage over time.", "Regenerating HP over time."). The English sheet is read
    /// whatever the client language, so the test is the same for everyone.
    /// </summary>
    public static bool Ticks(uint id, bool detrimental)
    {
        try
        {
            if (!Plugin.DataManager.GetExcelSheet<Status>().TryGetRow(id, out var row)) return false;
            if ((row.StatusCategory == 2) != detrimental) return false;

            if (!Plugin.DataManager.GetExcelSheet<Status>(ClientLanguage.English).TryGetRow(id, out var english)) return false;
            var text = english.Description.ExtractText();
            return text.Contains("damage over time", StringComparison.OrdinalIgnoreCase)
                   || text.Contains("HP over time", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Status {Id} lookup failed", id);
            return false;
        }
    }

    // Words shown in place of a number. Most are the game's own fly-text strings, so they read
    // exactly like the native text in every client language.
    public static string Miss => Addon(723);
    public static string Dodge => Addon(726);
    public static string Invulnerable => Addon(731);
    public static string Resist => Addon(719);
    public static string NoEffect => Capitalized(Addon(984));
    public static string Reflect => Addon(848);
    public static string Reflected => Addon(849);
    public static string Interrupted => Addon(1099);
    public static string Incapacitated => Addon(987);

    public static string Progress => Addon(213);
    public static string Quality => Addon(216);
    public static string Collectability => Addon(3860);

    /// <summary>Stand-ins when a status or item has no name.</summary>
    public static string StatusWord => Addon(2570);
    public static string ItemWord => Addon(953);

    // Units after a number, with the leading space.
    public static string Hp => Unit(BaseParam(7));
    public static string Mp => Unit(BaseParam(8));
    public static string Tp => Unit(BaseParam(9));
    public static string Gp => Unit(BaseParam(10));
    public static string Cp => Unit(BaseParam(11));
    public static string Ep => Unit(Addon(1053));
    public static string Exp => Unit(Addon(720));
    public static string IslandExp => Unit(Addon(14297));
    public static string PhantomExp => Unit(Addon(16589));
    public static string Knowledge => Unit(Addon(16588));
    public static string Data => Unit(Addon(13390));

    private static string Addon(uint id)
    {
        try
        {
            if (Plugin.DataManager.GetExcelSheet<Addon>().TryGetRow(id, out var row))
                return row.Text.ExtractText().Trim();
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Addon {Id} lookup failed", id);
        }

        return string.Empty;
    }

    private static string BaseParam(uint id)
    {
        try
        {
            if (Plugin.DataManager.GetExcelSheet<BaseParam>().TryGetRow(id, out var row))
                return row.Name.ExtractText().Trim();
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "BaseParam {Id} lookup failed", id);
        }

        return string.Empty;
    }

    private static string Unit(string word) => word.Length > 0 ? " " + word : string.Empty;

    /// <summary>The English "has no effect!" follows a name in the log; alone it needs a capital.</summary>
    private static string Capitalized(string text)
        => text.Length > 0 && char.IsLower(text[0]) ? char.ToUpperInvariant(text[0]) + text[1..] : text;
}
