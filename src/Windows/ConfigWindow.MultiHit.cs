using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using HitSpark.Models;
using HitSpark.Services;
using HitSpark.UI;

namespace HitSpark.Windows;

/// <summary>The Multi-hit page: import MultiHit action groups and switch them on or off.</summary>
public partial class ConfigWindow
{
    private readonly FileDialogManager fileDialog = new();
    private string multiHitMessage = string.Empty;
    private bool multiHitOk;

    /// <summary>Sections still in beta; the nav rail marks them.</summary>
    private static readonly string[] BetaSections = ["Multi-hit"];

    private MultiHitService MultiHit => plugin.MultiHit;

    /// <summary>A small "BETA" tag, vertically centred on <paramref name="leftCenter"/>.</summary>
    private static void DrawBetaPill(ImDrawListPtr dl, Vector2 leftCenter)
    {
        const string text = "BETA";
        var scale = 0.72f;
        var font = ImGui.GetFont();
        var size = ImGui.GetFontSize() * scale;
        var ts = ImGui.CalcTextSize(text) * scale;
        var min = new Vector2(leftCenter.X, leftCenter.Y - ts.Y * 0.5f - 2f);
        var max = new Vector2(min.X + ts.X + 8f, leftCenter.Y + ts.Y * 0.5f + 2f);
        dl.AddRectFilled(min, max, Theme.U32(Theme.Crystal, 0.18f), (max.Y - min.Y) * 0.5f);
        dl.AddRect(min, max, Theme.U32(Theme.Crystal, 0.55f), (max.Y - min.Y) * 0.5f, ImDrawFlags.None, 1f);
        dl.AddText(font, size, new Vector2(min.X + 4f, min.Y + 2f), Theme.U32(Theme.CrystalBright), text);
    }

