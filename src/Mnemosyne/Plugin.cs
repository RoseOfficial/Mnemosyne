using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Agent;
using Dalamud.Game.Agent.AgentArgTypes;
using Dalamud.Game.Chat;
using Dalamud.Game.DutyState;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Mnemosyne.Core.Chat;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Readers;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Storage;
using Mnemosyne.Core.Switch;
using Mnemosyne.Interop;
using Mnemosyne.Interop.Readers;
using Mnemosyne.Interop.Sheets;
using Mnemosyne.Look;
using Mnemosyne.Windows;

namespace Mnemosyne;

/// <summary>The plugin's entry point. The constructor builds everything first (the catalog from the game data, the readers, the tracker, the
/// windows), then subscribes to the game's events, then catches up a character already logged in; Dispose undoes it all in reverse and saves.
/// Mnemosyne reads the logged-in character's own progress from the client (quests, jobs, events, dailies and weeklies, content, collections
/// and money), keeps one record per character on this PC, and shows what is left to do: each character's checklist, All tasks, All
/// characters, Totals with gil over time, roles and My tasks, one chat line at login, the server info bar and opt-in reminders. It writes
/// nothing to the game and has no network code; the one thing it hands another plugin is the optional switch button's command to Lifestream
/// (RelogService).</summary>
public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IUnlockState UnlockState { get; private set; } = null!;
    [PluginService] internal static IDutyState DutyState { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IAgentLifecycle AgentLifecycle { get; private set; } = null!;
    /// <summary>The game's own icons, from the game data, loaded lazily and cached by Dalamud.</summary>
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;

    public const string Command = ChatCommand.Name;
    public const string PrivacySentence = "Mnemosyne stores character data only on this PC. It is never uploaded anywhere.";
    /// <summary>Follows the privacy sentence in the first-run notice and the manifest's description.</summary>
    public const string ReadOnlySentence = "It reads the game and never writes to it; its optional switch button asks Lifestream to change character.";

    /// <summary>The catalog every checklist reads: the sheet-built one plus the My tasks part, rebuilt when the settings version moves. The
    /// readers and the cactpot observer hold the sheet-built one alone.</summary>
    public Catalog Catalog => _catalogs.For(Settings, _settings.Version);
    public CatalogRows Rows { get; }
    public RecordStore Store { get; }
    public Settings Settings => _settings.Settings;
    public Tracker Tracker { get; }
    public ChecklistCache Cache { get; } = new();
    public Attention Attention { get; }
    public TickStats Ticks { get; } = new();
    /// <summary>The cost of `FireWindow`, next to the live tick's: it runs on its own trigger (an addon or agent Post event), not on the tick,
    /// so it is named separately from the live tick's own cost.</summary>
    public TickStats WindowTicks { get; } = new();
    public string GameVersion { get; }
    public string PluginVersion { get; } = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
    public MainWindow MainWindow { get; }
    public SettingsWindow SettingsWindow { get; }
    /// <summary>The switch over Lifestream.</summary>
    public IRelogService Relog => _relog;
    public Transfer Transfer { get; }
    /// <summary>The Axis font for a character's name, built once and freed at unload.</summary>
    internal LookFonts Fonts { get; }
    /// <summary>The game's icons beside the job and currency rows.</summary>
    internal GameIcons Icons { get; }
    /// <summary>One character's checklist, assembled only when that record, the settings or the minute moved.</summary>
    public Checklist ChecklistFor(CharacterRecord record, DateTimeOffset now) =>
        Cache.Get(Catalog, record, Settings, Tracker.VersionOf(record.ContentId), _settings.Version, now);

    public MatrixRow RowFor(CharacterRecord record, DateTimeOffset now) =>
        Cache.GetRow(Catalog, record, Settings, Tracker.VersionOf(record.ContentId), _settings.Version, now);

    /// <summary>The sidebar's characters, in its order: every record not hidden, the most to do first, then the most recently seen. The All
    /// tasks board lists each task's characters in this order, and the dump prints the same board.</summary>
    public List<(CharacterRecord Record, Checklist Checklist)> Characters(DateTimeOffset now) =>
        Tracker.All.Where(r => !r.Hidden)
            .Select(r => (Record: r, Checklist: ChecklistFor(r, now)))
            .OrderByDescending(c => c.Checklist.Badge)
            .ThenByDescending(c => c.Record.LastSeen)
            .ToList();

    /// <summary>The board over the given characters, kept from the last frame while nothing that could change it has moved (BoardCacheKey:
    /// the characters' own Checklist references, in order; the tasks version; the UTC minute). Not pure: it reads _settings.TasksVersion and,
    /// on a passed snooze, calls UpdateTasks, which moves that same version and is folded into the key that is kept, so the very next frame
    /// does not rebuild again for a change this call already applied. The minute in the key covers a snooze passing without a separate check:
    /// Board.Build resolves every expired one within the minute it ends, and the key changes every minute regardless.</summary>
    public TaskBoard BoardFor(IReadOnlyList<(CharacterRecord Record, Checklist Checklist)> characters, DateTimeOffset now)
    {
        var key = BoardCacheKey.For(characters, _settings.TasksVersion, now);
        if (_board != null && key.Equals(_boardKey)) return _board;
        var board = Board.Build(characters, Settings.Tasks, now);
        if (board.Expired.Count > 0) UpdateTasks(tasks => { foreach (var id in board.Expired) tasks.Wake(id); });
        _board = board;
        _boardKey = BoardCacheKey.For(characters, _settings.TasksVersion, now);   // re-read: the Wake above may have just moved it
        return board;
    }

    /// <summary>The Totals lines, kept from the last frame while nothing that could move them has moved (TotalsCacheKey: the tracker's own
    /// Version, which every record change bumps; the settings Version; the UTC minute, which also covers a holder going stale). BoardFor
    /// keeps the board the same way.</summary>
    public IReadOnlyList<TotalLine> TotalsFor(IEnumerable<CharacterRecord> records, DateTimeOffset now)
    {
        var key = TotalsCacheKey.For(Tracker.Version, _settings.Version, now);
        if (_totals != null && key.Equals(_totalsKey)) return _totals;
        _totals = Totals.Build(records, Catalog, Settings, now);
        _totalsKey = key;
        return _totals;
    }

    /// <summary>The Gil over time graph for one range, kept from the last frame while nothing that could move it has moved (GilGraphCacheKey:
    /// the tracker's own Version, which every record change bumps, a hide and an import included; the range; the UTC day), as TotalsFor keeps
    /// the Totals lines. The settings are not in the key: holdings are information, and no setting changes a point.</summary>
    public GilGraph GilGraphFor(IEnumerable<CharacterRecord> records, GilRange range, DateTimeOffset now)
    {
        var key = GilGraphCacheKey.For(Tracker.Version, range, now);
        if (_gilGraph != null && key.Equals(_gilGraphKey)) return _gilGraph;
        _gilGraph = GilGraph.Build(records, range, now);
        _gilGraphKey = key;
        return _gilGraph;
    }

    private readonly SystemClock _clock = new();
    private readonly GameIdentity _identity;
    private readonly RelogService _relog;
    private readonly WindowSystem _windows = new("Mnemosyne");
    private readonly MnemosyneCommand _command;
    private readonly SettingsKeeper _settings;
    private readonly MyTaskCatalog _catalogs;
    private double _sinceTick;
    private int _loginAttempts;
    private CancellationTokenSource _capture = new();
    private readonly CactpotObserver? _observer;
    private bool _logMessageThreadReported;
    private bool _disposed;
    /// <summary>BoardFor's kept board and the key it was built at.</summary>
    private TaskBoard? _board;
    private BoardCacheKey? _boardKey;
    /// <summary>TotalsFor's kept lines and the key they were built at.</summary>
    private IReadOnlyList<TotalLine>? _totals;
    private TotalsCacheKey? _totalsKey;
    /// <summary>GilGraphFor's kept graph and the key it was built at.</summary>
    private GilGraph? _gilGraph;
    private GilGraphCacheKey? _gilGraphKey;
    /// <summary>The character the last switch that a handler took asked for, and when, kept for the pulse on its button alone.</summary>
    private (ulong ContentId, DateTimeOffset At)? _switchAsked;

    public Plugin()
    {
        GameVersion = Interop.GameVersion.Read(DataManager, Log);
        Store = new RecordStore(new PhysicalFileSystem(), PluginInterface.ConfigDirectory.FullName);
        // The message already holds the failure's own words with the folder taken out, so the exception, which repeats the full path, is not logged.
        _settings = new SettingsKeeper(Store, (_, message) => Log.Error("{Message}", message));
        // The hand file is loaded inside the try with everything else it feeds: a malformed one thrown from a field
        // initialiser would escape the constructor and the plugin would not load at all.
        IReadOnlyList<IProgressReader> readers = [];
        CatalogExtras? loadedExtras = null;
        var build = Stopwatch.StartNew();
        // The sheet-built catalog, which the readers and the observer read; Catalog adds the My tasks part to it.
        Catalog sheets;
        try
        {
            var extras = CatalogExtras.Load();
            loadedExtras = extras;
            Rows = new LuminaCatalogSource(DataManager, extras, GameVersion, _clock).Load();
            sheets = CatalogBuilder.Build(Rows, extras, message => Log.Warning("catalog: {Message}", message));
            readers =
            [
                new QuestReader(Category.Story, sheets, Rows, PlayerState, UnlockState, DataManager, _clock),
                new QuestReader(Category.Events, sheets, Rows, PlayerState, UnlockState, DataManager, _clock),
                new RewardReader(sheets, extras, PlayerState, UnlockState, DataManager, _clock, message => Log.Warning("reward: {Message}", message)),
                new JobsReader(sheets, Rows, extras, PlayerState, UnlockState, DataManager, _clock, message => Log.Warning("{Message}", message)),
                new ResetsReader(sheets, Rows, extras, PlayerState, UnlockState, DataManager, _clock, message => Log.Warning("resets: {Message}", message)),
                new WindowReader(GameWindow.GrandCompanySupply, sheets, Rows, extras, PlayerState, UnlockState, DataManager, _clock, message => Log.Debug("window: {Message}", message)),
                new WindowReader(GameWindow.ChallengeLog, sheets, Rows, extras, PlayerState, UnlockState, DataManager, _clock, message => Log.Debug("window: {Message}", message)),
                new WindowReader(GameWindow.FashionReport, sheets, Rows, extras, PlayerState, UnlockState, DataManager, _clock, message => Log.Debug("window: {Message}", message)),
                new WindowReader(GameWindow.DomanEnclave, sheets, Rows, extras, PlayerState, UnlockState, DataManager, _clock, message => Log.Debug("window: {Message}", message)),
                new WindowReader(GameWindow.MaskedCarnivale, sheets, Rows, extras, PlayerState, UnlockState, DataManager, _clock, message => Log.Debug("window: {Message}", message)),
                new WindowReader(GameWindow.DutyFinder, sheets, Rows, extras, PlayerState, UnlockState, DataManager, _clock, message => Log.Debug("window: {Message}", message)),
                new TroveReader(sheets, Rows, PlayerState, _clock),
                new ContentReader(ContentFamily.Duties, sheets, Rows, extras, PlayerState, UnlockState, DataManager, _clock, message => Log.Debug("content: {Message}", message)),
                new ContentReader(ContentFamily.HuntingLog, sheets, Rows, extras, PlayerState, UnlockState, DataManager, _clock, message => Log.Debug("content: {Message}", message)),
                new ContentReader(ContentFamily.Housing, sheets, Rows, extras, PlayerState, UnlockState, DataManager, _clock, message => Log.Debug("content: {Message}", message)),
                new ContentReader(ContentFamily.GoldSaucer, sheets, Rows, extras, PlayerState, UnlockState, DataManager, _clock, message => Log.Debug("content: {Message}", message)),
                new ContentReader(ContentFamily.Pvp, sheets, Rows, extras, PlayerState, UnlockState, DataManager, _clock, message => Log.Debug("content: {Message}", message)),
                new ContentReader(ContentFamily.HouseVisit, sheets, Rows, extras, PlayerState, UnlockState, DataManager, _clock, message => Log.Debug("content: {Message}", message)),
                new ContentReader(ContentFamily.Racing, sheets, Rows, extras, PlayerState, UnlockState, DataManager, _clock, message => Log.Debug("content: {Message}", message)),
                new PlaceReader(PlaceFamily.Eureka, sheets, Rows, extras, PlayerState, UnlockState, ClientState, DataManager, message => Log.Debug("place: {Message}", message)),
                new PlaceReader(PlaceFamily.Bozja, sheets, Rows, extras, PlayerState, UnlockState, ClientState, DataManager, message => Log.Debug("place: {Message}", message)),
                new PlaceReader(PlaceFamily.OccultCrescent, sheets, Rows, extras, PlayerState, UnlockState, ClientState, DataManager, message => Log.Debug("place: {Message}", message)),
                new PlaceReader(PlaceFamily.CosmicExploration, sheets, Rows, extras, PlayerState, UnlockState, ClientState, DataManager, message => Log.Debug("place: {Message}", message)),
                new PlaceReader(PlaceFamily.IslandSanctuary, sheets, Rows, extras, PlayerState, UnlockState, ClientState, DataManager, message => Log.Debug("place: {Message}", message)),
                new PlaceReader(PlaceFamily.DeepDungeons, sheets, Rows, extras, PlayerState, UnlockState, ClientState, DataManager, message => Log.Debug("place: {Message}", message)),
                new CollectionsReader(CollectionFamily.Unlocks, sheets, Rows, extras, PlayerState, UnlockState, DataManager, _clock, message => Log.Debug("collections: {Message}", message)),
                new CollectionsReader(CollectionFamily.Logs, sheets, Rows, extras, PlayerState, UnlockState, DataManager, _clock, message => Log.Debug("collections: {Message}", message)),
                new AchievementReader(sheets, Rows, PlayerState, UnlockState, DataManager, _clock, message => Log.Debug("collections: {Message}", message)),
                new MoneyReader(MoneyFamily.Currencies, sheets, Rows, extras, PlayerState, DataManager, _clock, message => Log.Debug("money: {Message}", message)),
                new MoneyReader(MoneyFamily.Retainers, sheets, Rows, extras, PlayerState, DataManager, _clock, message => Log.Debug("money: {Message}", message)),
                new MoneyReader(MoneyFamily.Bags, sheets, Rows, extras, PlayerState, DataManager, _clock, message => Log.Debug("money: {Message}", message)),
                new StorageReader(StorageFamily.Saddlebag, sheets, Rows, extras, PlayerState, UnlockState, DataManager, _clock, message => Log.Debug("money: {Message}", message)),
                new StorageReader(StorageFamily.Dresser, sheets, Rows, extras, PlayerState, UnlockState, DataManager, _clock, message => Log.Debug("money: {Message}", message)),
                new StorageReader(StorageFamily.Armoire, sheets, Rows, extras, PlayerState, UnlockState, DataManager, _clock, message => Log.Debug("money: {Message}", message)),
            ];
            Log.Debug("catalog built: {Items} items from {Quests} quests on game {Game} in {Ms} ms",
                sheets.Items.Count, Rows.Quests.Count, Rows.GameVersion, build.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            // No catalog means nothing to read, so no reader is registered.
            Log.Error(ex, "the catalog could not be built; running with an empty catalog");
            Rows = new CatalogRows(GameVersion, _clock.UtcNow.ToString("u"), [], [], [], [], [], 0, [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], []);
            sheets = Catalog.Empty;
            Store.Report("catalog: could not be built from the game data (" + ex.Message + "); see the log");
        }
        _catalogs = new MyTaskCatalog(sheets);
        Tracker = new Tracker(_clock, Store, readers, GameVersion, message => Log.Debug("{Message}", message), message => Log.Error("{Message}", message));
        // No catalog means no cactpot items to observe, so no observer (the same reason no reader is registered above).
        _observer = loadedExtras is { } observed ? new CactpotObserver(sheets, observed, Tracker, message => Log.Debug("observe: {Message}", message)) : null;
        foreach (var problem in Store.Problems) Log.Warning("storage: {Problem}", problem);
        _identity = new GameIdentity(PlayerState);

        // Before the windows, which read it from their first draw.
        _relog = new RelogService(PluginInterface, CommandManager);
        // Before the windows too, which draw with them from their first frame.
        Fonts = new LookFonts(PluginInterface.UiBuilder);
        Icons = new GameIcons(TextureProvider);
        MainWindow = new MainWindow(this) { IsOpen = Settings.MainWindowOpen };
        Transfer = new Transfer(this, new PhysicalFileSystem(), _clock);
        SettingsWindow = new SettingsWindow(this) { IsOpen = !Settings.FirstRunNoticeDismissed };
        _windows.AddWindow(MainWindow);
        _windows.AddWindow(SettingsWindow);
        Attention = new Attention(this, _clock);
        PluginInterface.UiBuilder.Draw += _windows.Draw;
        PluginInterface.UiBuilder.Draw += Transfer.DrawDialogs;
        PluginInterface.UiBuilder.OpenMainUi += MainWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += SettingsWindow.Toggle;
        _command = new MnemosyneCommand(CommandManager, this);

        ClientState.Login += OnLogin;
        ClientState.Logout += OnLogout;
        Framework.Update += OnUpdate;
        UnlockState.Unlock += OnUnlock;
        ClientState.LevelChanged += OnLevelChanged;
        ClientState.ClassJobChanged += OnClassJobChanged;
        ClientState.TerritoryChanged += OnTerritoryChanged;
        DutyState.DutyCompleted += OnDutyCompleted;
        // Post events only, on the framework thread, reading and assigning nothing. A window's Post
        // event fires the Window trigger, so the edge-gated rows are read on this frame rather than on the next tick.
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, WindowAddons, OnWindow);
        AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, WindowAddons, OnWindow);
        AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, WindowAddons, OnWindow);
        AgentLifecycle.RegisterListener(AgentEvent.PostShow, WindowAgents, OnAgent);
        if (_observer != null)
        {
            AddonLifecycle.RegisterListener(AddonEvent.PostSetup, MiniCactpotAddon, _observer.OnMiniCactpot);
            ChatGui.LogMessage += OnLogMessage;
        }
        // Login does not fire for a plugin loaded into a session already logged in, so it is caught up here. With LoadSync false Dalamud
        // constructs the plugin off the framework thread, so the catch-up is marshalled onto it rather than run here, where the Login
        // readers would read the client off that thread and race the tick.
        if (ClientState.IsLoggedIn) _ = Framework.RunOnFrameworkThread(CatchUpLogin);

        Log.Information("Mnemosyne {Version} loaded on game {Game}; {Count} character record(s)", PluginVersion, GameVersion, Tracker.All.Count);
    }

    private void OnLogin()
    {
        _capture.Cancel();
        _capture.Dispose();
        _capture = new CancellationTokenSource();
        _loginAttempts = 0;
        TryCapture();
    }

    /// <summary>The mid-session catch-up: on the framework thread, inline when the constructor already runs there, otherwise on the next
    /// tick, so it carries its own try. It does nothing once the plugin is disposed, the character has logged out, or a login event that
    /// fired after the constructor subscribed has already captured a character. A late login event after this capture can still capture
    /// again; that window is narrow and known and accepted. Its log line says which thread it ran on.</summary>
    private void CatchUpLogin()
    {
        if (_disposed || !ClientState.IsLoggedIn || Tracker.Current != null) return;
        try
        {
            Log.Debug("mid-session login: caught up; on the framework thread: {OnThread}", Framework.IsInFrameworkUpdateThread);
            OnLogin();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "the mid-session login could not be caught up");
        }
    }

    /// <summary>The login event proves the player object exists, not that the player state is loaded. Confirm, else retry next tick. A
    /// storage failure is logged here so it can never escape the constructor or an event handler.</summary>
    private void TryCapture()
    {
        if (_disposed || _capture.IsCancellationRequested || !ClientState.IsLoggedIn) return;
        if (!PlayerState.IsLoaded)
        {
            if (++_loginAttempts > 600) { Log.Warning("player state never loaded within 600 ticks of login"); return; }
            _ = Framework.RunOnTick(TryCapture, delayTicks: 1, cancellationToken: _capture.Token);
            return;
        }
        Log.Debug("login captured after {Ticks} tick(s) of waiting for the player state", _loginAttempts);
        try
        {
            Tracker.Login(_identity, Settings.RoleForNewCharacters);   // a character seen for the first time gets the default role
        }
        catch (Exception ex)
        {
            Log.Error(ex, "the character record could not be saved; check the plugin configuration folder");
        }
        // Its own try, separate from the record save above, so a failure composing or printing the login line is never
        // reported under the record-save message and never aborts the capture.
        try
        {
            if (Tracker.Current is { } record) Attention.Login(ChecklistFor(record, _clock.UtcNow).Badge, _clock.UtcNow);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "the login line could not be sent");
        }
    }

    /// <summary>Log what the logout event leaves readable. Nothing is read into the record here.</summary>
    private void OnLogout(int type, int code)
    {
        Log.Debug("logout type={Type} code={Code}; player state loaded={Loaded}; content id={ContentId}",
            type, code, PlayerState.IsLoaded, PlayerState.IsLoaded ? PlayerState.ContentId : 0);
        Tracker.Logout();
        Attention.Logout();
    }

    private void OnUpdate(IFramework framework)
    {
        // A role change asked for the info bar on this frame (Attention.RefreshSoon). Its own try, so a
        // failure here never stops the live tick below.
        try
        {
            Attention.Frame(_clock.UtcNow);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "info bar refresh failed");
        }
        var interval = Math.Max(1, Settings.LiveReadSeconds);
        _sinceTick += framework.UpdateDelta.TotalSeconds;
        if (_sinceTick < interval) return;
        // Subtract rather than zero so the interval does not drift a frame at a time; a long stall — a loading screen, a
        // cutscene — is collapsed to one tick rather than a burst.
        _sinceTick = Math.Min(_sinceTick - interval, interval);
        var watch = Stopwatch.StartNew();
        try
        {
            Tracker.Tick();
            Attention.Tick(_clock.UtcNow);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "tick failed");
        }
        Ticks.Record(watch.Elapsed);
    }

    /// <summary>An unlock only ORs a dirty flag; the read happens on the next tick.</summary>
    private void OnUnlock(RowRef rowRef) => Tracker.MarkDirty(ReaderTrigger.Unlock);

    private void OnLevelChanged(uint classJobId, uint level) => Tracker.MarkDirty(ReaderTrigger.LevelOrJob);

    private void OnClassJobChanged(uint classJobId) => Tracker.MarkDirty(ReaderTrigger.LevelOrJob);

    private void OnTerritoryChanged(uint territory) => Tracker.MarkDirty(ReaderTrigger.Territory);

    /// <summary>A finished duty is when a roulette flips; the flag is ORed and the read happens on the next interval tick, not on this
    /// frame. Every reader of the Resets category also asks for Interval, so today the flag changes nothing; it is raised so that a reader
    /// which does not ask for Interval has this event to name. The delegate is Dalamud's own IDutyState.DutyCompletedDelegate(IDutyStateEventArgs
    /// args); the argument is not used.</summary>
    private void OnDutyCompleted(IDutyStateEventArgs args) => Tracker.MarkDirty(ReaderTrigger.DutyCompleted);

    /// <summary>The [Addon] names of the window rows that have a typed addon in this build. The challenge log (agent 136) and the Carnivale
    /// briefing (agent 333) have none and key on the agent id, and so does the PvP profile (agent 135), whose PostShow is the trigger that
    /// reads the series rows on the frame the window opens. The Gold Saucer information window's Chocobo tab loads the racing rank; its
    /// PostRefresh covers the tab change. The Achievements window (103) and the Character window (39) with its title list (209, 210) have
    /// their PostShow let the per-character guard see a list the window has just requested. The saddlebag (300), the glamour dresser (303)
    /// and the armoire with its withdraw window (92, 93) have their PostShow read the storage rows on the frame the window opens; the
    /// armoire's own data arrives after its PostShow, so the tick reads it while its window stays up.</summary>
    private static readonly string[] WindowAddons = ["GrandCompanySupplyList", "FashionCheck", "ReconstructionBox", "ContentsFinder", "GoldSaucerInfo"];
    private static readonly AgentId[] WindowAgents = [AgentId.ContentsNote, AgentId.AozContentBriefing, AgentId.PvpProfile, AgentId.Achievement, AgentId.Status, AgentId.CharacterTitle, AgentId.CharacterTitleSelect,
        AgentId.InventoryBuddy, AgentId.MiragePrismPrismBox, AgentId.Cabinet, AgentId.CabinetWithdraw];

    /// <summary>The Mini Cactpot addon's [Addon] name; PostSetup, never PreSetup, which is the event for modifying setup data.</summary>
    private const string MiniCactpotAddon = "LotteryDaily";

    /// <summary>The message object is valid only inside the callback, so it is read here; the record is touched on the framework thread
    /// only. That the chat hook already runs there is taken to be true; not yet confirmed, and the log says once if it did not. Wrapped like
    /// the other two lifecycle entry points, FireWindow and OnMiniCactpot, so nothing here escapes into Dalamud's chat pipeline; the
    /// RunOnFrameworkThread continuation runs later, off this call stack, so it carries its own try as well.</summary>
    private void OnLogMessage(ILogMessage message)
    {
        try
        {
            if (_observer == null || !_observer.TryReadJumboPurchase(message, out var observation)) return;
            if (Framework.IsInFrameworkUpdateThread)
            {
                _observer.ObserveJumbo(observation);
                return;
            }
            if (!_logMessageThreadReported)
            {
                _logMessageThreadReported = true;
                Log.Debug("a chat log message arrived off the framework thread; the observation is marshalled");
            }
            _ = Framework.RunOnFrameworkThread(() =>
            {
                // This runs later, off this call stack, on the framework thread; the outer catch cannot reach it.
                try
                {
                    _observer.ObserveJumbo(observation);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "chat log observation failed");
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "chat log observation failed");
        }
    }

    private void OnWindow(AddonEvent type, AddonArgs args) => FireWindow();

    private void OnAgent(AgentEvent type, AgentArgs args) => FireWindow();

    /// <summary>A window row that is an edge (the supply list, the Carnivale briefing, the Duty Finder) is readable only while its agent is
    /// active, and the next interval tick may fall after the window closed, so the Window readers run now rather than on a dirty mark. The
    /// lifecycle wrappers invoke listeners from the game's own call on the framework thread. Run has its own try per reader; this catch is
    /// for anything outside it. Timed into WindowTicks, next to the live tick's TickStats, so the settings page can show how often this
    /// fires and at what cost.</summary>
    private void FireWindow()
    {
        var watch = Stopwatch.StartNew();
        try
        {
            Tracker.Fire(ReaderTrigger.Window);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "window read failed");
        }
        WindowTicks.Record(watch.Elapsed);
    }

    /// <summary>Update is every edit but an All tasks adjustment: saved, and the version the checklist cache keys on moves. Every such edit
    /// also asks the info bar to follow on the next frame (Attention.RefreshSoon), so a settings edit that can change the logged-in
    /// character's badge — a category toggle, "Ignore everywhere", a role's tick tree and the rest — never waits for the next live
    /// tick.</summary>
    public void UpdateSettings(Action<Settings> change) { _settings.Update(change); Attention.RefreshSoon(); }

    /// <summary>A task adjustment, saved with the settings. No checklist reads one, so the version the checklist cache keys on stays where
    /// it is, and pinning a task re-assembles nothing.</summary>
    public void UpdateTasks(Action<TaskPreferences> change) => _settings.UpdateTasks(change);

    /// <summary>An import that replaces the settings. The planner has already kept this PC's two machine-local fields; the new object is
    /// saved as any edit is, and the version moves, so every cached checklist is assembled again. It also asks the info bar to follow on the
    /// next frame, so an import that changes or wipes the roles reaches it at once too.</summary>
    public void ReplaceSettings(Settings settings) { _settings.Replace(settings); Attention.RefreshSoon(); }

    /// <summary>The box of a My tasks row, in a character's own list or beside it under an All tasks line. Done stores the task's tick for
    /// that character, and not done removes it, through the Tracker, which moves that record's version alone and saves it. A counted task
    /// counts instead: a click on its unticked box adds one, and a click on its ticked box, or Take one back, takes one back. Any character,
    /// logged in or not: it is the player's own bookkeeping, and nothing is sent to the game. A tick can change the logged-in character's
    /// badge and no settings edit carries it, so it asks the info bar to follow on the next frame itself, as the Role menu does.</summary>
    public void SetTaskDone(CharacterRecord record, string taskId, bool done)
    {
        var task = Settings.MyTasks.Find(taskId);
        if (task is { IsCounted: true }) Tracker.CountTask(record.ContentId, task, done ? 1 : -1);
        else if (!done) Tracker.UntickTask(record.ContentId, taskId);
        else if (task != null) Tracker.TickTask(record.ContentId, task);
        Attention.RefreshSoon();
    }

    /// <summary>"Reset to 0" on a counted task's row. The stored count goes, so the task reads "0 of N" again.</summary>
    public void ResetTask(CharacterRecord record, string taskId)
    {
        Tracker.UntickTask(record.ContentId, taskId);
        Attention.RefreshSoon();
    }

    public void Say(string text) => ChatGui.Print(text, "Mnemosyne");

    /// <summary>The switch button's click. The sidebar enables the button only in the Ready state. The relog service is the one caller of
    /// ProcessCommand, and one chat line says it asked, and nothing more.</summary>
    public void SwitchTo(CharacterRecord record)
    {
        var taken = Relog.SwitchTo(record);
        if (taken) _switchAsked = (record.ContentId, _clock.UtcNow);
        Say(SwitchRules.Asked(record, taken));
        Log.Debug("switch: asked Lifestream for a character switch; a handler took it: {Taken}", taken);
    }

    /// <summary>Whether the switch the last click asked for is to this character and still in progress (SwitchRules.InProgress), so its
    /// button pulses. It changes nothing but that pulse.</summary>
    public bool SwitchInProgress(ulong contentId, DateTimeOffset now) =>
        _switchAsked is { } asked && SwitchRules.InProgress(asked.ContentId, asked.At, Tracker.Current?.ContentId, contentId, now);

    /// <summary>The current record, its role, its checklist as text, the All tasks board, the totals, the gil history, the disabled
    /// readers, storage problems, and the tick and window-read costs.</summary>
    public void Dump()
    {
        var record = Tracker.Current;
        if (record == null) { Say("not logged in; nothing to dump"); return; }
        try
        {
            var now = _clock.UtcNow;
            var sb = new StringBuilder();
            sb.Append("Mnemosyne ").Append(PluginVersion).Append(" on game ").Append(GameVersion).Append(" at ").Append(now.ToString("u")).Append('\n');
            sb.Append($"tick cost: last {Ticks.LastUs} us, max {Ticks.MaxUs} us, average {Ticks.AverageUs:0} us over {Ticks.Count} ticks\n");
            sb.Append($"window-read cost: last {WindowTicks.LastUs} us, max {WindowTicks.MaxUs} us, average {WindowTicks.AverageUs:0} us over {WindowTicks.Count} fires\n");
            foreach (var reader in Tracker.Disabled) sb.Append("disabled reader: ").Append(reader.Name).Append(" (").Append(reader.Category).Append("): ").Append(reader.Reason).Append('\n');
            foreach (var problem in Store.Problems) sb.Append("storage problem: ").Append(problem).Append('\n');
            // The character's role by name, so the dump says whose switches its off rows follow.
            sb.Append("role: ").Append(Settings.RoleOf(record)?.Name ?? (string.IsNullOrEmpty(record.Role) ? "none" : $"unknown ({record.Role})")).Append('\n');
            sb.Append('\n').Append(ChecklistText.Render(Assembler.Assemble(Catalog, record, Settings, now), record, now));
            // The All tasks board as the window builds it. A full cache budget is claimed first, as Attention.Tick claims one,
            // so no character in it is a checklist a closed window left frozen; a passed snooze is cleared here, as on any build of the list.
            Cache.BeginFrame(int.MaxValue);
            var characters = Characters(now);
            sb.Append("\n--- all tasks ---\n").Append(BoardText.Render(BoardFor(characters, now), now));
            // The Totals page as the window shows it, from the same kept lines, so the file can be read against the window.
            sb.Append("\n--- totals ---\n").Append(TotalsText.Render(TotalsFor(characters.Select(c => c.Record), now), now));
            // All gil, then each character in the sidebar's order, today, 7 and 30 days ago and a year ago.
            sb.Append("\n--- gil history ---\n").Append(GilHistoryText.Render(characters.Select(c => c.Record).ToList(), now));
            sb.Append("\n--- record ---\n").Append(JsonSerializer.Serialize(record, Json.Options)).Append('\n');
            var name = Dumps.FileName(record.ContentId, now);
            var path = Path.Combine(Store.Directory, name);
            File.WriteAllText(path, sb.ToString());
            Log.Debug("dump written to {Path}", path);
            Say(Dumps.Written(name));
            RemoveOlderDumps();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Error(ex, "dump failed");
            Say("dump failed: " + FileNames.Hide(ex.Message, Store.Directory, "the configuration folder"));
        }
    }

    /// <summary>Keeps the newest few dumps (Dumps.Kept): each holds every visible character's name and content id, so old ones should not pile
    /// up. A file that cannot be removed is left where it is, and the log says so.</summary>
    private void RemoveOlderDumps()
    {
        try
        {
            var names = Directory.EnumerateFiles(Store.Directory, "dump-*.txt").Select(Path.GetFileName).OfType<string>();
            foreach (var name in Dumps.Older(names)) File.Delete(Path.Combine(Store.Directory, name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"older dumps could not be removed: {FileNames.Hide(ex.Message, Store.Directory, "the configuration folder")}");
        }
    }

    /// <summary>The reduced rows the catalog was built from, for regenerating the test fixture after a patch.</summary>
    public void DumpSheets()
    {
        if (Rows.Quests.Count == 0)
        {
            const string message = "sheet dump skipped: the catalog was not built from the game data; see the log";
            Log.Warning(message);
            Say(message);
            return;
        }
        try
        {
            var dir = Path.Combine(Store.Directory, "sheets");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"sheets-{GameVersion}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(Rows, Json.Options));
            Log.Debug("sheet dump written to {Path}", path);
            Say($"sheet dump written: {Path.GetFileName(path)} (in the plugin configuration folder, sheets)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Error(ex, "sheet dump failed");
            Say("sheet dump failed: " + FileNames.Hide(ex.Message, Store.Directory, "the configuration folder"));
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _capture.Cancel();
        Framework.Update -= OnUpdate;
        ClientState.Login -= OnLogin;
        ClientState.Logout -= OnLogout;
        UnlockState.Unlock -= OnUnlock;
        ClientState.LevelChanged -= OnLevelChanged;
        ClientState.ClassJobChanged -= OnClassJobChanged;
        ClientState.TerritoryChanged -= OnTerritoryChanged;
        DutyState.DutyCompleted -= OnDutyCompleted;
        AddonLifecycle.UnregisterListener(OnWindow);
        AgentLifecycle.UnregisterListener(OnAgent);
        if (_observer != null)
        {
            AddonLifecycle.UnregisterListener(_observer.OnMiniCactpot);
            ChatGui.LogMessage -= OnLogMessage;
        }
        try
        {
            Tracker.Logout();
            Settings.MainWindowOpen = MainWindow.IsOpen;
            Store.SaveSettings(Settings);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "state could not be saved at unload");
        }
        finally
        {
            Attention.Dispose();
            _command.Dispose();
            _relog.Dispose();
            PluginInterface.UiBuilder.Draw -= _windows.Draw;
            PluginInterface.UiBuilder.Draw -= Transfer.DrawDialogs;
            PluginInterface.UiBuilder.OpenMainUi -= MainWindow.Toggle;
            PluginInterface.UiBuilder.OpenConfigUi -= SettingsWindow.Toggle;
            _windows.RemoveAllWindows();
            Fonts.Dispose();
            _capture.Dispose();
        }
    }
}
