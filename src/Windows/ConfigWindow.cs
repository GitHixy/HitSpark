using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using HitSpark.Models;
using HitSpark.Presets;
using HitSpark.Rendering;
using HitSpark.Services;
using HitSpark.UI;

namespace HitSpark.Windows;

public class ConfigWindow : Window
{
    private readonly Plugin plugin;
    private readonly PreviewCanvas presetPreview;
    private readonly PreviewCanvas editorPreview;

    private int section;

    private const string RepoUrl = "https://github.com/GitHixy/HitSpark";
    private const int ReportLineLimit = 200;
    private const float LabelWidth = 150f;
    private const float ControlWidth = 230f;

    // Presets page
    private string? selectedPresetId;
    private CompiledPreset? selectedCompiled;
    private string? selectedCompiledId;
    private string renameBuffer = string.Empty;
    private Preset? renaming;
    private string importMessage = string.Empty;
    private bool importOk;

    // Editor
    private Category editCategory = Category.Ability;
    private int editTab;
    private CategoryStyle? copiedStyle;
    private bool changed;
    private double saveAt = double.MaxValue;

    // Diagnostics view state. The filtered list is rebuilt only when the log or the filter changes.
    private int logLevelFilter;
    private string logSearch = string.Empty;
    private bool logAutoScroll = true;
    private readonly HashSet<LogEntry> selectedLogLines = [];
    private List<LogEntry> visibleLogLines = [];
    private (int Revision, int Level, string Search) logViewKey = (-1, -1, string.Empty);
    private int scrolledRevision = -1;

    private static readonly (FontAwesomeIcon Icon, string Label, string Blurb)[] Sections =
    [
        (FontAwesomeIcon.LayerGroup, "Presets", "Pick a look, or share and import one"),
        (FontAwesomeIcon.PaintBrush, "Style editor", "Tune every category of combat text"),
        (FontAwesomeIcon.SlidersH, "General", "Size, opacity, motion and limits"),
        (FontAwesomeIcon.Bug, "Diagnostics", "Status, event recorder and bug reports"),
        (FontAwesomeIcon.InfoCircle, "About", "Version, links and credits"),
    ];

    public ConfigWindow(Plugin plugin)
        : base("HitSpark Settings###HitSparkConfig")
    {
        this.plugin = plugin;
        presetPreview = new PreviewCanvas(plugin);
        editorPreview = new PreviewCanvas(plugin);

        Size = new Vector2(860, 640);
        SizeConstraintMin = new Vector2(760, 520);
        SizeConstraintMax = new Vector2(1400, 1200);
        WindowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
    }

    private PresetService Presets => plugin.Presets;
    private ConfigurationService ConfigService => plugin.ConfigService;
    private Configuration Config => plugin.Configuration;

    protected override void DrawContents()
    {
        try
        {
            BgAlpha = 0.94f;
            changed = false;

            Theme.WindowHeader(FontAwesomeIcon.Bolt, "HitSpark", Sections[section].Blurb, DrawHeaderActions);

            var avail = ImGui.GetContentRegionAvail();
            const float railWidth = 158f;

            DrawNavRail(new Vector2(railWidth, avail.Y));

            ImGui.SameLine(0, 12);

            using (var pane = Theme.Region("##ConfigPane", new Vector2(0, avail.Y)))
            {
                if (pane)
                {
                    switch (section)
                    {
                        case 0: DrawPresets(); break;
                        case 1: DrawEditor(); break;
                        case 2: DrawGeneral(); break;
                        case 3: DrawDiagnostics(); break;
                        case 4: DrawAbout(); break;
                    }

                    ImGui.Dummy(new Vector2(0, 8));
                }
            }

            if (changed)
            {
                Presets.MarkDirty();
                saveAt = plugin.Now + 1.0;
            }

            // Sliders fire every frame while dragged; save once things settle.
            if (plugin.Now >= saveAt)
            {
                saveAt = double.MaxValue;
                ConfigService.Save();
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Error drawing config window");
            ImGui.TextColored(Theme.Bad, "Error drawing settings.");
        }
    }

    protected override void OnClosed()
    {
        if (saveAt != double.MaxValue)
        {
            saveAt = double.MaxValue;
            ConfigService.Save();
        }
    }

    private void DrawHeaderActions()
    {
        var width = ImGui.GetContentRegionAvail().X;
        var label = plugin.TestMode ? "Stop test" : "Test in world";
        var buttonWidth = 130f;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + width - buttonWidth);
        if (plugin.TestMode ? Theme.PrimaryButton(label, new Vector2(buttonWidth, 0)) : Theme.GhostButton(label, new Vector2(buttonWidth, 0), Theme.Gold))
            plugin.TestMode = !plugin.TestMode;
        if (ImGui.IsItemHovered())
            Theme.Tooltip("Plays sample numbers of every kind on you and your target, so you can judge a preset in the real game. Works out of combat. Also /hitspark test.");
    }

    private void DrawNavRail(Vector2 size)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();

        dl.AddRectFilled(origin, origin + size, Theme.U32(Theme.Panel, 0.55f), Theme.Radius);
        dl.AddRect(origin, origin + size, Theme.U32(Theme.Line, 0.8f), Theme.Radius, ImDrawFlags.None, 1f);

        using var child = Theme.Region("##ConfigNav", size);
        if (!child) return;

        ImGui.Dummy(new Vector2(0, 4));

        for (var i = 0; i < Sections.Length; i++)
        {
            var (icon, label, _) = Sections[i];
            var selected = section == i;

            var p = ImGui.GetCursorScreenPos();
            var w = ImGui.GetContentRegionAvail().X - 8;
            const float h = 34f;

            ImGui.SetCursorScreenPos(new Vector2(p.X + 4, p.Y));
            if (ImGui.InvisibleButton($"##nav{i}", new Vector2(w, h)))
                section = i;

            var hovered = ImGui.IsItemHovered();
            var min = new Vector2(p.X + 4, p.Y);
            var max = new Vector2(p.X + 4 + w, p.Y + h);

            if (selected)
            {
                dl.AddRectFilled(min, max, Theme.U32(Theme.Gold, 0.13f), Theme.Radius);
                dl.AddRectFilled(min, new Vector2(min.X + 2.5f, max.Y), Theme.U32(Theme.Gold, 0.95f), 1.5f);
            }
            else if (hovered)
            {
                dl.AddRectFilled(min, max, Theme.U32(Theme.Crystal, 0.12f), Theme.Radius);
            }

            var tint = selected ? Theme.GoldBright : hovered ? Theme.Text : Theme.TextMuted;

            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                var glyph = icon.ToIconString();
                var gs = ImGui.CalcTextSize(glyph);
                dl.AddText(new Vector2(min.X + 15 - gs.X * 0.5f, min.Y + (h - gs.Y) * 0.5f), Theme.U32(tint), glyph);
            }

            var ts = ImGui.CalcTextSize(label);
            dl.AddText(new Vector2(min.X + 30, min.Y + (h - ts.Y) * 0.5f), Theme.U32(tint), label);