    private void DrawMultiHit()
    {
        Theme.Callout(FontAwesomeIcon.Flask, "Beta",
            "Multi-hit is new and still being tested. If a split looks wrong or a MultiHit group won't import, " +
            "Diagnostics copies a report you can attach to a GitHub issue.",
            Theme.Crystal);
        ImGui.Dummy(new Vector2(0, 6));

        Theme.SectionHeader("Multi-hit", FontAwesomeIcon.Stream);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
        ImGui.TextColored(Theme.TextMuted,
            "Splits one damage number into several hits that land with the animation, for actions " +
            "(often modded) that strike more than once. It uses the action groups of the MultiHit plugin, " +
            "so groups shared for it import as they are. Only damage from your own actions is split.");
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0, 4));

        var enabled = Config.MultiHitEnabled;
        Row("Split hits");
        if (Theme.Toggle("##multiHitEnabled", ref enabled))
        {
            Config.MultiHitEnabled = enabled;
            ConfigService.Save();
        }

        ImGui.SameLine(0, 8);
        Theme.HelpMarker("Turn off to show every damage number whole again, without losing your groups.");

        if (MultiHit.MultiHitLoaded)
        {
            ImGui.Dummy(new Vector2(0, 6));
            Theme.Callout(FontAwesomeIcon.ExclamationTriangle, "MultiHit is also loaded",
                "While HitSpark draws your damage numbers, MultiHit never sees them, so its splits don't show. " +
                "Import its groups below and disable it. To keep using MultiHit instead, set the damage " +
                "categories to Native in the style editor.",
                Theme.Warn);
        }

        ImGui.Dummy(new Vector2(0, 8));
        Theme.SectionHeader("Import", FontAwesomeIcon.FileImport);

        var configPath = MultiHitService.MultiHitConfigPath;
        using (ImRaii.Disabled(configPath is null))
        {
            if (Theme.PrimaryButton("From MultiHit settings", new Vector2(190, 30)) && configPath is not null)
                ShowImport(MultiHit.ImportFile(configPath));
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            Theme.Tooltip(configPath is null
                ? "MultiHit's settings weren't found on this computer."
                : "Copies every group from MultiHit's settings. Groups with the same name are replaced.");

        ImGui.SameLine(0, 8);
        if (Theme.GhostButton("From file", new Vector2(110, 30)))
        {
            fileDialog.OpenFileDialog("Import MultiHit group", ".json", (ok, path) =>
            {
                if (ok && !string.IsNullOrEmpty(path))
                    ShowImport(MultiHit.ImportFile(path));
            });
        }

        if (ImGui.IsItemHovered())
            Theme.Tooltip("A group exported from MultiHit (.json).");

        ImGui.SameLine(0, 8);
        if (Theme.GhostButton("From clipboard", new Vector2(130, 30)))
            ShowImport(MultiHit.ImportJson(ImGui.GetClipboardText() ?? string.Empty));

        if (ImGui.IsItemHovered())
            Theme.Tooltip("The text of a MultiHit group, copied from a .json file or a message.");

        if (multiHitMessage.Length > 0)
        {
            ImGui.Dummy(new Vector2(0, 2));
            ImGui.TextColored(multiHitOk ? Theme.Good : Theme.Bad, multiHitMessage);
        }

        ImGui.Dummy(new Vector2(0, 8));
        Theme.SectionHeader("Groups", FontAwesomeIcon.LayerGroup);

        if (MultiHit.Groups.Count == 0)
        {
            Theme.EmptyState(FontAwesomeIcon.Stream, "No groups yet", "Import one from MultiHit to get started.");
            return;
        }

        foreach (var group in MultiHit.Groups.ToList())
            DrawMultiHitGroup(group);
    }

    private void ShowImport(MultiHitService.ImportResult result)
    {
        multiHitOk = result.Ok;
        multiHitMessage = result.Ok
            ? $"Imported {result.Groups} group{(result.Groups == 1 ? "" : "s")} with {result.Actions} action{(result.Actions == 1 ? "" : "s")}."
            : result.Error!;
    }

    private void DrawMultiHitGroup(MultiHitGroup group)
    {
        using var id = ImRaii.PushId(group.GetHashCode());

        var on = group.Enabled;
        if (Theme.Toggle("##groupOn", ref on))
        {
            group.Enabled = on;
            MultiHit.Changed();
        }

        ImGui.SameLine(0, 10);
        ImGui.AlignTextToFramePadding();
        var open = ImGui.TreeNodeEx($"{group.Name}###group", ImGuiTreeNodeFlags.SpanAvailWidth);

        ImGui.SameLine(ImGui.GetContentRegionMax().X - 120f);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.TextFaint, $"{group.Actions.Count} action{(group.Actions.Count == 1 ? "" : "s")}");
        ImGui.SameLine(ImGui.GetContentRegionMax().X - 28f);
        if (Theme.IconButton("##deleteGroup", FontAwesomeIcon.Trash, "Remove group (hold Shift)", Theme.Bad, false, 28f) && ImGui.GetIO().KeyShift)
        {
            MultiHit.Groups.Remove(group);
            MultiHit.Changed();
            if (open) ImGui.TreePop();
            return;
        }

        if (!open) return;

        using (var table = ImRaii.Table("##actions", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.PadOuterX))
        {
            if (table)
            {
                ImGui.TableSetupColumn("On", ImGuiTableColumnFlags.WidthFixed, 44f);
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthStretch, 2f);
                ImGui.TableSetupColumn("Hits", ImGuiTableColumnFlags.WidthStretch, 1.4f);
                ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, 52f);
                ImGui.TableSetupColumn("Interrupt", ImGuiTableColumnFlags.WidthFixed, 70f);
                ImGui.TableHeadersRow();

                for (var i = 0; i < group.Actions.Count; i++)
                    DrawMultiHitAction(group.Actions[i], i);
            }
        }

        ImGui.TreePop();
        ImGui.Dummy(new Vector2(0, 4));
    }

    private void DrawMultiHitAction(MultiHitAction action, int index)
    {
        using var id = ImRaii.PushId(index);
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        var on = action.Enabled;
        if (Theme.Toggle("##actionOn", ref on))
        {
            action.Enabled = on;
            MultiHit.Changed();
        }

        ImGui.TableNextColumn();
        var entry = GameSheets.Action(action.ActionId);
        if (entry.Icon != 0)
        {
            var wrap = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(entry.Icon)).GetWrapOrEmpty();
            var h = ImGui.GetFrameHeight();
            ImGui.Image(wrap.Handle, new Vector2(h, h));
            ImGui.SameLine(0, 6);
        }

        ImGui.AlignTextToFramePadding();
        var name = entry.Name.Length > 0 ? entry.Name : $"Unknown action";
        ImGui.TextColored(action.Enabled ? Theme.Text : Theme.TextMuted, name);
        if (!string.IsNullOrWhiteSpace(action.CustomName))
        {
            ImGui.SameLine(0, 6);
            ImGui.TextColored(Theme.TextFaint, $"as \"{action.CustomName}\"");
        }

        if (ImGui.IsItemHovered())
            Theme.Tooltip($"Action #{action.ActionId}");

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        var span = action.Hits.Count == 0 ? 0f : action.Hits.Max(h => h.Seconds);
        var percent = action.Hits.Sum(h => h.Percent);
        ImGui.TextColored(Theme.TextMuted, action.Hits.Count == 0
            ? "None"
            : $"{action.Hits.Count} over {span:0.00}s ({percent}%)");
        if (ImGui.IsItemHovered() && action.Hits.Count > 0)
            Theme.Tooltip(string.Join("\n", action.Hits.Select((h, i) => $"#{i + 1}  {h.Seconds:0.00}s  {h.Percent}%")));

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.TextMuted, action.ShowFinal || action.Hits.Count == 0 ? "Yes" : "-");

        ImGui.TableNextColumn();
        var interrupt = action.Interruptible;
        if (Theme.Toggle("##interrupt", ref interrupt))
        {
            action.Interruptible = interrupt;
            MultiHit.Changed();
        }

        if (ImGui.IsItemHovered())
            Theme.Tooltip("Drop the hits still to come when you use another action first.");
    }
}
