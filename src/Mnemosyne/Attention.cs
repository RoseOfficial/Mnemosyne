using System.Globalization;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Time;

namespace Mnemosyne;

/// <summary>One chat line after login, a passive server info bar entry, and five opt-in reminders. Nothing here speaks
/// while a must-stay-silent flag is on; the info bar is the one exemption, because it is passive. The decisions about
/// what is worth saying live in Core's Reminders.</summary>
public sealed class Attention : IDisposable
{
    /// <summary>The set tested as one call: the three bound-by-duty flags, deep dungeon, duty recorder playback, the three
    /// cutscene flags, the two between-areas flags, logging out, occupied in event, occupied in quest event, and in
    /// combat. All fourteen are present in this build under these names.</summary>
    private static readonly ConditionFlag[] MustStaySilent =
    [
        ConditionFlag.BoundByDuty, ConditionFlag.BoundByDuty56, ConditionFlag.BoundByDuty95,
        ConditionFlag.InDeepDungeon, ConditionFlag.DutyRecorderPlayback,
        ConditionFlag.OccupiedInCutSceneEvent, ConditionFlag.WatchingCutscene, ConditionFlag.WatchingCutscene78,
        ConditionFlag.BetweenAreas, ConditionFlag.BetweenAreas51,
        ConditionFlag.LoggingOut, ConditionFlag.OccupiedInEvent, ConditionFlag.OccupiedInQuestEvent,
        ConditionFlag.InCombat,
    ];

    private const uint LinkCommandId = 1;
    private const string InfoBarTitle = "Mnemosyne";

    private readonly Plugin _plugin;
    private readonly IClock _clock;
    private readonly DalamudLinkPayload _link;
    private IDtrBarEntry? _entry;
    private bool _infoBarUnavailable;
    private bool _loginOwed;
    private bool _refreshSoon;

    public Attention(Plugin plugin, IClock clock)
    {
        _plugin = plugin;
        _clock = clock;
        _link = Plugin.ChatGui.AddChatLinkHandler(LinkCommandId, (_, _) => _plugin.MainWindow.IsOpen = true);
        Plugin.Condition.ConditionChange += OnConditionChange;
    }

    /// <summary>Whether anything in the set is on. The whole set is re-tested on every call rather than tracked flag by
    /// flag, because the event fires once per flag across all 112 of them and two can change on one tick.</summary>
    public bool Suppressed => Plugin.Condition.Any(MustStaySilent);

    /// <summary>A login owes at most one line. What is recorded here is only the fact that one is owed, never a composed
    /// line, so whichever character and badge are current when it actually goes out — at once, if nothing suppresses it,
    /// or later, from the change that clears a held flag — are the only ones it can ever print. Composing up front and
    /// holding the result would let a duty entered right after logging in, a logout, or a character switch print a name
    /// and a count that were true only at login, not when the line was actually said.</summary>
    public void Login(int badge, DateTimeOffset now)
    {
        if (!_plugin.Settings.LoginLine || badge <= 0) return;
        _loginOwed = true;
        if (!Suppressed) TrySendOwedLogin(now);
    }

    /// <summary>A login line still owed when the character logs out has no one left to speak of, so the debt is forgiven
    /// here instead of surviving to print for whichever character logs in next.</summary>
    public void Logout() => _loginOwed = false;

    /// <summary>One live interval. The cache's per-frame budget is otherwise spent by the window's own draw; claiming a
    /// budget here, before either read, keeps the reminder path — not just the info bar — from serving a frozen checklist
    /// while the window stays closed. The info bar is refreshed whatever the condition flags say; the reminders are
    /// not.</summary>
    public void Tick(DateTimeOffset now)
    {
        _plugin.Cache.BeginFrame(1);
        UpdateInfoBar(now);
        if (Suppressed) return;
        Remind(now);
    }

    /// <summary>A role change, an assignment in the sidebar or an edit on the settings page, reaches the info bar on the
    /// next frame rather than at the next live tick. The window's draw only raises this flag; Frame, on the next
    /// framework update, does the refresh, where the live tick does its own.</summary>
    public void RefreshSoon() => _refreshSoon = true;