            ImGui.Dummy(new Vector2(0, 2));
        }

        // Status pill at the bottom of the rail: are we replacing text right now?
        var (statusText, statusColor) = StatusSummary();
        var pillY = origin.Y + size.Y - 34f;
        var pillMin = new Vector2(origin.X + 8, pillY);
        var pillMax = new Vector2(origin.X + size.X - 8, pillY + 24f);
        dl.AddRectFilled(pillMin, pillMax, Theme.U32(statusColor, 0.12f), 12f);
        dl.AddCircleFilled(new Vector2(pillMin.X + 12, pillMin.Y + 12), 4f, Theme.U32(statusColor), 12);
        dl.AddText(new Vector2(pillMin.X + 22, pillMin.Y + (24f - ImGui.GetTextLineHeight()) * 0.5f), Theme.U32(statusColor), statusText);
    }

    private (string Text, Vector4 Color) StatusSummary()
    {
        if (!plugin.Pipeline.HooksActive) return ("Hooks offline", Theme.Bad);
        if (plugin.Pipeline.TripReason is not null) return ("Paused (errors)", Theme.Bad);
        if (Config.NativeMode) return ("Native mode", Theme.Warn);
        return ("Active", Theme.Good);
    }

    // ------------------------------------------------------------------
    // Presets
    // ------------------------------------------------------------------

    private void DrawPresets()
    {
        selectedPresetId ??= Presets.Active.Id;
        var selected = Presets.Find(selectedPresetId) ?? Presets.Active;

        // The preview plays whichever preset is selected in the list, active or not.
        CompiledPreset compiled;
        if (selected == Presets.Active)
        {
            compiled = Presets.Compiled;
        }
        else
        {
            if (selectedCompiled is null || selectedCompiledId != selected.Id)
            {
                selectedCompiled = CompiledPreset.From(selected);
                selectedCompiledId = selected.Id;
            }

            compiled = selectedCompiled;
        }

        presetPreview.Draw("##PresetPreview", new Vector2(0, 250f), compiled, selected.Id);

        ImGui.Dummy(new Vector2(0, 4));
        ImGui.TextColored(Theme.TextMuted, $"Previewing {selected.Name}.");
        if (selected != Presets.Active)
        {
            ImGui.SameLine(0, 8);
            ImGui.TextColored(Theme.TextFaint, $"In use: {Presets.Active.Name}");
        }

        ImGui.Dummy(new Vector2(0, 6));
        Theme.SectionHeader("Library", FontAwesomeIcon.LayerGroup);

        foreach (var preset in Presets.All.ToList())
            DrawPresetRow(preset);

        ImGui.Dummy(new Vector2(0, 10));
        Theme.SectionHeader("Share", FontAwesomeIcon.ShareAlt);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
        ImGui.TextColored(Theme.TextMuted,
            "Export copies a preset as a short code you can paste anywhere. To import one, copy its code and press the button below.");
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0, 4));

        if (Theme.PrimaryButton("Import from clipboard", new Vector2(190, 30)))
        {
            var text = ImGui.GetClipboardText();
            if (PresetCodec.TryDecode(text ?? string.Empty, out var imported, out var error))
            {
                var added = Presets.Import(imported);
                selectedPresetId = added.Id;
                importOk = true;
                importMessage = $"Imported \"{added.Name}\". Select it and press Use to switch to it.";
                Plugin.Log.Info("Imported preset {Name}", added.Name);
            }
            else
            {
                importOk = false;
                importMessage = error;
            }
        }

        if (importMessage.Length > 0)
        {
            ImGui.SameLine(0, 10);
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(importOk ? Theme.Good : Theme.Bad, importMessage);
        }

        DrawRenamePopup();
    }

    private void DrawPresetRow(Preset preset)
    {
        var isActive = preset == Presets.Active;
        var isSelected = preset.Id == selectedPresetId;

        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var w = ImGui.GetContentRegionAvail().X;
        const float h = 58f;

        ImGui.PushID(preset.Id);
        if (ImGui.InvisibleButton("##row", new Vector2(Math.Max(w - 330f, 40f), h)))
            selectedPresetId = preset.Id;
        var hovered = ImGui.IsItemHovered();

        var bg = isSelected ? Theme.Alpha(Theme.Gold, 0.10f) : hovered ? Theme.Alpha(Theme.Crystal, 0.07f) : Theme.Alpha(Theme.Surface, 0.55f);
        dl.AddRectFilled(p, p + new Vector2(w, h), Theme.U32(bg), Theme.Radius);
        dl.AddRect(p, p + new Vector2(w, h), Theme.U32(isSelected ? Theme.GoldDim : Theme.LineSoft), Theme.Radius, ImDrawFlags.None, 1f);
        if (isActive)
            dl.AddRectFilled(p, new Vector2(p.X + 3f, p.Y + h), Theme.U32(Theme.Gold), 1.5f);

        dl.AddText(p + new Vector2(14, 9), Theme.U32(isActive ? Theme.GoldBright : Theme.Text), preset.Name);
        var nameWidth = ImGui.CalcTextSize(preset.Name).X;
        var badgeX = p.X + 14 + nameWidth + 10;
        if (isActive)
            badgeX = DrawInlineBadge(dl, new Vector2(badgeX, p.Y + 9), "IN USE", Theme.Gold);
        if (preset.IsBuiltIn)
            DrawInlineBadge(dl, new Vector2(badgeX, p.Y + 9), "BUILT-IN", Theme.Crystal);
        else if (preset.BasedOn.Length > 0)
            DrawInlineBadge(dl, new Vector2(badgeX, p.Y + 9), $"FROM {preset.BasedOn.ToUpperInvariant()}", Theme.TextMuted);

        var description = preset.Description.Length > 0 ? preset.Description : "Your own preset.";
        Theme.ClipText(dl, p + new Vector2(14, 31), w - 350f, description, Theme.U32(Theme.TextMuted));

        // Actions, right-aligned.
        ImGui.SetCursorScreenPos(new Vector2(p.X + w - 322f, p.Y + (h - 28f) * 0.5f));
        if (!isActive)
        {
            if (Theme.PrimaryButton("Use", new Vector2(56, 28)))
            {
                Presets.Activate(preset);
                selectedPresetId = preset.Id;
            }
        }
        else
        {
            ImGui.Dummy(new Vector2(56, 28));
        }

        ImGui.SameLine(0, 6);
        if (Theme.GhostButton("Duplicate", new Vector2(80, 28)))
        {
            var copy = Presets.Duplicate(preset);
            selectedPresetId = copy.Id;
        }

        ImGui.SameLine(0, 6);
        if (Theme.GhostButton("Export", new Vector2(62, 28), Theme.Crystal))
        {
            ImGui.SetClipboardText(PresetCodec.Encode(preset));
            importOk = true;
            importMessage = $"Code for \"{preset.Name}\" copied to the clipboard.";
        }

        ImGui.SameLine(0, 6);
        if (!preset.IsBuiltIn)
        {
            if (Theme.IconButton("##rename", FontAwesomeIcon.Pen, "Rename", null, false, 28f))
            {
                renaming = preset;
                renameBuffer = preset.Name;
                ImGui.OpenPopup("Rename preset###HitSparkRename");
            }

            ImGui.SameLine(0, 4);
            if (Theme.IconButton("##delete", FontAwesomeIcon.Trash, "Delete (hold Shift)", Theme.Bad, false, 28f) && ImGui.GetIO().KeyShift)
            {
                Presets.Delete(preset);
                if (selectedPresetId == preset.Id) selectedPresetId = Presets.Active.Id;
            }
        }

        ImGui.PopID();
        ImGui.SetCursorScreenPos(new Vector2(p.X, p.Y + h + 6f));
        ImGui.Dummy(new Vector2(0, 0));
    }

    private static float DrawInlineBadge(ImDrawListPtr dl, Vector2 at, string label, Vector4 color)
    {
        using var scale = new Theme.FontScale(0.78f);
        var ts = ImGui.CalcTextSize(label);
        var max = at + new Vector2(ts.X + 12, ts.Y + 4);
        dl.AddRectFilled(at, max, Theme.U32(color, 0.14f), 3f);
        dl.AddRect(at, max, Theme.U32(color, 0.45f), 3f, ImDrawFlags.None, 1f);
        dl.AddText(at + new Vector2(6, 2), Theme.U32(color), label);
        return max.X + 6;
    }

    private void DrawRenamePopup()
    {
        ImGui.SetNextWindowSize(new Vector2(340, 0));
        if (!ImGui.BeginPopupModal("Rename preset###HitSparkRename", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar))
            return;

        ImGui.TextColored(Theme.Text, "Rename preset");
        ImGui.Dummy(new Vector2(0, 4));
        ImGui.SetNextItemWidth(-1);
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        var submit = ImGui.InputText("##renameInput", ref renameBuffer, 40, ImGuiInputTextFlags.EnterReturnsTrue);

        ImGui.Dummy(new Vector2(0, 6));
        if (Theme.PrimaryButton("Save", new Vector2(90, 28)) || submit)
        {
            if (renaming is not null) Presets.Rename(renaming, renameBuffer);
            renaming = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine(0, 8);
        if (Theme.GhostButton("Cancel", new Vector2(90, 28)))
        {
            renaming = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    // ------------------------------------------------------------------
    // Style editor
    // ------------------------------------------------------------------

    private void DrawEditor()
    {
        var preset = Presets.Active;

        if (preset.IsBuiltIn)
        {
            Theme.Callout(FontAwesomeIcon.Lock, $"{preset.Name} is a built-in preset",
                "Built-in presets stay as they ship so they can keep improving with updates. Duplicate it to get your own copy you can change freely.",
                Theme.Crystal);
            ImGui.Dummy(new Vector2(0, 8));
            if (Theme.PrimaryButton($"Duplicate {preset.Name} and edit", new Vector2(260, 32)))
            {
                var copy = Presets.Duplicate(preset);
                Presets.Activate(copy);
                selectedPresetId = copy.Id;
            }

            ImGui.Dummy(new Vector2(0, 10));
            editorPreview.Draw("##EditorPreviewLocked", new Vector2(0, 240f), Presets.Compiled, preset.Id, editCategory);
            DrawCategoryPicker(preset, readOnly: true);
            return;
        }

        DrawPresetMeta(preset);
        ImGui.Dummy(new Vector2(0, 6));

        var avail = ImGui.GetContentRegionAvail();
        const float listWidth = 200f;

        using (var list = Theme.Region("##CategoryList", new Vector2(listWidth, avail.Y)))
        {
            if (list) DrawCategoryList(preset);
        }

        ImGui.SameLine(0, 10);

        using var detail = Theme.Region("##CategoryDetail", new Vector2(0, avail.Y));
        if (!detail) return;

        var style = preset.Get(editCategory);
        editorPreview.Draw("##EditorPreview", new Vector2(0, 220f), Presets.Compiled, preset.Id, editCategory);
        ImGui.Dummy(new Vector2(0, 4));

        DrawStyleToolbar(preset, style);
        ImGui.Dummy(new Vector2(0, 4));

        Theme.TabStrip("##StyleTabs", ref editTab,
            (FontAwesomeIcon.Font, "Text"),
            (FontAwesomeIcon.Route, "Motion"),
            (FontAwesomeIcon.Bolt, "Impact"),
            (FontAwesomeIcon.Filter, "Behaviour"));
        ImGui.Dummy(new Vector2(0, 6));

        switch (editTab)
        {
            case 0: DrawTextTab(preset, style.Text); break;
            case 1: DrawMotionTab(style.Motion); break;
            case 2: DrawImpactTab(style.Emphasis); break;
            case 3: DrawBehaviourTab(style); break;
        }
    }

    private void DrawPresetMeta(Preset preset)
    {
        var name = preset.Name;
        Row("Preset name");
        ImGui.SetNextItemWidth(ControlWidth);
        if (ImGui.InputText("##presetName", ref name, 40))
        {
            if (name.Trim().Length > 0)
            {
                preset.Name = name;
                changed = true;
            }
        }

        var author = preset.Author;
        ImGui.SameLine(0, 16);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.TextMuted, "Author");
        ImGui.SameLine(0, 8);
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputText("##presetAuthor", ref author, 40))
        {
            preset.Author = author;
            changed = true;
        }

        var description = preset.Description;
        Row("Description");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##presetDesc", ref description, 300))
        {
            preset.Description = description;
            changed = true;
        }
    }

    private void DrawCategoryList(Preset preset)
    {
        foreach (var group in Enum.GetValues<CategoryGroup>())
        {
            ImGui.Dummy(new Vector2(0, 2));
            using (new Theme.FontScale(0.82f))
                ImGui.TextColored(Theme.GoldDim, CategoryInfo.GroupName(group).ToUpperInvariant());

            foreach (var category in CategoryInfo.All.Where(c => CategoryInfo.Group(c) == group))
            {
                var mode = preset.Get(category).Mode;
                var dot = mode switch
                {
                    CategoryMode.Replace => Theme.Gold,
                    CategoryMode.Hide => Theme.Bad,
                    _ => Theme.TextFaint,
                };

                var p = ImGui.GetCursorScreenPos();
                if (ImGui.Selectable($"      {CategoryInfo.Name(category)}##cat{category}", editCategory == category))
                    editCategory = category;
                ImGui.GetWindowDrawList().AddCircleFilled(p + new Vector2(8, ImGui.GetTextLineHeight() * 0.5f + 1), 3.5f, Theme.U32(dot), 12);
            }
        }

        ImGui.Dummy(new Vector2(0, 6));
        ImGui.TextColored(Theme.TextFaint, "Gold: HitSpark draws it\nGrey: the game draws it\nRed: hidden");
    }

    /// <summary>Read-only category list for built-in presets, so the preview can still be browsed.</summary>
    private void DrawCategoryPicker(Preset preset, bool readOnly)
    {
        ImGui.Dummy(new Vector2(0, 6));
        Row("Preview category");
        ImGui.SetNextItemWidth(ControlWidth);
        using var combo = ImRaii.Combo("##previewCategory", CategoryInfo.Name(editCategory));
        if (!combo) return;
        foreach (var category in CategoryInfo.All)
        {
            if (ImGui.Selectable(CategoryInfo.Name(category), category == editCategory))
                editCategory = category;
        }
    }

    private void DrawStyleToolbar(Preset preset, CategoryStyle style)
    {
        var mode = (int)style.Mode;
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.Text, CategoryInfo.Name(editCategory));
        ImGui.SameLine(0, 14);
        if (Theme.SegmentedControl("##mode", ref mode, "HitSpark", "Game", "Hidden"))
        {
            style.Mode = (CategoryMode)mode;
            changed = true;
        }

        if (ImGui.IsItemHovered())
            Theme.Tooltip("HitSpark: drawn by HitSpark with this style. Game: the game's own text, untouched. Hidden: not shown at all.");

        ImGui.SameLine(0, 14);
        if (Theme.GhostButton("Copy style", new Vector2(92, 0)))
            copiedStyle = style.Clone();

        ImGui.SameLine(0, 6);
        using (ImRaii.Disabled(copiedStyle is null))
        {
            if (Theme.GhostButton("Paste", new Vector2(60, 0)) && copiedStyle is not null)
            {
                preset.Styles[editCategory] = copiedStyle.Clone();
                changed = true;
            }
        }

        ImGui.SameLine(0, 6);
        using (ImRaii.Disabled(copiedStyle is null))
        {
            if (Theme.GhostButton("Paste to group", new Vector2(110, 0)) && copiedStyle is not null)
            {
                foreach (var category in CategoryInfo.All.Where(c => CategoryInfo.Group(c) == CategoryInfo.Group(editCategory)))
                    preset.Styles[category] = copiedStyle.Clone();
                changed = true;
            }
        }

        if (ImGui.IsItemHovered())
            Theme.Tooltip($"Pastes the copied style onto every category in {CategoryInfo.GroupName(CategoryInfo.Group(editCategory))}.");
    }

    private void DrawTextTab(Preset preset, TextStyle t)
    {
        Theme.SectionHeader("Font");
        EnumRow("Font", t.Font, v => t.Font = v, FontService.DisplayName);
        FloatRow("Size", t.Size, v => t.Size = v, 8f, 120f, "%.0f px");
        FloatRow("Letter spacing", t.LetterSpacing, v => t.LetterSpacing = v, -4f, 16f, "%.1f px");

        Theme.SectionHeader("Colour");
        ColorRow("Colour", t.Color, v => t.Color = v);
        EnumRow("Tint", t.Tint, v => t.Tint = v, v => v == TintMode.DamageType ? "By damage type (palette)" : "None");
        if (t.Tint == TintMode.DamageType)
        {
            ColorRow("Physical", preset.Palette.Physical, v => preset.Palette.Physical = v);
            ColorRow("Magical", preset.Palette.Magical, v => preset.Palette.Magical = v);
            ColorRow("Unique", preset.Palette.Unique, v => preset.Palette.Unique = v);
        }

        ToggleRow("Gradient", t.Gradient, v => t.Gradient = v, "Blends from the colour at the top to this one at the bottom.");
        if (t.Gradient)
            ColorRow("Bottom colour", t.GradientColor, v => t.GradientColor = v);

        Theme.SectionHeader("Outline & shadow");
        FloatRow("Outline width", t.OutlineWidth, v => t.OutlineWidth = v, 0f, 5f, "%.1f px");
        if (t.OutlineWidth > 0f)
            ColorRow("Outline colour", t.OutlineColor, v => t.OutlineColor = v);
        ToggleRow("Drop shadow", t.Shadow, v => t.Shadow = v);
        if (t.Shadow)
        {
            ColorRow("Shadow colour", t.ShadowColor, v => t.ShadowColor = v);
            FloatRow("Shadow X", t.ShadowOffset.X, v => t.ShadowOffset = t.ShadowOffset with { X = v }, -8f, 8f, "%.1f px");
            FloatRow("Shadow Y", t.ShadowOffset.Y, v => t.ShadowOffset = t.ShadowOffset with { Y = v }, -8f, 8f, "%.1f px");
        }

        Theme.SectionHeader("Content");
        ToggleRow("Digit grouping", t.Separators, v => t.Separators = v, "12,345 instead of 12345.");
        ToggleRow("Abbreviate", t.Abbreviate, v => t.Abbreviate = v, "12.3K, 1.2M for big numbers.");
        if (t.Abbreviate)
        {
            var from = (int)Math.Min(t.AbbreviateFrom, int.MaxValue);
            Row("Abbreviate from");
            ImGui.SetNextItemWidth(ControlWidth);
            if (ImGui.InputInt("##abbrevFrom", ref from, 1000, 10000))
            {
                t.AbbreviateFrom = Math.Max(1000, from);
                changed = true;
            }
        }

        TextRow("Prefix", t.Prefix, v => t.Prefix = v, 8);
        TextRow("Suffix", t.Suffix, v => t.Suffix = v, 12);
        TextRow("Label", t.Label, v => t.Label = v, 20);
        ImGui.SameLine(0, 8);
        Theme.HelpMarker("A fixed caption under the number, like \"DoT Tick\". Leave empty for none. When set, it replaces the action name.");
        ToggleRow("Show action / status name", t.ShowName, v => t.ShowName = v);
        if (t.ShowName || t.Label.Length > 0)
            FloatRow("Caption size", t.NameScale, v => t.NameScale = v, 0.3f, 1.2f, "%.2fx");
        EnumRow("Icon", t.Icon, v => t.Icon = v, v => v switch { IconMode.Left => "Left of the text", IconMode.Right => "Right of the text", _ => "None" });
        if (t.Icon != IconMode.None)
            FloatRow("Icon size", t.IconScale, v => t.IconScale = v, 0.4f, 2f, "%.2fx");
    }

    private void DrawMotionTab(MotionStyle m)
    {
        Theme.SectionHeader("Placement");
        EnumRow("Anchor", m.Anchor, v => m.Anchor = v, v => v switch
        {
            AnchorMode.Actor => "Above the affected character",
            AnchorMode.Self => "Above you",
            AnchorMode.SelfLeft => "Column left of you",
            _ => "Column right of you",
        });
        FloatRow("Offset X", m.OffsetX, v => m.OffsetX = v, -300f, 300f, "%.0f px");
        FloatRow("Offset Y", m.OffsetY, v => m.OffsetY = v, -300f, 300f, "%.0f px");

        Theme.SectionHeader("Path");
        EnumRow("Trajectory", m.Trajectory, v => m.Trajectory = v, v => v switch
        {
            Trajectory.Rise => "Rise",
            Trajectory.Fall => "Fall",
            Trajectory.Arc => "Arc",
            Trajectory.Fountain => "Fountain",
            Trajectory.Lane => "Scrolling column",
            _ => "Stay in place",
        });
        FloatRow("Duration", m.Duration, v => m.Duration = v, 0.3f, 4f, "%.2f s");
        if (m.Trajectory != Trajectory.Fixed)
            FloatRow("Distance", m.Distance, v => m.Distance = v, 0f, 300f, "%.0f px");
        FloatRow("Spread", m.Spread, v => m.Spread = v, 0f, 300f, "%.0f px");
        if (m.Trajectory is Trajectory.Arc or Trajectory.Fountain or Trajectory.Fall)
            EnumRow("Direction", m.ArcDirection, v => m.ArcDirection = v);
        if (m.Trajectory is not (Trajectory.Lane or Trajectory.Fountain or Trajectory.Fixed))
            EnumRow("Path easing", m.PathEasing, v => m.PathEasing = v, Ease.Name);

        Theme.SectionHeader("Entrance & exit");
        FloatRow("Pop scale", m.PopScale, v => m.PopScale = v, 0.2f, 3f, "%.2fx");
        FloatRow("Pop time", m.PopTime, v => m.PopTime = v, 0f, 0.8f, "%.2f s");
        EnumRow("Pop easing", m.PopEasing, v => m.PopEasing = v, Ease.Name);
        FloatRow("Fade in", m.FadeInTime, v => m.FadeInTime = v, 0f, 0.6f, "%.2f s");
        FloatRow("Fade out", m.FadeOutTime, v => m.FadeOutTime = v, 0.05f, 2f, "%.2f s");
        FloatRow("End scale", m.EndScale, v => m.EndScale = v, 0.2f, 2f, "%.2fx");
        FloatRow("Random tilt", m.Tilt, v => m.Tilt = v, 0f, 30f, "%.0f°");
        FloatRow("Stepped motion", m.StepRate, v => m.StepRate = v, 0f, 30f, m.StepRate <= 0f ? "smooth" : "%.0f fps");
    }

    private void DrawImpactTab(EmphasisStyle e)
    {
        Theme.SectionHeader("Punch");
        ToggleRow("Punch", e.Punch, v => e.Punch = v, "Slams in oversized, squashes and settles. Replays when aggregated hits land.");
        if (e.Punch)
        {
            FloatRow("Punch scale", e.PunchScale, v => e.PunchScale = v, 1f, 3f, "%.2fx");
            FloatRow("Punch time", e.PunchTime, v => e.PunchTime = v, 0.08f, 0.8f, "%.2f s");
        }

        Theme.SectionHeader("Shake & flash");
        ToggleRow("Shake", e.Shake, v => e.Shake = v);
        if (e.Shake)
        {
            FloatRow("Shake amount", e.ShakeAmount, v => e.ShakeAmount = v, 1f, 20f, "%.1f px");
            FloatRow("Shake time", e.ShakeTime, v => e.ShakeTime = v, 0.05f, 1f, "%.2f s");
        }

        ToggleRow("Flash", e.Flash, v => e.Flash = v, "A white flash over the number on impact.");
        if (e.Flash)
            FloatRow("Flash time", e.FlashTime, v => e.FlashTime = v, 0.03f, 0.6f, "%.2f s");

        Theme.SectionHeader("Particles & trail");
        ToggleRow("Sparks", e.Sparks, v => e.Sparks = v);
        if (e.Sparks)
        {
            IntRow("Spark count", e.SparkCount, v => e.SparkCount = v, 1, 40);
            FloatRow("Spark speed", e.SparkSpeed, v => e.SparkSpeed = v, 40f, 800f, "%.0f");
            ColorRow("Spark colour", e.SparkColor, v => e.SparkColor = v);
        }

        ToggleRow("Ghost trail", e.Ghost, v => e.Ghost = v, "Faint copies following the number. Best on fast motion.");
        if (e.Ghost)
            IntRow("Ghosts", e.GhostCount, v => e.GhostCount = v, 1, FlyInstance.MaxGhosts);

        ImGui.Dummy(new Vector2(0, 6));
        ImGui.TextColored(Theme.TextFaint, "Shake, sparks and trails switch off when reduced motion is on (Settings → General).");
    }

    private void DrawBehaviourTab(CategoryStyle style)
    {
        Theme.SectionHeader("Show for");
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
        ImGui.TextColored(Theme.TextMuted,
            "Who the event involves, as the game sees it. Unticked groups are hidden. HitSpark never shows more than the game would.");
        ImGui.PopTextWrapPos();
        FlagRow("You and your pets", style, Involvement.You);
        FlagRow("Party members", style, Involvement.Party);
        FlagRow("Others", style, Involvement.Others);

        Theme.SectionHeader("Aggregation");
        var a = style.Aggregation;
        ToggleRow("Merge hits", a.Enabled, v => a.Enabled = v, "Hits landing close together become one number that rolls up.");
        if (a.Enabled)
        {
            FloatRow("Window", a.WindowMs, v => a.WindowMs = v, 100f, 2500f, "%.0f ms");
            EnumRow("Merge", a.Key, v => a.Key = v, v => v == AggregateKey.Action ? "Same action on the same target" : "Anything on the same target");
        }
    }

    // ------------------------------------------------------------------
    // Editor rows
    // ------------------------------------------------------------------

    private static void Row(string label)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.TextMuted, label);
        ImGui.SameLine(LabelWidth);
    }

    private void FloatRow(string label, float value, Action<float> set, float min, float max, string format)
    {
        Row(label);
        var v = value;
        if (Theme.Slider($"##{label}", ref v, min, max, format, ControlWidth))
        {
            set(v);
            changed = true;
        }
    }

    private void IntRow(string label, int value, Action<int> set, int min, int max)
    {
        Row(label);
        var v = value;
        if (Theme.SliderInt($"##{label}", ref v, min, max, ControlWidth))
        {
            set(v);
            changed = true;
        }
    }

    private void ToggleRow(string label, bool value, Action<bool> set, string? help = null)
    {
        Row(label);
        var v = value;
        if (Theme.Toggle($"##tgl_{label}", ref v))
        {
            set(v);
            changed = true;
        }

        if (help is not null)
        {
            ImGui.SameLine(0, 8);
            Theme.HelpMarker(help);
        }
    }

    private void ColorRow(string label, Vector4 value, Action<Vector4> set)
    {
        Row(label);
        var v = value;
        if (ImGui.ColorEdit4($"##{label}", ref v, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf))
        {
            set(v);
            changed = true;
        }
    }

    private void TextRow(string label, string value, Action<string> set, int maxLength)
    {
        Row(label);
        var v = value;
        ImGui.SetNextItemWidth(ControlWidth);
        if (ImGui.InputText($"##{label}", ref v, maxLength))
        {
            set(v);
            changed = true;
        }
    }

    private void EnumRow<T>(string label, T value, Action<T> set, Func<T, string>? name = null) where T : struct, Enum
    {
        name ??= v => Pretty(v.ToString());
        Row(label);
        ImGui.SetNextItemWidth(ControlWidth);
        using var combo = ImRaii.Combo($"##{label}", name(value));
        if (!combo) return;

        foreach (var option in Enum.GetValues<T>())
        {
            if (ImGui.Selectable(name(option), option.Equals(value)))
            {
                set(option);
                changed = true;
            }
        }
    }

    private void FlagRow(string label, CategoryStyle style, Involvement flag)
    {
        Row(label);
        var on = (style.Show & flag) != 0;
        if (Theme.Toggle($"##show_{flag}", ref on))
        {
            style.Show = on ? style.Show | flag : style.Show & ~flag;
            changed = true;
        }
    }

    private static string Pretty(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                sb.Append(' ');
            sb.Append(i == 0 ? name[i] : char.ToLowerInvariant(name[i]));
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // General
    // ------------------------------------------------------------------

    private void DrawGeneral()
    {
        Theme.SectionHeader("Look", FontAwesomeIcon.Eye);

        var scale = Config.GlobalScale;
        Row("Global size");
        if (Theme.Slider("##globalScale", ref scale, 0.5f, 2f, "%.2fx", ControlWidth))
        {
            Config.GlobalScale = scale;
            saveAt = plugin.Now + 1.0;
        }

        var opacity = Config.GlobalOpacity;
        Row("Opacity");
        if (Theme.Slider("##opacity", ref opacity, 0.2f, 1f, "%.2f", ControlWidth))
        {
            Config.GlobalOpacity = opacity;
            saveAt = plugin.Now + 1.0;
        }

        var reduced = (int)Config.ReducedMotion;
        Row("Reduced motion");
        if (Theme.SegmentedControl("##reducedMotion", ref reduced, "Follow Dalamud", "On", "Off"))
        {
            Config.ReducedMotion = (ReducedMotionMode)reduced;
            ConfigService.Save();
        }

        ImGui.SameLine(0, 8);
        Theme.HelpMarker("Turns off shake, sparks and trails, and swaps arcs for a short rise. Follow Dalamud uses the setting in Dalamud's own options.");

        ImGui.Dummy(new Vector2(0, 8));
        Theme.SectionHeader("Other players", FontAwesomeIcon.Users);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
        ImGui.TextColored(Theme.TextMuted,
            "Numbers from actions that don't involve you, such as a party member hitting the boss. " +
            "Anything done to you or by you always counts as yours. Applies to every preset.");
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0, 2));

        DrawGroupRow("Party members", "party", () => Config.ShowParty, v => Config.ShowParty = v,
            () => Config.PartyScale, v => Config.PartyScale = v);
        DrawGroupRow("Others", "others", () => Config.ShowOthers, v => Config.ShowOthers = v,
            () => Config.OthersScale, v => Config.OthersScale = v);

        ImGui.Dummy(new Vector2(0, 8));
        Theme.SectionHeader("Behaviour", FontAwesomeIcon.Cogs);

        var max = Config.MaxLiveNumbers;
        Row("Max numbers");
        if (Theme.SliderInt("##maxLive", ref max, 40, FlyRenderer.Capacity, ControlWidth))
        {
            Config.MaxLiveNumbers = max;
            saveAt = plugin.Now + 1.0;
        }

        ImGui.SameLine(0, 8);
        Theme.HelpMarker("Most numbers on screen at once. When full, the oldest and least important make room; crits go last.");

        var native = Config.NativeMode;
        Row("Native mode");
        if (Theme.Toggle("##nativeMode", ref native))
        {
            Config.NativeMode = native;
            ConfigService.Save();
        }

        ImGui.SameLine(0, 8);
        Theme.HelpMarker("Hands every category back to the game without unloading HitSpark. Also /hitspark native.");

        var test = plugin.TestMode;
        Row("Test in world");
        if (Theme.Toggle("##testMode", ref test))
            plugin.TestMode = test;

        ImGui.SameLine(0, 8);
        Theme.HelpMarker("Plays sample numbers on you and your target. Nothing is sent to the game or other players.");

        ImGui.Dummy(new Vector2(0, 10));
        Theme.Callout(FontAwesomeIcon.ShieldAlt, "Purely cosmetic",
            "HitSpark only redraws text the game already shows you, using your game's own display settings. It adds no combat information and never sends data anywhere.",
            Theme.Crystal);
    }

    /// <summary>One "other players" row: a show toggle, then the size slider while shown.</summary>
    private void DrawGroupRow(string label, string id, Func<bool> getShow, Action<bool> setShow, Func<float> getScale, Action<float> setScale)
    {
        Row(label);
        var show = getShow();
        if (Theme.Toggle($"##show_{id}", ref show))
        {
            setShow(show);
            ConfigService.Save();
        }

        if (!show)
        {
            ImGui.SameLine(0, 10);
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Theme.TextFaint, "Hidden");
            return;
        }

        ImGui.SameLine(0, 14);
        var scale = getScale();
        if (Theme.Slider($"##scale_{id}", ref scale, 0.3f, 1.5f, "%.2fx size", ControlWidth - 50f))
        {
            setScale(scale);
            saveAt = plugin.Now + 1.0;
        }
    }

    // ------------------------------------------------------------------
    // Diagnostics
    // ------------------------------------------------------------------

    private void DrawDiagnostics()
    {
        var pipeline = plugin.Pipeline;
        var renderer = plugin.WorldRenderer;

        Theme.SectionHeader("Status", FontAwesomeIcon.Heartbeat);

        var width = (ImGui.GetContentRegionAvail().X - 24f) / 4f;
        var (statusText, statusColor) = StatusSummary();
        Theme.StatCard(FontAwesomeIcon.Plug, "Status", statusText, statusColor, width);
        ImGui.SameLine(0, 8);
        Theme.StatCard(FontAwesomeIcon.Bolt, "Replaced", pipeline.Replaced.ToString("N0"), Theme.Gold, width,
            $"Captured {pipeline.Captured:N0} · hidden {pipeline.Hidden:N0} · native {pipeline.LeftNative:N0}");
        ImGui.SameLine(0, 8);
        Theme.StatCard(FontAwesomeIcon.LayerGroup, "Live now", $"{renderer.LiveCount} / peak {renderer.PeakLive}", Theme.Crystal, width,
            $"Dropped at the cap: {renderer.Dropped:N0}");
        ImGui.SameLine(0, 8);
        Theme.StatCard(FontAwesomeIcon.Stopwatch, "Frame cost", $"{renderer.LastFrameMs:0.000} ms", Theme.Text, width,
            "CPU time HitSpark spent drawing the last frame.");

        if (pipeline.HookFailure is not null)
        {
            ImGui.Dummy(new Vector2(0, 6));
            Theme.Callout(FontAwesomeIcon.ExclamationTriangle, "Not attached to the game", pipeline.HookFailure +
                " The game's own combat text is unaffected. This usually follows a game patch and is fixed by a HitSpark or Dalamud update.",
                Theme.Bad);
        }

        if (pipeline.TripReason is not null)
        {
            ImGui.Dummy(new Vector2(0, 6));
            Theme.Callout(FontAwesomeIcon.PauseCircle, "Paused after errors",
                $"HitSpark handed combat text back to the game ({pipeline.TripReason}). The log below shows what went wrong.",
                Theme.Warn);
            ImGui.Dummy(new Vector2(0, 4));
            if (Theme.PrimaryButton("Resume HitSpark", new Vector2(150, 30)))
                pipeline.ResetBreaker();
        }

        ImGui.Dummy(new Vector2(0, 8));
        var logEvents = Config.LogEvents;
        if (Theme.ToggleRow("Record every combat event", ref logEvents,
                "Writes each combat text event to the log below: kind, who was involved, values and what HitSpark did with it. " +
                "Useful when a number shows up wrong or in the wrong place. Leave it off otherwise."))
        {
            Config.LogEvents = logEvents;
            ConfigService.Save();
        }

        ImGui.Dummy(new Vector2(0, 8));
        Theme.SectionHeader("Feedback", FontAwesomeIcon.CommentDots);

        Theme.Callout(FontAwesomeIcon.Bug, "Something not working?",
            "1. Turn on Record every combat event, make the problem happen again, then come back to this page.\n" +
            "2. Press Copy report. It copies your HitSpark version, a few settings and the log below. " +
            "Click individual lines first if you only want to share those.\n" +
            "3. Press Report a bug, tell me what you did, what you expected and what happened instead, " +
            "then paste the report into the issue.",
            Theme.Warn);

        ImGui.Dummy(new Vector2(0, 8));
        if (Theme.PrimaryButton("Report a bug", new Vector2(140, 32)))
            OpenUrl(NewIssueUrl("bug", "[Bug] ",
                "**What happened?**\n\n\n**What did you expect?**\n\n\n**Steps to reproduce**\n1. \n\n" +
                "**Diagnostics report**\n<!-- Settings → Diagnostics → Copy report, then paste here -->\n"));

        ImGui.SameLine(0, 8);
        if (Theme.GhostButton("Suggest a feature", new Vector2(150, 32), Theme.Crystal))
            OpenUrl(NewIssueUrl("enhancement", "[Feature] ",
                "**What would you like HitSpark to do?**\n\n\n**How would it help you?**\n\n"));

        ImGui.SameLine(0, 8);
        if (Theme.GhostButton("Open issues", new Vector2(120, 32)))
            OpenUrl($"{RepoUrl}/issues");

        ImGui.Dummy(new Vector2(0, 12));
        Theme.SectionHeader("Log", FontAwesomeIcon.Terminal);

        RefreshLogView();

        if (Theme.SegmentedControl("##LogLevel", ref logLevelFilter, "All", "Info", "Warnings", "Errors"))
            RefreshLogView();

        ImGui.SameLine(0, 10);
        ImGui.SetNextItemWidth(Math.Max(ImGui.GetContentRegionAvail().X, 120f));
        if (ImGui.InputTextWithHint("##LogSearch", "Filter log lines", ref logSearch, 100))
            RefreshLogView();

        ImGui.Dummy(new Vector2(0, 4));
        var copyLabel = selectedLogLines.Count > 0 ? $"Copy report ({selectedLogLines.Count})" : "Copy report";
        if (Theme.PrimaryButton(copyLabel, new Vector2(150, 30)))
            ImGui.SetClipboardText(BuildReport());
        if (ImGui.IsItemHovered())
            Theme.Tooltip(selectedLogLines.Count > 0
                ? "Copies your setup and the selected lines, ready to paste into an issue."
                : $"Copies your setup and the last {ReportLineLimit} lines shown, ready to paste into an issue.");

        ImGui.SameLine(0, 8);
        if (Theme.GhostButton("Copy lines only", new Vector2(130, 30)))
            ImGui.SetClipboardText(string.Join("\n", ReportLines().Select(e => e.Format())));

        if (selectedLogLines.Count > 0)
        {
            ImGui.SameLine(0, 8);
            if (Theme.GhostButton("Deselect", new Vector2(90, 30)))
                selectedLogLines.Clear();
        }

        ImGui.SameLine(0, 8);
        if (Theme.DangerButton("Clear", new Vector2(70, 30)))
        {
            Plugin.Log.Clear();
            selectedLogLines.Clear();
            RefreshLogView();
        }

        ImGui.SameLine(0, 12);
        ImGui.AlignTextToFramePadding();
        ImGui.Checkbox("Auto-scroll", ref logAutoScroll);

        ImGui.Dummy(new Vector2(0, 4));
        DrawLogLines();

        ImGui.Dummy(new Vector2(0, 6));
        Theme.Callout(FontAwesomeIcon.UserSecret, "Check before you share",
            "The event recorder logs character names from your party and nearby players. Feel free to remove anything you'd rather keep private " +
            "before posting. The log only lives in memory and is cleared when the game closes.",
            Theme.TextMuted);
    }

    private void DrawLogLines()
    {
        var height = Math.Max(ImGui.GetContentRegionAvail().Y - 70f, 260f);
        using var card = Theme.Card("##LogCard", new Vector2(0, height));
        if (!card) return;

        using var rows = Theme.Region("##LogRows", Vector2.Zero, ImGuiWindowFlags.HorizontalScrollbar);
        if (!rows) return;

        if (visibleLogLines.Count == 0)
        {
            ImGui.TextColored(Theme.TextFaint, logViewKey.Level == 0 && logSearch.Length == 0
                ? "Nothing logged yet."
                : "No lines match the current filter.");
            return;
        }

        for (var i = 0; i < visibleLogLines.Count; i++)
        {
            var entry = visibleLogLines[i];
            var selected = selectedLogLines.Contains(entry);

            using (ImRaii.PushColor(ImGuiCol.Text, LevelColor(entry.Level)))
            {
                if (ImGui.Selectable($"{entry.Format()}##log{i}", selected))
                {
                    if (!selectedLogLines.Remove(entry))
                        selectedLogLines.Add(entry);
                }
            }

            using var popup = ImRaii.ContextPopupItem($"##logctx{i}");
            if (popup.Success && ImGui.Selectable("Copy this line"))
                ImGui.SetClipboardText(entry.Format());
        }

        if (logAutoScroll && scrolledRevision != Plugin.Log.Revision)
        {
            ImGui.SetScrollHereY(1f);
            scrolledRevision = Plugin.Log.Revision;
        }
    }

    private void RefreshLogView()
    {
        var key = (Plugin.Log.Revision, logLevelFilter, logSearch);
        if (key == logViewKey) return;
        logViewKey = key;

        var minLevel = (LogLevel)logLevelFilter;
        visibleLogLines = Plugin.Log.Snapshot()
            .Where(e => e.Level >= minLevel)
            .Where(e => logSearch.Length == 0
                        || e.Message.Contains(logSearch, StringComparison.OrdinalIgnoreCase)
                        || (e.Exception?.Contains(logSearch, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        // Lines that scrolled out of the buffer can't be copied any more.
        var all = Plugin.Log.Snapshot().ToHashSet();
        selectedLogLines.RemoveWhere(e => !all.Contains(e));
    }

    private IEnumerable<LogEntry> ReportLines()
    {
        return selectedLogLines.Count > 0
            ? selectedLogLines.OrderBy(e => e.Time)
            : visibleLogLines.Skip(Math.Max(0, visibleLogLines.Count - ReportLineLimit));
    }

    private string BuildReport()
    {
        var pipeline = plugin.Pipeline;
        var sb = new StringBuilder();

        sb.AppendLine("### HitSpark diagnostics");
        sb.AppendLine($"- HitSpark: {Changelog.CurrentVersion}");
        sb.AppendLine($"- Dalamud: {typeof(Dalamud.Plugin.IDalamudPluginInterface).Assembly.GetName().Version}");
        sb.AppendLine($"- Game language: {Plugin.ClientState.ClientLanguage}");
        sb.AppendLine($"- Preset: {Presets.Active.Name}{(Presets.Active.IsBuiltIn ? " (built-in)" : string.Empty)}");
        sb.AppendLine($"- Hooks: {(pipeline.HooksActive ? "active" : pipeline.HookFailure ?? "inactive")}, tick hook: {(pipeline.TickHookActive ? "active" : "off")}, breaker: {pipeline.TripReason ?? "ok"}, native mode: {Config.NativeMode}");
        sb.AppendLine($"- Events: captured {pipeline.Captured}, replaced {pipeline.Replaced}, hidden {pipeline.Hidden}, native {pipeline.LeftNative}, errors {pipeline.Errors}");
        var world = plugin.WorldRenderer;
        sb.AppendLine($"- Renderer: spawned {world.Spawned}, live {world.LiveCount}, queued {world.Queued}, peak {world.PeakLive}, dropped {world.Dropped}, " +
                      $"last frame {world.LastFrameMs:0.000} ms, cap {Config.MaxLiveNumbers}");
        sb.AppendLine($"- Game display options: {Capture.GameDisplay.Describe()}");
        sb.AppendLine($"- World: {(Plugin.HiddenReason is { } hidden ? $"hidden ({hidden})" : "visible")}, frame errors {plugin.FrameErrors}, test mode {plugin.TestMode}");
        sb.AppendLine($"- Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine("```text");
        foreach (var entry in ReportLines())
            sb.AppendLine(entry.Format());
        sb.AppendLine("```");

        return sb.ToString();
    }

    private static Vector4 LevelColor(LogLevel level) => level switch
    {
        LogLevel.Error => Theme.Bad,
        LogLevel.Warning => Theme.Warn,
        LogLevel.Info => Theme.Text,
        _ => Theme.TextMuted,
    };

    private static string NewIssueUrl(string label, string title, string body)
        => $"{RepoUrl}/issues/new?labels={label}&title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";

    // ------------------------------------------------------------------
    // About
    // ------------------------------------------------------------------

    private void DrawAbout()
    {
        Theme.SectionHeader("HitSpark", FontAwesomeIcon.Bolt);

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.TextMuted, $"Version {Changelog.CurrentVersion}  ·  by GitHixy");
        ImGui.SameLine(0, 12);
        if (Theme.GhostButton("What's new", new Vector2(120, 0), Theme.Gold))
            plugin.ChangelogWindow.ShowAll();

        ImGui.Dummy(new Vector2(0, 6));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Min(ImGui.GetContentRegionAvail().X, 460f));
        ImGui.TextColored(Theme.Text,
            "A complete, preset-driven replacement for Final Fantasy XIV's combat text: better type, real motion and impact on crits.");
        ImGui.PopTextWrapPos();

        ImGui.Dummy(new Vector2(0, 14));
        Theme.SectionHeader("Support", FontAwesomeIcon.Heart);

        using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(1.0f, 0.26f, 0.30f, 0.85f))
                   .Push(ImGuiCol.ButtonHovered, new Vector4(1.0f, 0.36f, 0.40f, 1.0f))
                   .Push(ImGuiCol.ButtonActive, new Vector4(0.85f, 0.20f, 0.24f, 1.0f))
                   .Push(ImGuiCol.Text, Theme.Text))
        {
            if (ImGui.Button("Support me on Patreon", new Vector2(230, 34)))
                OpenUrl("https://www.patreon.com/GitHixy");
        }

        ImGui.SameLine(0, 10);
        if (Theme.GhostButton("GitHub", new Vector2(110, 34)))
            OpenUrl(RepoUrl);

        ImGui.SameLine(0, 10);
        if (Theme.GhostButton("Report a problem", new Vector2(150, 34)))
            section = Array.FindIndex(Sections, s => s.Label == "Diagnostics");

        ImGui.Dummy(new Vector2(0, 14));
        Theme.SectionHeader("Shortcuts", FontAwesomeIcon.Keyboard);

        ShortcutRow("/hitspark", "Open these settings (also /hs)");
        ShortcutRow("/hitspark test", "Toggle test numbers around you");
        ShortcutRow("/hitspark native", "Hand combat text back to the game");

        ImGui.Dummy(new Vector2(0, 14));
        Theme.SectionHeader("Credits", FontAwesomeIcon.Award);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Min(ImGui.GetContentRegionAvail().X, 520f));
        ImGui.TextColored(Theme.TextMuted,
            "Fonts: Bebas Neue, Anton, Russo One, Barlow, Barlow Condensed, Lilita One, Cinzel Decorative and Press Start 2P, " +
            "all under the SIL Open Font License (see Assets/Fonts/licenses). The Classic+ numerals are the game's own, loaded from your installation.\n\n" +
            "Thanks to the authors of CBT, DamageInfoPlugin, MultiHit and FlyTextFilter, whose work mapped out how the game's combat text flows, " +
            "and to the FFXIVClientStructs and Dalamud teams.");
        ImGui.PopTextWrapPos();
    }

    private static void ShortcutRow(string command, string description)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var ts = ImGui.CalcTextSize(command);

        dl.AddRectFilled(p, new Vector2(p.X + ts.X + 14, p.Y + ts.Y + 5), Theme.U32(Theme.Panel, 0.95f), 4f);
        dl.AddRect(p, new Vector2(p.X + ts.X + 14, p.Y + ts.Y + 5), Theme.U32(Theme.Line), 4f, ImDrawFlags.None, 1f);
        dl.AddText(new Vector2(p.X + 7, p.Y + 2.5f), Theme.U32(Theme.GoldBright), command);

        ImGui.Dummy(new Vector2(Math.Max(ts.X + 14, 150f), ts.Y + 5));
        ImGui.SameLine(0, 12);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.TextMuted, description);
        ImGui.Dummy(new Vector2(0, 2));
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to open {Url}", url);
        }
    }
}
