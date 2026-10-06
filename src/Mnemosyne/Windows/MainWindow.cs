using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Switch;
using Mnemosyne.Core.Time;
using Mnemosyne.Look;

namespace Mnemosyne.Windows;

/// <summary>The sidebar, the "All characters" matrix, and one character's checklist, which CharacterPage draws. Reads the tracker's records
/// through the checklist cache; every edit goes through the tracker or the plugin's settings update. The sidebar carries a switch button on
/// each row. All tasks sits above All characters, drawn by TasksView with the sidebar's own switch button; the window opens on All tasks, a
/// character's name under an All tasks line opens that character's view, and a locked row is drawn dimmed with "not unlocked yet". The
/// sidebar has a Role submenu, the table has a Role column and dimmed "off for &lt;role&gt;" rows, and a My tasks row's box is a real
/// checkbox. The table also carries gil, ventures ready, bag space and company seals columns, and the Totals page follows All characters,
/// drawn by TotalsView, ending with the Gil over time graph. Both windows draw in the Starlight look (Look): the sidebar's page tabs carry
/// icons and a tile holds each character, with its badge as a pill in its urgency tone and the switch as an icon (SwitchIcon); every click,
/// menu, tooltip and id is the one it always had. The All characters table is drawn in the look too (DrawMatrix), as TasksView and TotalsView
/// draw All tasks and Totals.</summary>
public sealed class MainWindow : Window
{
    private static readonly IReadOnlyDictionary<MatrixColumn, string> ColumnLabels = new Dictionary<MatrixColumn, string>
    {
        [MatrixColumn.Name] = "Character", [MatrixColumn.Role] = "Role", [MatrixColumn.Msq] = "MSQ", [MatrixColumn.Jobs] = "Jobs at cap", [MatrixColumn.Events] = "Events",
        [MatrixColumn.Dailies] = "Dailies left", [MatrixColumn.Weeklies] = "Weeklies left", [MatrixColumn.EndingSoon] = "Ending soon",
        [MatrixColumn.LastSeen] = "Last seen", [MatrixColumn.Badge] = "To do",
        // The four holdings columns, after To do.
        [MatrixColumn.Gil] = "Gil", [MatrixColumn.Ventures] = "Ventures ready", [MatrixColumn.Bags] = "Bag space", [MatrixColumn.Seals] = "Company seals",
    };

    /// <summary>Headers are in the section-label style: the table's header words, in capitals. The Sort box keeps ColumnLabels. A header's
    /// words are no id ImGui keeps anything under: it keeps a column's width by the table's id and the columns' count and order.</summary>
    private static readonly IReadOnlyDictionary<MatrixColumn, string> HeaderLabels = ColumnLabels.ToDictionary(pair => pair.Key, pair => pair.Value.ToUpperInvariant());

    /// <summary>The widest a cell's small bar is, at 100 %; a narrower column narrows it.</summary>
    private const float CellBarWidth = 96f;

    private readonly Plugin _plugin;
    private MatrixColumn _sortColumn = MatrixColumn.Badge;
    private bool _sortDescending = true;
    private ulong? _renaming;
    private string _renameDraft = "";
    private ulong? _forgetting;
    private bool _forgetOpened;
    private readonly TasksView _tasks;
    private readonly TotalsView _totals;
    /// <summary>A character's page, with the search text and the Show done choices it keeps.</summary>
    private readonly CharacterPage _character;
    // The page drawn while no character is selected. The window opens on All tasks.
    private enum Page { AllTasks, AllCharacters, Totals }
    private Page _page = Page.AllTasks;
    /// <summary>The sidebar keeps its width, at 100 %, scaled by the global UI scale.</summary>
    private const float SidebarWidth = 240f;

    public ulong? Selected { get; private set; }