    /// <summary>Every framework update, from Plugin.OnUpdate, before the live interval is counted: honours RefreshSoon once. A budget of one is
    /// claimed first, as Tick claims one, so the logged-in character's checklist is assembled again now rather than served from a slot the
    /// window's own budget left. The info bar is passive, so the must-stay-silent set does not hold it back, as it does not in Tick.</summary>
    public void Frame(DateTimeOffset now)
    {
        if (!_refreshSoon) return;
        _refreshSoon = false;
        _plugin.Cache.BeginFrame(1);
        UpdateInfoBar(now);
    }

    public void Dispose()
    {
        Plugin.Condition.ConditionChange -= OnConditionChange;
        Plugin.ChatGui.RemoveChatLinkHandler(LinkCommandId);
        _entry?.Remove();
        _entry = null;
    }

    /// <summary>A login line still owed is sent, or abandoned, on the change that clears the set, rather than up to one
    /// live interval later. Reminders need no such handling: one that is due is still due on the next tick.</summary>
    private void OnConditionChange(ConditionFlag flag, bool value)
    {
        // The plugin's own clock, never the wall clock: the instant becomes the minute key ChecklistFor caches on, and
        // every other path into this class already passes it.
        if (_loginOwed && !Suppressed) TrySendOwedLogin(_clock.UtcNow);
    }

    /// <summary>The only place a login line is composed, so it always names whoever is current and whatever their badge
    /// reads the instant it is actually said, never what was true when it first became owed. Clears
    /// the debt whether it sends or finds nothing left worth saying, so it can never be attempted twice.</summary>
    private void TrySendOwedLogin(DateTimeOffset now)
    {
        if (!_loginOwed) return;
        _loginOwed = false;
        if (!_plugin.Settings.LoginLine || _plugin.Tracker.Current is not { } record) return;
        var badge = _plugin.ChecklistFor(record, now).Badge;
        if (badge <= 0) return;
        Say(new SeStringBuilder()
            .AddText($"{record.Label}: {badge} thing{(badge == 1 ? "" : "s")} to do. ")
            .Add(_link)
            .AddText(Plugin.Command)
            .Add(RawPayload.LinkTerminator)
            .AddText(" to see them.")
            .Build());
    }

    private void Remind(DateTimeOffset now)
    {
        if (_plugin.Tracker.Current is not { } record) return;
        foreach (var reminder in Reminders.Due(_plugin.ChecklistFor(record, now), record, _plugin.Settings, now))
        {
            // Recorded before said: if Say ever threw, OnUpdate's catch would swallow it, and a
            // record-after-say order would then repeat the same reminder on every later tick.
            _plugin.Tracker.Remind(reminder.Key, reminder.Occasion);
            _plugin.Say(reminder.Text);
        }
    }

    private void UpdateInfoBar(DateTimeOffset now)
    {
        if (!_plugin.Settings.InfoBar || _infoBarUnavailable)
        {
            _entry?.Remove();
            _entry = null;
            return;
        }
        _entry ??= Create();
        if (_entry is not { } entry) return;
        if (_plugin.Tracker.Current is not { } record)
        {
            entry.Shown = false;
            return;
        }
        var badge = _plugin.ChecklistFor(record, now).Badge;
        entry.Text = badge.ToString(CultureInfo.InvariantCulture);
        entry.Tooltip = $"{record.Label}: {badge} thing{(badge == 1 ? "" : "s")} to do. Click to open Mnemosyne.";
        entry.Shown = true;
    }

    /// <summary>Get throws when another plugin already owns the title. Mnemosyne then runs without the entry rather
    /// than failing to load, and does not try again this session.</summary>
    private IDtrBarEntry? Create()
    {
        try
        {
            var entry = Plugin.DtrBar.Get(InfoBarTitle);
            entry.OnClick = _ => _plugin.MainWindow.IsOpen = true;   // Action<DtrInteractionEvent>; the event is not used
            return entry;
        }
        catch (ArgumentException ex)
        {
            Plugin.Log.Warning(ex, "the server info bar entry could not be created; running without it");
            _infoBarUnavailable = true;
            return null;
        }
    }

    private static void Say(SeString line) => Plugin.ChatGui.Print(line, "Mnemosyne");
}