    public MainWindow(Plugin plugin) : base("Mnemosyne##main")
    {
        _plugin = plugin;
        _tasks = new TasksView(plugin, id => Select(id));
        _totals = new TotalsView(plugin);
        _character = new CharacterPage(plugin);
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(700, 420), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
        // A cog in the title bar opens the settings, which the role menu and the first-run notice point to.
        TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Cog,
            IconOffset = new Vector2(2f, 1f),
            ShowTooltip = () => ImGui.SetTooltip("Settings"),
            Click = _ =>
            {
                plugin.SettingsWindow.IsOpen = true;
                plugin.SettingsWindow.BringToFront();
            },
        });
    }

    public void Select(ulong? contentId)
    {
        Selected = contentId;
        _page = Page.AllCharacters;
        IsOpen = true;
    }

    public bool SelectByName(string prefix)
    {
        var hit = _plugin.Tracker.All.FirstOrDefault(r => !r.Hidden
            && (r.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || r.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
        if (hit == null) return false;
        Select(hit.ContentId);
        return true;
    }

    private sealed record Row(CharacterRecord Record, Checklist Checklist);

    /// <summary>The palette and the window style, pushed before the window begins and popped after it ends (Starlight), so nothing leaks into
    /// the game's windows or another plugin's. Its popups, menus, tooltips and the forget modal are drawn between the two, so they take it
    /// too.</summary>
    public override void PreDraw() => Starlight.Push();

    public override void PostDraw() => Starlight.Pop();

    public override void Draw()
    {
        var now = DateTimeOffset.UtcNow;
        // Two characters at most are re-assembled per frame, and the selected one is touched first so the checklist this
        // frame actually draws is never the one held back (see ChecklistCache).
        _plugin.Cache.BeginFrame(2);
        if (Selected is { } selectedId && _plugin.Tracker.All.FirstOrDefault(r => r.ContentId == selectedId && !r.Hidden) is { } shown)
            _plugin.ChecklistFor(shown, now);
        // The sidebar's order lives in Plugin.Characters, which the All tasks board and the dump share.
        var characters = _plugin.Characters(now);
        var rows = characters.Select(c => new Row(c.Record, c.Checklist)).ToList();
        // Dalamud's reduce-motion setting holds the pulses still. Read once a frame.
        var reduced = Plugin.PluginInterface.UiBuilder.ShouldUseReducedMotion;

        // The sidebar keeps its width at the global UI scale, and scrolls on its own.
        using (ImRaii.PushColor(ImGuiCol.ChildBg, Palette.Sidebar))
        using (var sidebar = ImRaii.Child("sidebar", new Vector2(SidebarWidth * ImGuiHelpers.GlobalScale, 0f), true))
        {
            if (sidebar) DrawSidebar(rows, characters, now, reduced);
        }
        ImGui.SameLine();
        using (var main = ImRaii.Child("main", Vector2.Zero, false))
        {
            if (main)
            {
                var selected = Selected is { } id ? rows.FirstOrDefault(r => r.Record.ContentId == id) : null;
                if (selected != null) _character.Draw(selected.Record, selected.Checklist, now, reduced);
                else
                {
                    Selected = null;
                    switch (_page)
                    {
                        case Page.AllTasks: _tasks.Draw(characters, now, reduced); break;
                        case Page.Totals: _totals.Draw(characters, now); break;
                        default: DrawMatrix(rows, now); break;
                    }
                }
            }
        }
        DrawForgetModal();
    }

    /// <summary>The page tabs, "CHARACTERS (n)", then one tile per character, in the sidebar's order. Every click, the right-click menu and
    /// the switch behave as they did, under the same ids.</summary>
    private void DrawSidebar(List<Row> rows, IReadOnlyList<(CharacterRecord Record, Checklist Checklist)> characters, DateTimeOffset now, bool reduced)
    {
        // All tasks first, then All characters, then Totals. All tasks'
        // pill counts the lines its page lists, from the very board the page draws, which Plugin.BoardFor keeps while nothing moves.
        var lines = _plugin.BoardFor(characters, now).Groups.Sum(section => section.Lines.Count);
        if (DrawTab("All tasks", Glyphs.AllTasks, Selected == null && _page == Page.AllTasks, lines)) { Selected = null; _page = Page.AllTasks; }
        if (DrawTab("All characters", Glyphs.AllCharacters, Selected == null && _page == Page.AllCharacters, 0)) { Selected = null; _page = Page.AllCharacters; }
        if (DrawTab("Totals", Glyphs.Totals, Selected == null && _page == Page.Totals, 0)) { Selected = null; _page = Page.Totals; }
        ImGui.Spacing();
        Blocks.SectionLabel(_plugin.Fonts.Label, $"Characters ({rows.Count})");
        var current = _plugin.Tracker.Current?.ContentId;
        // Read once a frame; the installed-plugin list behind it is re-read only after Dalamud says it changed.
        var switchable = _plugin.Relog.IsAvailable;
        var silenced = _plugin.Attention.Suppressed;
        var weekliesCount = _plugin.Settings.WeekliesCount;
        foreach (var row in rows)
            DrawTile(row, SwitchRules.State(switchable, row.Record, current, silenced), current == row.Record.ContentId, weekliesCount, now, reduced);
        if (rows.Count == 0) ImGui.TextDisabled("Log into a character and it appears here.");
    }

    /// <summary>A page tab. Its Selectable keeps its label, so its id is the one it always had; the Selectable's own text and highlight are
    /// drawn transparent (Blocks.Invisible) and the tab is drawn over it: the icon, the name, the accent fill and violet edge of the current
    /// page, a fill on hover, and a count in a pill when there is one.</summary>
    private static bool DrawTab(string label, FontAwesomeIcon icon, bool current, int count)
    {
        bool clicked;
        using (Blocks.Invisible())
            clicked = ImGui.Selectable(label, current, ImGuiSelectableFlags.None, new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight()));
        var hovered = ImGui.IsItemHovered();
        var (min, max) = Blocks.Visible(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        var drawList = ImGui.GetWindowDrawList();
        var rounding = Blocks.S(6f);
        if (current)
        {
            drawList.AddRectFilled(min, max, Palette.U32(Palette.Fade(Palette.Accent, 0.16f)), rounding);
            Blocks.EdgeAt(drawList, min, max, Palette.Accent, rounding);
        }
        else if (hovered) drawList.AddRectFilled(min, max, Palette.U32(Palette.CardHover), rounding);
        var y = min.Y + (max.Y - min.Y - ImGui.GetTextLineHeight()) * 0.5f;
        var x = min.X + Blocks.S(10f);
        Blocks.GlyphAt(drawList, new Vector2(x, y), icon, current ? Palette.Accent : Palette.TextDim);
        drawList.AddText(new Vector2(x + Blocks.S(24f), y), Palette.U32(current ? Palette.Text : Palette.TextSecondary), label);
        if (count > 0)
        {
            var text = count.ToString(CultureInfo.InvariantCulture);
            var size = Blocks.PillSize(text);
            Blocks.PillAt(drawList, new Vector2(max.X - Blocks.S(8f) - size.X, min.Y + (max.Y - min.Y - size.Y) * 0.5f), text, Palette.Accent);
        }
        return clicked;
    }

    /// <summary>One character's tile. Its Selectable keeps the label it always had ("● " before the logged-in character's), so its id, and
    /// its right-click menu ("menu") follows it at once, as before; what shows is drawn over it: the gold glowing dot of the logged-in
    /// character, the name, the world below it in secondary text, a role chip, the badge as a pill in the character's urgency tone or a green
    /// check at zero, the accent when selected and a fill on hover. A stale tile is dimmed and says how long ago the character was seen. The
    /// switch is an icon at the tile's bottom right (SwitchIcon), with its states and tooltips as before. A name or world too long for the
    /// tile is cut with "…" and shown whole in a tooltip.</summary>
    private void DrawTile(Row row, SwitchState state, bool loggedIn, bool weekliesCount, DateTimeOffset now, bool reduced)
    {
        var record = row.Record;
        using var id = ImRaii.PushId(record.ContentId.ToString());
        var line = ImGui.GetTextLineHeight();
        var pad = Blocks.S(8f);
        var label = (loggedIn ? "● " : "") + record.Label;
        // The width is given, not left to ImGui: a Selectable left to size itself is at least as wide as its label, and a long display name
        // would push the tile, its badge and its switch past the sidebar's edge.
        var tile = new Vector2(ImGui.GetContentRegionAvail().X, pad * 2f + line * 2f + Blocks.S(2f));
        bool clicked;
        using (Blocks.Invisible()) clicked = ImGui.Selectable(label, Selected == record.ContentId, ImGuiSelectableFlags.AllowItemOverlap, tile);
        if (clicked)
        {
            Selected = record.ContentId;
            _page = Page.AllCharacters;
        }
        var hovered = ImGui.IsItemHovered();
        var (min, max) = Blocks.Visible(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        DrawCharacterMenu(record);
        var after = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var rounding = Blocks.S(8f);
        if (Selected == record.ContentId)
        {
            drawList.AddRectFilled(min, max, Palette.U32(Palette.Fade(Palette.Accent, 0.16f)), rounding);
            drawList.AddRect(min, max, Palette.U32(Palette.Fade(Palette.Accent, 0.55f)), rounding);
        }
        else if (hovered) drawList.AddRectFilled(min, max, Palette.U32(Palette.CardHover), rounding);

        var right = max.X - pad;
        var switchSide = line + Blocks.S(4f);
        var role = _plugin.Settings.RoleOf(record)?.Name ?? (string.IsNullOrEmpty(record.Role) ? null : Summariser.UnknownRole);
        bool nameCut, worldCut;
        // A cut role chip's whole name is found inside the fade but shown after it, so its tooltip is never dimmed,
        // and it takes the place of the name's tooltip, so one frame shows one tooltip.
        string? roleTip = null;
        using (Blocks.Faded(row.Checklist.Stale ? Blocks.StaleAlpha : 1f))
        {
            // The first line: the badge on the right; the gold dot and the name on the left.
            float badgeWidth;
            if (row.Checklist.Badge > 0)
            {
                var badge = row.Checklist.Badge.ToString(CultureInfo.InvariantCulture);
                var size = Blocks.PillSize(badge);
                Blocks.PillAt(drawList, new Vector2(right - size.X, min.Y + pad + (line - size.Y) * 0.5f), badge, Palette.Of(Tones.Urgency(row.Checklist, weekliesCount)));
                badgeWidth = size.X;
            }
            else
            {
                badgeWidth = Blocks.GlyphSize(Glyphs.Check).X;
                Blocks.GlyphAt(drawList, new Vector2(right - badgeWidth, min.Y + pad), Glyphs.Check, Palette.Done);
            }
            var x = min.X + pad;
            if (loggedIn)
            {
                Blocks.DotAt(drawList, new Vector2(x + Blocks.S(4f), min.Y + pad + line * 0.5f), Palette.Gold, glow: true);
                x += Blocks.S(14f);
            }
            (var name, nameCut) = Blocks.Fit(record.Label, right - badgeWidth - Blocks.S(6f) - x);
            drawList.AddText(new Vector2(x, min.Y + pad), Palette.U32(Palette.Text), name);

            // The second line: the world, and how long ago when stale, then the role chip; the switch keeps the corner. A
            // role name can run to 31 characters, so the chip is cut to at most half the tile's line (Blocks.Fit) rather than reserving its
            // whole width, which would leave no room for the world or run the chip past the tile's edge; the whole name is shown in a
            // tooltip on the chip itself.
            var lineTwo = min.Y + pad + line + Blocks.S(2f);
            var world = row.Checklist.Stale ? $"{record.HomeWorld} · {Relative.Since(record.LastSeen, now)}" : record.HomeWorld;
            string? roleText = role;
            var roleCut = false;
            if (role != null) (roleText, roleCut) = Blocks.Fit(role, (max.X - min.X) * 0.5f);
            var roleWidth = role != null ? Blocks.PillSize(roleText!).X + Blocks.S(6f) : 0f;
            var worldRight = max.X - Blocks.S(4f) - (state != SwitchState.Hidden ? switchSide + Blocks.S(4f) : 0f) - roleWidth;
            (var place, worldCut) = Blocks.Fit(world, worldRight - (min.X + pad));
            drawList.AddText(new Vector2(min.X + pad, lineTwo), Palette.U32(Palette.TextSecondary), place);
            if (role != null)
            {
                var pillMin = new Vector2(min.X + pad + ImGui.CalcTextSize(place).X + Blocks.S(6f), lineTwo - Blocks.S(1f));
                var pillSize = Blocks.PillSize(roleText!);
                Blocks.PillAt(drawList, pillMin, roleText!, role == Summariser.UnknownRole ? Palette.Quiet : Palette.TextSecondary);
                if (roleCut && ImGui.IsMouseHoveringRect(pillMin, pillMin + pillSize)) roleTip = role;
            }
        }
        if (roleTip != null) ImGui.SetTooltip(roleTip);
        else if (hovered && (nameCut || worldCut)) ImGui.SetTooltip($"{record.Label}\n{record.HomeWorld}");
        if (state != SwitchState.Hidden)
            SwitchIcon.Draw(_plugin, record, state, new Vector2(max.X - Blocks.S(4f) - switchSide, max.Y - Blocks.S(4f) - switchSide), switchSide, now, reduced);
        ImGui.SetCursorScreenPos(after);
    }

    /// <summary>Rename display, the character's role, hide, forget. Forget asks for confirmation so a stray click cannot delete a
    /// file.</summary>
    private void DrawCharacterMenu(CharacterRecord record)
    {
        using var popup = ImRaii.ContextPopupItem("menu");
        if (!popup) return;
        if (_renaming != record.ContentId)
        {
            if (ImGui.MenuItem("Rename display")) { _renaming = record.ContentId; _renameDraft = record.DisplayName ?? ""; }
        }
        else
        {
            ImGui.SetNextItemWidth(180f);
            if (ImGui.InputTextWithHint("##rename", record.Name, ref _renameDraft, 40, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                _plugin.Tracker.Rename(record.ContentId, _renameDraft);
                _renaming = null;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear")) { _plugin.Tracker.Rename(record.ContentId, null); _renaming = null; }
            ImGui.SameLine();
            if (ImGui.SmallButton("Cancel")) _renaming = null;
        }
        DrawRoleMenu(record);
        if (ImGui.MenuItem("Hide from the list")) { _plugin.Tracker.Hide(record.ContentId, true); if (Selected == record.ContentId) Selected = null; }
        if (ImGui.MenuItem("Forget this character...")) _forgetting = record.ContentId;
    }

    /// <summary>Every role and None, in the order they were made, the current one ticked. A record whose id this PC does not have shows a
    /// ticked, greyed "unknown role" line with None unticked: it behaves as no role, and keeps the id until None or a role is chosen. Each
    /// choice is one Tracker edit, which moves that record's version alone, and asks the info bar to follow on the next frame.</summary>
    private void DrawRoleMenu(CharacterRecord record)
    {
        using var menu = ImRaii.Menu("Role");
        if (!menu) return;
        var current = _plugin.Settings.RoleOf(record);
        var none = string.IsNullOrEmpty(record.Role);
        if (ImGui.MenuItem(RoleList.NoneName, none)) Assign(null);
        if (current == null && !none) ImGui.MenuItem(Summariser.UnknownRole, true, false);
        foreach (var role in _plugin.Settings.Roles.Usable)
        {
            using var id = ImRaii.PushId(role.Id);
            if (ImGui.MenuItem(role.Name, current?.Id == role.Id)) Assign(role.Id);
        }
        if (!_plugin.Settings.Roles.Usable.Any()) ImGui.TextDisabled("Make a role on the settings page.");

        void Assign(string? roleId)
        {
            _plugin.Tracker.SetRole(record.ContentId, roleId);
            _plugin.Attention.RefreshSoon();
        }
    }

    /// <summary>Forget deletes the file only after a confirmation.</summary>
    private void DrawForgetModal()
    {
        if (_forgetting is not { } id) return;
        var record = _plugin.Tracker.All.FirstOrDefault(r => r.ContentId == id);
        if (record == null) { _forgetting = null; _forgetOpened = false; return; }
        const string title = "Forget character##forget";
        if (_forgetOpened && !ImGui.IsPopupOpen(title)) { _forgetting = null; _forgetOpened = false; return; }
        if (!ImGui.IsPopupOpen(title)) { ImGui.OpenPopup(title); _forgetOpened = true; }
        using var modal = ImRaii.PopupModal(title);
        if (!modal) return;
        ImGui.TextWrapped($"Forget {record.Label}? Its file is deleted. Logging into the character again starts a fresh record.");
        if (ImGui.Button("Forget"))
        {
            _plugin.Tracker.Forget(id);
            _plugin.Cache.Remove(id);
            if (Selected == id) Selected = null;
            _forgetting = null;
            _forgetOpened = false;
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel")) { _forgetting = null; _forgetOpened = false; ImGui.CloseCurrentPopup(); }
    }

    /// <summary>The characters ticked in All characters' first column, waiting for Give role.</summary>
    private readonly HashSet<ulong> _ticked = new();

    /// <summary>The All characters table, in a card as tall as the page. It keeps its columns, its sorting (the Sort box and Descending, as
    /// before) and its row click; its headers are small dim capitals, the current sort column's with an arrow of its direction (DrawHeaders);
    /// alternate rows are softly tinted by the style's own table colours, a row lights up under the mouse, and a stale row is dimmed, every
    /// cell of it (DrawRow). With no character to list, the page says so in one quiet line instead of an empty table. Every id is the one it
    /// had.</summary>
    private void DrawMatrix(List<Row> rows, DateTimeOffset now)
    {
        var matrix = Summariser.Sort(rows.Select(r => _plugin.RowFor(r.Record, now)), _sortColumn, _sortDescending);
        _ticked.IntersectWith(matrix.Select(r => r.ContentId));    // a character hidden or forgotten since it was ticked drops out

        Blocks.PageTitle(_plugin.Fonts.Title, Glyphs.AllCharacters, "All characters", "");
        if (matrix.Count == 0)
        {
            // The sidebar's own words for an empty list, not a page-tall card of headers alone.
            ImGui.TextColored(Palette.Quiet, "Log into a character and it appears here.");
            return;
        }
        ImGui.SetNextItemWidth(Blocks.S(170f));
        using (var combo = ImRaii.Combo("##sort", "Sort: " + ColumnLabels[_sortColumn]))
        {
            if (combo)
            {
                foreach (var column in Enum.GetValues<MatrixColumn>())
                {
                    if (ImGui.Selectable(ColumnLabels[column], column == _sortColumn)) _sortColumn = column;
                }
            }
        }
        ImGui.SameLine();
        ImGui.Checkbox("Descending", ref _sortDescending);
        ImGui.SameLine();
        DrawGiveRole();

        // A first, narrow column holds each row's tick; the name's selectable still spans the row and still opens the character.
        // The table stops where the card's padding starts, at its right and its foot, so the card holds it and the page itself
        // never scrolls; the table scrolls, as before.
        using var card = Blocks.Card();
        var size = new Vector2(card.InnerRight - ImGui.GetCursorScreenPos().X,
            MathF.Max(ImGui.GetFrameHeight() * 4f, ImGui.GetContentRegionAvail().Y - Blocks.S(Blocks.CardPadding) - 1f));
        using var table = ImRaii.Table("matrix", Enum.GetValues<MatrixColumn>().Length + 1, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable, size);
        if (!table) return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("##tick", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
        foreach (var column in Enum.GetValues<MatrixColumn>())
            ImGui.TableSetupColumn(HeaderLabels[column]);
        DrawHeaders();

        var weekliesCount = _plugin.Settings.WeekliesCount;
        var rowHeight = ImGui.GetFrameHeight();
        foreach (var row in matrix)
        {
            using var id = ImRaii.PushId(row.ContentId.ToString());
            ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
            // A stale character's row is dimmed, every cell of it. A cell's tooltip is found inside the fade and shown after it, so it is
            // never dimmed, as the sidebar's tile does.
            string? tip;
            using (Blocks.Faded(row.Stale ? Blocks.StaleAlpha : 1f)) tip = DrawRow(row, weekliesCount, rowHeight, now);
            if (tip != null) ImGui.SetTooltip(tip);
        }
    }

    /// <summary>One row's cells, reading Core's rules (PageLook). Returns the whole words of the cut cell under the mouse, if any, for the caller
    /// to show after the row's fade.</summary>
    private string? DrawRow(MatrixRow row, bool weekliesCount, float rowHeight, DateTimeOffset now)
    {
        string? tip = null;
        ImGui.TableNextColumn();                                   // the tick column, drawn after the row's selectable so the box wins a click
        ImGui.TableNextColumn();
        // The row's Selectable keeps its label, so its id; its own text and highlight are drawn transparent, the row lights up through the
        // table's own row colour, and the name is drawn over it, cut to its column.
        var cell = ImGui.GetCursorScreenPos();
        var nameRoom = ImGui.GetContentRegionAvail().X;
        bool clicked;
        using (Blocks.Invisible())
            clicked = ImGui.Selectable(row.Label + "##row", false, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap, new Vector2(0f, rowHeight));
        if (clicked) Selected = row.ContentId;
        var rowHovered = ImGui.IsItemHovered();
        if (rowHovered) ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, Palette.U32(Palette.CardHover));
        var (name, nameCut) = Blocks.Fit(row.Label, nameRoom);
        ImGui.GetWindowDrawList().AddText(new Vector2(cell.X, cell.Y + (rowHeight - ImGui.GetTextLineHeight()) * 0.5f), Palette.U32(Palette.Text), name);
        if (nameCut && rowHovered && ImGui.IsMouseHoveringRect(cell, cell + new Vector2(nameRoom, rowHeight))) tip = row.Label;
        ImGui.TableSetColumnIndex(0);
        var ticked = _ticked.Contains(row.ContentId);
        if (ImGui.Checkbox("##tick", ref ticked)) { if (ticked) _ticked.Add(row.ContentId); else _ticked.Remove(row.ContentId); }
        ImGui.TableSetColumnIndex(1);
        // The role's name as a chip, "unknown role" quiet, or nothing.
        ImGui.TableNextColumn();
        if (row.Role.Length > 0) tip = CellPill(row.Role, row.Role == Summariser.UnknownRole ? Palette.Quiet : Palette.TextSecondary) ?? tip;
        ImGui.TableNextColumn(); tip = CellText(row.MsqPosition, Palette.TextSecondary) ?? tip;
        // Jobs at cap is a small bar, its words inside; "0 / 0" alone, with no job read.
        ImGui.TableNextColumn();
        var jobs = $"{row.JobsAtCap} / {row.JobsUnlocked}";
        tip = (PageLook.Jobs(row) is { } meter ? CellBar(meter, jobs, Palette.Accent) : CellText(jobs, Palette.TextSecondary)) ?? tip;
        ImGui.TableNextColumn(); tip = CellText($"{row.EventsDone} / {row.EventsAvailable}", Palette.TextSecondary) ?? tip;
        // Dailies and weeklies left are pills in their tones; ending soon left the same; a count of none quiet.
        ImGui.TableNextColumn(); tip = CellPill(Count(row.DailiesLeft), Palette.Of(PageLook.OfLeft(row.DailiesLeft, Tone.Today))) ?? tip;
        ImGui.TableNextColumn(); tip = CellPill(Count(row.WeekliesLeft), Palette.Of(PageLook.OfLeft(row.WeekliesLeft, Tone.ThisWeek))) ?? tip;
        ImGui.TableNextColumn(); tip = CellPill(Count(row.EndingSoon), Palette.Of(PageLook.OfLeft(row.EndingSoon, Tone.EndingSoon))) ?? tip;
        ImGui.TableNextColumn(); tip = CellText(Relative.Since(row.LastSeen, now), Palette.TextSecondary) ?? tip;
        // The badge is the sidebar's pill: in the character's urgency tone, or a green check at zero.
        ImGui.TableNextColumn();
        if (row.Badge > 0) tip = CellPill(Count(row.Badge), Palette.Of(PageLook.Urgency(row, weekliesCount))) ?? tip;
        else
        {
            ImGui.AlignTextToFramePadding();
            Blocks.Glyph(Glyphs.Check, Palette.Done);
        }
        // In MatrixColumn's order, each blank when nothing is stored: gil, the retainers' share in secondary text;
        // ventures ready after a dot, green when some are ready; bag space, a bar of how full the bags are, rose at 5 free or fewer; the current
        // company's seals, a bar toward the cap, or the count alone with no cap.
        ImGui.TableNextColumn(); tip = CellGil(row) ?? tip;
        ImGui.TableNextColumn();
        if (row.Ventures > 0) tip = CellDot(row.VenturesText, Palette.Of(PageLook.OfVentures(row))) ?? tip;
        ImGui.TableNextColumn();
        if (PageLook.Bags(row) is { } bags) tip = CellBar(bags, row.BagsText, Palette.BarOf(PageLook.OfBags(row))) ?? tip;
        ImGui.TableNextColumn();
        tip = (PageLook.Seals(row) is { } seals ? CellBar(seals, row.SealsText, Palette.Accent) : CellText(row.SealsText, Palette.TextSecondary)) ?? tip;
        return tip;
    }

    /// <summary>Headers are in the section-label style, with sort arrows: ImGui's own header row, so a header's right-click menu and a
    /// column's resize are as before, its words in the small Axis font and the dim colour on the card's own surface; then the arrow of the
    /// sort's direction after the current sort column's words, in the accent. The header row is still the table's current row, so its cell is
    /// reached again by its index (the tick column first, then MatrixColumn's order).</summary>
    private void DrawHeaders()
    {
        float line;
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextDim).Push(ImGuiCol.TableHeaderBg, Vector4.Zero))
        using (_plugin.Fonts.Label.Push())
        {
            ImGui.TableHeadersRow();
            line = ImGui.GetTextLineHeight();
        }
        ImGui.TableSetColumnIndex(1 + (int)_sortColumn);
        var at = ImGui.GetCursorScreenPos();
        float words;
        using (_plugin.Fonts.Label.Push()) words = ImGui.CalcTextSize(HeaderLabels[_sortColumn]).X;
        var glyph = _sortDescending ? Glyphs.Down : Glyphs.Up;
        var size = Blocks.GlyphSize(glyph);
        var x = MathF.Min(at.X + words + Blocks.S(4f), at.X + ImGui.GetContentRegionAvail().X - size.X);
        Blocks.GlyphAt(ImGui.GetWindowDrawList(), new Vector2(x, at.Y + (line - size.Y) * 0.5f), glyph, Palette.Accent);
    }

    /// <summary>A cell's words in their colour, level with the row's framed items, cut with "…" to the column. Returns the whole words while
    /// the cut words are under the mouse, for the row's tooltip.</summary>
    private static string? CellText(string text, Vector4 colour)
    {
        if (text.Length == 0) return null;
        var (shown, cut) = Blocks.Fit(text, ImGui.GetContentRegionAvail().X);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(colour, shown);
        return cut && ImGui.IsItemHovered() ? text : null;
    }

    /// <summary>A cell's pill, cut to the column; its whole words while it is cut and under the mouse.</summary>
    private static string? CellPill(string text, Vector4 tone)
    {
        var (shown, cut) = Blocks.Fit(text, MathF.Max(Blocks.S(12f), ImGui.GetContentRegionAvail().X - Blocks.S(Blocks.PillPadding) * 2f));
        Blocks.Pill(shown, tone);
        return cut && ImGui.IsItemHovered() ? text : null;
    }

    /// <summary>A cell's small bar with its words inside it, as wide as the column up to CellBarWidth, centred on the row; its words while
    /// the bar is narrower than they are and under the mouse.</summary>
    private static string? CellBar(Meter meter, string words, Vector4 tone)
    {
        var width = MathF.Max(Blocks.S(24f), MathF.Min(ImGui.GetContentRegionAvail().X, Blocks.S(CellBarWidth)));
        var height = ImGui.GetTextLineHeight() + Blocks.S(2f);
        var rowHeight = ImGui.GetFrameHeight();
        var at = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(width, rowHeight));
        Blocks.Bar(ImGui.GetWindowDrawList(), new Vector2(at.X, at.Y + (rowHeight - height) * 0.5f), new Vector2(width, height), meter.Fraction, tone, words);
        return ImGui.CalcTextSize(words).X > width && ImGui.IsItemHovered() ? words : null;
    }

    /// <summary>Ventures ready reads "2 of 5" with a dot: the dot, centred on the row, then the words.</summary>
    private static string? CellDot(string words, Vector4 colour)
    {
        var at = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(at + new Vector2(Blocks.S(14f), 0f));
        var tip = CellText(words, Palette.Text);
        Blocks.DotAt(ImGui.GetWindowDrawList(), new Vector2(at.X + Blocks.S(5f), at.Y + ImGui.GetFrameHeight() * 0.5f), colour);
        return tip;
    }

    /// <summary>Gil is compact, with the retainers' share in secondary text (PageLook.Gil), cut with "…" to the column like any cell: the
    /// retainers' share gives way first, then the character's own is cut; the whole words (MatrixRow.GilText) while the cell is cut and under
    /// the mouse.</summary>
    private static string? CellGil(MatrixRow row)
    {
        var (own, retainers) = PageLook.Gil(row);
        if (own.Length == 0) return null;
        var at = ImGui.GetCursorScreenPos();
        var room = ImGui.GetContentRegionAvail().X;
        var gap = Blocks.S(4f);
        var ownWidth = ImGui.CalcTextSize(own).X;
        ImGui.AlignTextToFramePadding();
        var (ownShown, cut) = Blocks.Fit(own, room);
        ImGui.TextColored(Palette.Text, ownShown);
        if (retainers.Length > 0 && !cut)
        {
            if (room - ownWidth - gap < ImGui.CalcTextSize(Blocks.Ellipsis).X) cut = true;
            else
            {
                (var shown, var retainersCut) = Blocks.Fit(retainers, room - ownWidth - gap);
                ImGui.SameLine(0f, gap);
                ImGui.TextColored(Palette.TextSecondary, shown);
                cut |= retainersCut;
            }
        }
        // IsMouseHoveringRect alone does not check occlusion, so this tooltip could pop up over an open popup elsewhere in
        // the window; IsWindowHovered is required too.
        return cut && ImGui.IsMouseHoveringRect(at, at + new Vector2(room, ImGui.GetFrameHeight())) && ImGui.IsWindowHovered() ? row.GilText : null;
    }

    private static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);

    /// <summary>Gives every ticked character one role, or none, through Tracker.SetRoles, then clears the ticks. The info bar follows at once
    /// (RefreshSoon), as the sidebar's Role menu does.</summary>
    private void DrawGiveRole()
    {
        using (ImRaii.Disabled(_ticked.Count == 0))
        {
            if (ImGui.Button(_ticked.Count == 0 ? "Give role..." : $"Give role to {_ticked.Count}...")) ImGui.OpenPopup("give-role");
        }
        using var popup = ImRaii.Popup("give-role");
        if (!popup) return;
        foreach (var role in _plugin.Settings.Roles.Usable)
        {
            using var id = ImRaii.PushId(role.Id);
            if (ImGui.Selectable(role.Name)) Give(role.Id);
        }
        if (ImGui.Selectable(RoleList.NoneName)) Give(null);

        void Give(string? roleId)
        {
            _plugin.Tracker.SetRoles(_ticked.ToList(), roleId);
            _plugin.Attention.RefreshSoon();
            _ticked.Clear();
        }
    }
}
