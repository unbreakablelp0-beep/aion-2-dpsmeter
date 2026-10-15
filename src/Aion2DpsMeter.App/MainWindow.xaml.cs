using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Aion2DpsMeter.Capture.Npcap;
using Aion2DpsMeter.Core.Analysis;
using Aion2DpsMeter.Core.Capture;
using Aion2DpsMeter.Core.Combat;
using Aion2DpsMeter.Core.Events;
using Aion2DpsMeter.Core.Updates;

namespace Aion2DpsMeter.App;

/// <summary>
/// Hauptfenster mit der Netzwerkdiagnose. Die Logik steckt in <see cref="NetworkDiagnostics"/> (Core);
/// hier wird nur angezeigt und weitergeleitet.
/// </summary>
public partial class MainWindow : Window
{
    private readonly NetworkDiagnostics _diagnostics = new(new NpcapCaptureBackend());
    private readonly IProcessConnectionSource _connections = new WindowsProcessConnectionSource();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly LiveCombatMeter _liveMeter = new();
    private LiveCombatDecoder? _liveDecoder;
    private LiveMeterWindow? _liveWindow;
    private long _liveErrors;
    private readonly OverlaySettingsStore _overlayStore = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aion2DpsMeter", "overlay.json"));
    private OverlaySettings _overlaySettings = new();
    private readonly SkillNameStore _skillNames = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Aion2DpsMeter", "skills.json"));
    private readonly PartyStore _partyStore = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aion2DpsMeter", "gruppe.json"));
    private string? _savedPartyKey;

    /// <summary>Sobald die eigene Klasse erkannt ist, beim gemerkten Charakter mitspeichern (nur bei Änderung).</summary>
    private void SaveOwnClass()
    {
        if (_liveMeter.OwnActor is not { } id || _liveMeter.ClassPrefixOf(id) is not { } prefix || _liveMeter.NameOf(id) is not { } name) return;
        try
        {
            if (_ownStore.Load(DateTimeOffset.UtcNow) is { } saved && saved.Name == name && saved.ClassPrefix == prefix) return;
            _ownStore.Save(new RememberedOwnCharacter(id, name, DateTimeOffset.UtcNow, prefix));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Echte Gruppenliste dieser Sitzung lokal merken (nur bei Änderung, nur Namen erkannter Mitglieder).</summary>
    private void SaveParty()
    {
        if (_liveMeter.PartyIsRemembered || _liveMeter.PartySnapshot() is not { } party) return;
        var key = party.Size + "|" + string.Join("|", party.Names.Order(StringComparer.Ordinal));
        if (key == _savedPartyKey) return;
        try
        {
            if (_partyStore.Save(party.Size, party.Names, DateTimeOffset.UtcNow)) _savedPartyKey = key;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private readonly OwnCharacterStore _ownStore = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aion2DpsMeter", "eigener-charakter.json"));
    private OverlayWindow? _overlay;
    private bool _overlayControlsReady;
    private bool _overlayMode;
    private bool _exiting;
    private string? _overlayStatus;
    private HotkeyManager? _hotkeys;
    private OverlaySettingsWindow? _settingsWindow;
    private readonly DispatcherTimer _autoStartTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _historyTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly FightHistoryStore _fightStore = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Aion2DpsMeter", "Kaempfe"));
    private FightHistoryWindow? _historyWindow;
    private readonly GameEventScheduleStore _eventStore = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Aion2DpsMeter", "events.json"));
    private GameEventSchedule _schedule = GameEventSchedule.Default;
    private readonly EventAlarmClock _alarmClock = new();
    private readonly WorldBossStore _worldBossStore = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Aion2DpsMeter", "weltbosse.json"));
    private IReadOnlyList<WorldBoss> _worldBosses = WorldBossTimes.Default;
    private readonly HashSet<(string, DateTimeOffset)> _firedBosses = new();
    private readonly DispatcherTimer _alarmTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private string? _alarmText;
    private DateTimeOffset _alarmUntil;
    private string? _hintText;
    private DateTimeOffset _hintUntil;
    private TrayIcon? _tray;
    private readonly EnemyHpTracker _enemyHp = new();
    private NpcNames _npcNames = NpcNames.Empty;
    private readonly List<(string Name, DateTimeOffset At, bool Real)> _bossSightings = new();

    private static string RecordingDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Aion2DpsMeter", "Aufnahmen");

    private readonly UpdateService _updates = new();
    private readonly StatsShareService _share = new();

    /// <summary>Eigene Werte eines Bosskampfs an die Bestenliste – nur mit Zustimmung (Einstellung „Teilen“).</summary>
    private void ShareFight(FightRecord fight)
    {
        var mode = _overlaySettings.ShareMode;
        if (mode == ShareModes.Off || !StatsShareConfig.Configured) return;
        // Eigener GS/KS nur mit der Einwilligung ab Stand 2 (die ausdrücklich GS und KS nennt).
        var own = _liveMeter.OwnActor;
        var withPower = _overlaySettings.ShareConsent >= ShareModes.ConsentVersion && own is not null;
        var gs = withPower ? _liveMeter.GearScoreOf(own!.Value) : null;
        var ks = withPower ? _liveMeter.CombatPowerOf(own!.Value) : null;
        if (SharedFight.From(fight, AppRelease.Current, withName: mode == ShareModes.WithName, gs, ks) is { } shared) _ = _share.ShareAsync(shared);
    }
    private volatile OdEnergy? _od; // Od-Energie aus dem Spiel (null = noch nicht gesehen)
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromSeconds(10) }; // erst kurz nach dem Start, dann alle 30 min
    private UpdateInfo? _update;
    private string? _updateProgress;
    private bool _updating;

    public MainWindow()
    {
        InitializeComponent();
        SaveFolderText.Text = $"Speicherort: {RecordingDirectory}";
        _overlaySettings = _overlayStore.Load();
        _liveMeter.IdleTimeout = TimeSpan.FromSeconds(_overlaySettings.IdleSeconds);
        _historyTimer.Tick += (_, _) =>
        {
            SaveFinishedFights(_liveMeter.DrainFinishedFights(DateTimeOffset.UtcNow));
            SaveParty();
            SaveOwnClass();
        };
        _historyTimer.Start();

        // Event-Alarme: Zeitplan aus Dokumente\Aion2DpsMeter\events.json (fehlt → Standard + Vorlage).
        _schedule = _eventStore.Load();
        _worldBosses = _worldBossStore.Load();
        _alarmTimer.Tick += (_, _) => CheckEventAlarms();
        _alarmTimer.Start();

        // Update-Prüfung: Reste eines früheren Updates wegräumen, dann kurz nach dem Start und alle 30 Minuten bei GitHub fragen.
        if (UpdateService.InstallableExePath() is { } exe) UpdateInstaller.CleanUp(exe);
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = TimeSpan.FromMinutes(30);
            if (!_overlaySettings.CheckForUpdates || _updating) return;
            _update = await _updates.CheckAsync(CancellationToken.None);
        };
        _updateTimer.Start();
        // Patchnotes: beim ersten Start einer neuen Version einmal „Was ist neu“ zeigen.
        var seenStore = new SeenVersionStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Aion2DpsMeter", "version.json"));
        var lastSeen = seenStore.Load();
        var newNotes = PatchNotes.ToShow(PatchNotesWindow.All, lastSeen, AppRelease.Current);
        seenStore.Save(AppRelease.Current);
        if (newNotes.Count > 0) Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => ShowPatchNotes(newNotes, afterUpdate: lastSeen is not null));

        // Bestenliste: einmal fragen (nur wenn eine Bestenliste eingerichtet ist). Ohne Antwort wird nichts gesendet.
        // Neu fragen, wenn noch nie gefragt – oder wenn jemand zugestimmt hat, die Einwilligung aber älter ist (jetzt mit GS/KS).
        if (StatsShareConfig.Configured && (!_overlaySettings.ShareAsked ||
            (_overlaySettings.ShareMode != ShareModes.Off && _overlaySettings.ShareConsent < ShareModes.ConsentVersion)))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                var mode = ShareConsentDialog.Ask();
                ChangeOverlay(s => s with { ShareMode = mode, ShareAsked = true, ShareConsent = ShareModes.ConsentVersion });
            });
        }
        if (_overlayStore.State == OverlaySettingsFileState.Foreign)
        {
            DetailText.Text = $"Hinweis: {_overlayStore.Path} ist nicht lesbar oder stammt nicht von diesem Programm – Standardwerte, die Datei bleibt unverändert.";
        }

        if (_ownStore.Load(DateTimeOffset.UtcNow) is { } remembered) _liveMeter.Remember(remembered);
        // Zuletzt erkannte Gruppe (höchstens 2 h alt) – gilt, bis das Spiel eine neue Gruppenliste schickt.
        if (_partyStore.Load(DateTimeOffset.UtcNow) is { } party) _liveMeter.RememberParty(party.Size, party.Names);
        _skillNames.Load();
        // Optionale Community-Namensliste (nur lokal, nicht mitgeliefert, nur gelesen).
        _skillNames.Community = CommunitySkillNames.Load(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Aion2DpsMeter", "skills-community.json"));
        // Optionale Community-Gegnerliste für Boss-Namen (nur lokal, nicht mitgeliefert, nur gelesen).
        _npcNames = NpcNames.Load(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Aion2DpsMeter", "npcs-community.json"));
        if (_skillNames.IsForeign)
        {
            DetailText.Text = $"Hinweis: {_skillNames.Path} stammt nicht von diesem Programm – Fähigkeitsnamen werden nicht gespeichert, die Datei bleibt unverändert.";
        }

        ClickThroughCheck.IsChecked = _overlaySettings.ClickThrough;
        OpacitySlider.Value = _overlaySettings.BackgroundOpacity;
        _overlayControlsReady = true;
        _refreshTimer.Tick += (_, _) => ShowStatistics();
        _diagnostics.StatusChanged += (_, status) => Dispatcher.BeginInvoke(() => ShowStatus(status));
        ShowStatus(_diagnostics.Status);
        ShowStatistics();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Im Overlay-Modus ist schon alles geladen (und die Aufnahme läuft evtl.) – nicht neu initialisieren.
        if (_overlayMode)
        {
            ShowStatus(_diagnostics.Status);
            return;
        }

        Reload();
    }

    private void OnReload(object sender, RoutedEventArgs e) => Reload();

    private void Reload()
    {
        try
        {
            _diagnostics.Initialize();
        }
        catch (Exception ex)
        {
            // Z. B. wenn eine Bibliothek nicht geladen werden kann (Smart App Control).
            StatusText.Text = "Die Netzwerkdiagnose konnte nicht gestartet werden.";
            StatusText.Foreground = Brushes.Firebrick;
            DetailText.Text = ex.Message;
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            return;
        }

        var info = _diagnostics.BackendInfo;
        VersionText.Text = info?.Version is { } version ? $"Npcap: {version}" : string.Empty;

        AdapterBox.Items.Clear();
        foreach (var adapter in _diagnostics.Adapters)
        {
            AdapterBox.Items.Add(new ComboBoxItem { Content = CaptureDisplay.DescribeAdapter(adapter), Tag = adapter });
        }

        var suggested = CaptureDisplay.SuggestAdapter(_diagnostics.Adapters);
        AdapterBox.SelectedItem = AdapterBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, suggested));
        UpdateButtons();
    }

    private void OnLoadProcesses(object sender, RoutedEventArgs e)
    {
        if (LoadProcesses() is { } groups) DetailText.Text = $"{groups.Count} Programme mit Netzwerkverbindungen gefunden.";
    }

    private IReadOnlyList<ProcessConnectionGroup>? LoadProcesses()
    {
        IReadOnlyList<ProcessConnectionGroup> groups;
        try
        {
            groups = ProcessConnectionFilter.GroupByProcess(_connections.GetConnections(), ProcessName);
        }
        catch (Exception ex)
        {
            DetailText.Text = $"Programme konnten nicht gelesen werden: {ex.Message}";
            return null;
        }

        ProcessBox.Items.Clear();
        foreach (var group in groups)
        {
            ProcessBox.Items.Add(new ComboBoxItem { Content = group.DisplayName, Tag = group.ProcessId });
        }

        return groups;
    }

    /// <summary>
    /// Ein-Klick-Start für Endnutzer: Netzwerkkarte (Vorschlag) und AION 2 automatisch wählen, nur dessen Verbindungen
    /// live auswerten (nichts speichern) und das Overlay einblenden. Alles bleibt passiv.
    /// </summary>
    private void OnQuickStart(object sender, RoutedEventArgs e)
    {
        if (TryQuickStart() && _overlay is not { IsLoaded: true }) OnToggleOverlay(sender, e);
    }

    /// <summary>
    /// Startet die Aufnahme automatisch. false mit Meldung in <see cref="_overlayStatus"/> und im Fenster, wenn Npcap,
    /// Netzwerkkarte oder das Spiel fehlen.
    /// </summary>
    private bool TryQuickStart()
    {
        if (_diagnostics.Status.State == CaptureState.Capturing) return true;

        if (_diagnostics.BackendInfo is not { IsAvailable: true })
        {
            ShowProblem("Npcap fehlt oder ist nicht nutzbar.",
                "Bitte Npcap von https://npcap.com/#download installieren (Standardoptionen) und das Programm neu starten. " +
                "Npcap wird aus Lizenzgründen nicht mitgeliefert.",
                "Npcap fehlt – bitte von npcap.com installieren und neu starten");
            return false;
        }

        if (AdapterBox.SelectedItem is null && AdapterBox.Items.Count > 0) AdapterBox.SelectedIndex = 0;
        if (AdapterBox.SelectedItem is null)
        {
            ShowProblem("Keine Netzwerkkarte gefunden.", "Unter „Erweitert“ auf „Neu laden“ klicken.", "Keine Netzwerkkarte gefunden");
            return false;
        }

        if (LoadProcesses() is not { } groups) return false;
        if (ProcessConnectionFilter.FindGameProcess(groups) is not { } game)
        {
            ShowProblem("AION 2 wurde nicht gefunden.",
                "Bitte das Spiel starten (Charakterauswahl reicht) und erneut auf „Starten“ klicken. " +
                "Oder unter „Erweitert“ das Programm von Hand wählen.",
                "Warte auf AION 2 … (Spiel starten)");
            return false;
        }

        ProcessBox.SelectedItem = ProcessBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Tag is int pid && pid == game.ProcessId);
        ProcessFilterCheck.IsChecked = true;
        LiveCheck.IsChecked = true;
        SaveCheck.IsChecked = false;

        OnStart(this, new RoutedEventArgs());
        var started = _diagnostics.Status.State == CaptureState.Capturing;
        _overlayStatus = started ? null : $"Aufnahme nicht gestartet: {_diagnostics.Status.Message}";
        return started;
    }

    private void ShowProblem(string status, string detail, string overlayText)
    {
        StatusText.Text = status;
        StatusText.Foreground = Brushes.Firebrick;
        DetailText.Text = detail;
        _overlayStatus = overlayText;
    }

    // ---------- Overlay-Modus (Endnutzer: kein Hauptfenster) ----------

    /// <summary>
    /// Startet ohne sichtbares Hauptfenster: Overlay einblenden, Aufnahme automatisch starten und – solange das Spiel
    /// nicht läuft – alle 5 s erneut versuchen. Das Hauptfenster bleibt als „Erweitert“ erreichbar.
    /// </summary>
    public void StartOverlayMode()
    {
        _overlayMode = true;
        Reload();
        OnToggleOverlay(this, new RoutedEventArgs());

        // Tastenkürzel (nur für dieses Programm, siehe HotkeyManager) – vor allem der Weg zurück bei „Klicks durchlassen“.
        _hotkeys = new HotkeyManager();
        _hotkeys.Register(HotkeyManager.ModControl | HotkeyManager.ModShift, 0x4B /* K */, "Strg+Umschalt+K: Klicks durchlassen an/aus", ToggleClickThrough);
        _hotkeys.Register(HotkeyManager.ModControl | HotkeyManager.ModShift, 0x4F /* O */, "Strg+Umschalt+O: Overlay ein/aus", () => OnToggleOverlay(this, new RoutedEventArgs()));
        _hotkeys.Register(HotkeyManager.ModControl | HotkeyManager.ModShift, 0x52 /* R */, "Strg+Umschalt+R: Kampf zurücksetzen", () => _liveMeter.Reset());
        _hotkeys.Register(HotkeyManager.ModControl | HotkeyManager.ModShift, 0x45 /* E */, "Strg+Umschalt+E: Einstellungen", OpenOverlaySettings);

        // Notausgang unabhängig von Tastenkürzeln: Symbol im Infobereich.
        _tray = new TrayIcon(
            isClickThrough: () => _overlaySettings.ClickThrough,
            disableClickThrough: DisableClickThrough,
            toggleClickThrough: ToggleClickThrough,
            toggleOverlay: () => OnToggleOverlay(this, new RoutedEventArgs()),
            openSettings: OpenOverlaySettings,
            exit: ExitApp);

        if (!TryQuickStart())
        {
            _autoStartTimer.Tick += (_, _) =>
            {
                if (TryQuickStart()) _autoStartTimer.Stop();
            };
            _autoStartTimer.Start();
        }
    }

    private void ShowAdvancedWindow()
    {
        Topmost = true; // sonst hinter dem Spiel (das Overlay nimmt nie den Fokus)
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>
    /// Hinweis beim Einschalten von „Klicks durchlassen“: einige Sekunden im Overlay und als Sprechblase am Tray-Symbol,
    /// damit klar ist, wie man wieder herauskommt.
    /// </summary>
    private void ShowClickThroughHint()
    {
        var hotkeyWorks = _hotkeys?.Registered.Any(h => h.StartsWith("Strg+Umschalt+K", StringComparison.Ordinal)) == true;
        var way = hotkeyWorks
            ? "Strg+Umschalt+K zum Ausschalten (oder Tray-Symbol doppelklicken)"
            : "Tray-Symbol doppelklicken zum Ausschalten";
        _hintText = $"🔒 Klicks durchgelassen – {way}";
        _hintUntil = DateTimeOffset.Now.AddSeconds(8);
        _tray?.Notify("Klicks werden durchgelassen", way);
    }

    private void DisableClickThrough()
    {
        if (_overlaySettings.ClickThrough || ClickThroughCheck.IsChecked == true) ToggleClickThrough();
    }

    private void ToggleClickThrough()
    {
        ClickThroughCheck.IsChecked = ClickThroughCheck.IsChecked != true;
        ChangeOverlay(s => s with { ClickThrough = ClickThroughCheck.IsChecked == true });
    }

    private void OpenOverlaySettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new OverlaySettingsWindow(
            () => _overlay is { IsLoaded: true } o ? o.CurrentSettings : _overlaySettings,
            change =>
            {
                ChangeOverlay(change);
                ClickThroughCheck.IsChecked = _overlaySettings.ClickThrough;
            },
            reset: () => _liveMeter.Reset(),
            showTable: () => OnShowLive(this, new RoutedEventArgs()),
            showHistory: OpenFightHistory,
            schedule: () => _schedule,
            scheduleInfo: () => _eventStore.Problem is { } p ? $"{_eventStore.Path}: {p}" : _eventStore.Path,
            worldBosses: () => _worldBosses,
            setWorldBosses: bosses =>
            {
                _worldBosses = bosses;
                try
                {
                    return _worldBossStore.Save(bosses) ? null : $"{_worldBossStore.Path} stammt nicht von diesem Programm – nur bis zum Beenden gemerkt.";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return $"Nicht gespeichert: {ex.Message}";
                }
            },
            showAdvanced: ShowAdvancedWindow,
            exit: ExitApp,
            hotkeys: _hotkeys?.Registered ?? [],
            failedHotkeys: _hotkeys?.Failed ?? [],
            showPatchNotes: () => ShowPatchNotes(PatchNotesWindow.All.Take(5).ToList(), afterUpdate: false));
        _settingsWindow.Show();
    }

    /// <summary>Prüft jede Sekunde, ob eine Event-Erinnerung fällig ist: Ton + Einblendung im Overlay (20 s).</summary>
    private void CheckEventAlarms()
    {
        var now = DateTimeOffset.Now;

        // Weltbosse (selbst eingetragene Zeiten).
        foreach (var boss in WorldBossTimes.Due(_worldBosses, now, _firedBosses))
        {
            var at = boss.RespawnAt!.Value;
            var minutes = Math.Max(0, (int)Math.Round((at - now).TotalMinutes));
            _alarmText = $"⏰ Weltboss {boss.Name} {(minutes == 0 ? "jetzt" : $"in {minutes} Min.")} ({at.ToLocalTime():HH:mm})";
            _alarmUntil = now.AddSeconds(30);
            AlarmSound.Play(_overlaySettings.AlarmTone, _overlaySettings.AlarmVolume);
        }

        var alarms = _overlaySettings.EventAlarms;
        if (alarms is null || alarms.Count == 0) return;

        foreach (var (e, at) in _alarmClock.Due(now, _schedule, alarms))
        {
            var minutes = Math.Max(0, (int)Math.Round((at - now).TotalMinutes));
            _alarmText = $"⏰ {e.Name} {(minutes == 0 ? "jetzt" : $"in {minutes} Min.")} ({at.ToLocalTime():HH:mm})";
            _alarmUntil = now.AddSeconds(20);
            AlarmSound.Play(_overlaySettings.AlarmTone, _overlaySettings.AlarmVolume);
        }
    }

    /// <summary>Zeile im Overlay: laufender Alarm, sonst (falls gewünscht) das nächste Event.</summary>
    private string? OverlayBanner()
    {
        var now = DateTimeOffset.Now;
        if (_hintText is not null && now < _hintUntil) return _hintText;
        if (_alarmText is not null && now < _alarmUntil) return _alarmText;
        if (!_overlaySettings.ShowNextEvent) return null;

        // Nächstes Ereignis: Event aus dem Zeitplan oder ein Weltboss mit Erinnerung, je nachdem was früher kommt.
        (string Name, DateTimeOffset At)? next = null;
        if (_schedule.Events.Count > 0)
        {
            var (e, at) = _schedule.Next(now);
            next = (e.Name, at);
        }

        var boss = _worldBosses.Where(b => b.Remind && b.RespawnAt > now).MinBy(b => b.RespawnAt);
        if (boss is not null && (next is null || boss.RespawnAt < next.Value.At)) next = ("Weltboss " + boss.Name, boss.RespawnAt!.Value);
        // Erst kurz vorher zeigen (Standard 10 Min.), damit die Zeile nicht dauernd da ist.
        return next is { } n && _overlaySettings.ShowNextEventNow(n.At - now)
            ? $"Nächstes: {n.Name} in {GameEventSchedule.Countdown(n.At - now)}"
            : null;
    }

    /// <summary>Feldboss-Liste des Spiels: Zeiten zugeordneter Weltbosse (z. B. Gartua) automatisch übernehmen und speichern.</summary>
    private void OnFieldBossList(Aion2DpsMeter.Core.Analysis.FieldBossList list)
    {
        if (WorldBossTimes.Apply(_worldBosses, list) is not { } updated) return;
        _worldBosses = updated;
        try
        {
            _worldBossStore.Save(updated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nur bis zum Beenden gemerkt.
        }
    }

    /// <summary>Beste eigene DPS aus der lokalen Kampfliste (ohne den gerade beendeten Kampf); nur beim Kampfende abgefragt.</summary>
    private double? BestOwnDps(DateTimeOffset? exceptStart)
    {
        try
        {
            return FightStatistics.BestOwnDps(_fightStore.List().Select(e => e.Fight), exceptStart);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Boss-Leiste: HP des Gegners, den der eigene Charakter in den letzten 10 s am meisten getroffen hat.</summary>
    private BossStatus? OverlayBoss()
    {
        var now = DateTimeOffset.UtcNow;
        // „Alle“: Treffer aller zählen. Sonst nur ich (+ Gruppe in der Automatik); ohne erkannten Charakter keine Leiste.
        IReadOnlySet<uint>? sources;
        var view = _overlay is { IsLoaded: true } overlay ? overlay.EffectiveView : _overlaySettings.PlayerView;
        if (view == OverlaySettings.ViewAll)
        {
            sources = _liveMeter.Snapshot(now).Actors.Select(a => a.Actor).ToHashSet();
        }
        else
        {
            if (_liveMeter.OwnActor is not { } own) return null;
            var ids = new HashSet<uint> { own };
            if (view == OverlaySettings.ViewAuto) ids.UnionWith(_liveMeter.PartyMembers());
            sources = ids;
        }

        if (_liveMeter.MainTarget(now, TimeSpan.FromSeconds(10), sources) is not { } target || _enemyHp.Status(target, now) is not { } status) return null;
        var result = status with { TopHit = _liveMeter.TopHit(target, sources), FightStart = _liveMeter.FirstHitOn(target, sources) };

        // Für die Statistik merken, welcher Boss gerade bekämpft wird (wird beim Speichern des Kampfs eingetragen).
        var npc = _npcNames.Lookup(result.NpcCode);
        if (OverlayFormat.IsBoss(result, npc))
        {
            var name = npc?.Name ?? $"Gegner #{result.Target}";
            // „Echt“ (für die Bestenliste): nur vom Spiel/der Gegnerliste bestätigte Bosse, keine unbekannten mit viel HP.
            var real = BossRules.IsRealBoss(npc, result.Deadline is not null);
            if (_bossSightings.Count > 0 && _bossSightings[^1].Name == name) _bossSightings[^1] = (name, now, real);
            else _bossSightings.Add((name, now, real));
            if (_bossSightings.Count > 100) _bossSightings.RemoveAt(0);
        }

        return result;
    }

    /// <summary>
    /// Boss tot (HP 0): Kampf sofort beenden, damit DPS und Zeit genau beim Tod stehen bleiben – nur bei echten Bossen,
    /// die ich bzw. meine Gruppe in diesem Kampf getroffen habe. Läuft auf dem Aufnahme-Thread (Meter ist gesperrt-sicher).
    /// </summary>
    private void EndFightIfBossDied(uint target, DateTimeOffset time)
    {
        if (_enemyHp.Status(target, time) is not { } status) return;
        var npc = _npcNames.Lookup(status.NpcCode);
        // Einfrieren auch bei großen Gegnern, die die Gegnerliste nicht als Boss kennt (z. B. eine Kromede-Variante, 15,5 Mio HP).
        var big = status.MaxHp >= OverlayFormat.BossMinMaxHp && !(npc is not null && (npc.IsDummy || BossRules.IsObjectName(npc.Name)));
        if (!big && !BossRules.IsRealBoss(npc, status.Deadline is not null)) return;
        if (_liveMeter.OwnActor is not { } own) return;
        var sources = new HashSet<uint>(_liveMeter.PartyMembers()) { own };
        if (_liveMeter.FirstHitOn(target, sources) is null) return;
        _liveMeter.EndFight(time);
    }

    /// <summary>Speichert beendete Kämpfe im Verlauf (eigene Dateien, nie überschreiben).</summary>
    private void SaveFinishedFights(IEnumerable<FightRecord> fights)
    {
        var party = _liveMeter.PartyMembers();
        // Nur Kämpfe mit eigenen oder Gruppen-Treffern speichern (nicht z. B. Fremde an der Puppe nebenan).
        var list = fights.ToList();
        // Gemerkte Gruppe verwerfen, wenn ich länger allein gekämpft habe (Austritt ist sonst nicht erkennbar).
        foreach (var fight in list) _liveMeter.ForgetRememberedPartyAfterSoloFight(fight);
        foreach (var fight in list.Where(f => FightStatistics.IsRelevant(f, party)))
        {
            try
            {
                // Boss eintragen, dessen Leiste während des Kampfs zu sehen war (Treffer- und Uhrzeit sind beide UTC).
                var sighting = _bossSightings.LastOrDefault(s => s.At >= fight.Start && s.At <= fight.End.AddSeconds(15));
                var saved = sighting.Name is null ? fight : fight with { BossName = sighting.Name };
                _fightStore.Save(saved);
                if (sighting.Real) ShareFight(saved); // Bestenliste nur mit echten Bossen
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                DetailText.Text = $"Kampf konnte nicht gespeichert werden: {ex.Message}";
            }
        }
    }

    private void OpenFightHistory()
    {
        if (_historyWindow is { IsLoaded: true })
        {
            _historyWindow.Activate();
            return;
        }

        _historyWindow = new FightHistoryWindow(_fightStore, _skillNames);
        _historyWindow.Show();
    }

    /// <summary>Beendet das Programm sauber: Aufnahme stoppen, Einstellungen speichern, Fenster schließen.</summary>
    /// <summary>„Was ist neu“-Fenster zeigen.</summary>
    private void ShowPatchNotes(IReadOnlyList<PatchNote> notes, bool afterUpdate) => new PatchNotesWindow(notes, afterUpdate).Show();
    /// <summary>Hinweiszeile fürs Overlay: Fortschritt eines laufenden Updates oder „neue Version verfügbar“.</summary>
    private string? UpdateLine() =>
        _updateProgress ?? (_update is { } u && _overlaySettings.CheckForUpdates ? $"⬆ Neue Version {u.Tag} verfügbar – hier klicken zum Aktualisieren" : null);

    /// <summary>Klick auf den Update-Hinweis: nachfragen, laden, prüfen, EXE austauschen, neu starten.</summary>
    private async void StartUpdate()
    {
        if (_update is not { } info || _updating) return;
        var owner = _overlay is { IsVisible: true } o ? (Window)o : this;
        var notes = UpdateCheck.ShortNotes(info.Notes);
        var exe = UpdateService.InstallableExePath();
        if (!info.CanInstall || exe is null)
        {
            var open = ConfirmDialog.Ask(owner, "Update verfügbar",
                $"Version {info.Tag} ist verfügbar (du hast {AppRelease.Current}).\n\n{notes}\n\n" +
                "Automatisch installieren geht hier nicht (keine geprüfte EXE im Release oder Entwicklungsversion). Download-Seite öffnen?",
                "Seite öffnen", "Später");
            if (open) UpdateService.OpenReleasePage(info.Page);
            return;
        }

        var answer = ConfirmDialog.Ask(owner, "Update installieren",
            $"Version {info.Tag} ist verfügbar (du hast {AppRelease.Current}).\n\n{notes}\n\n" +
            "Jetzt herunterladen und den Meter neu starten? Einstellungen, Kämpfe und deine Icons im Dokumente-Ordner bleiben erhalten.\n" +
            "Hinweis: Die EXE ist nicht signiert – Windows kann beim ersten Start warnen.",
            "Jetzt aktualisieren", "Später");
        if (!answer) return;

        _updating = true;
        _updateProgress = $"⬇ Lade {info.Tag} …";
        try
        {
            var progress = new Progress<double>(p => _updateProgress = $"⬇ Lade {info.Tag} … {p * 100:0} %");
            await _updates.InstallAsync(info, exe, progress, CancellationToken.None);
            _updateProgress = $"✔ {info.Tag} installiert – starte neu …";
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            ExitApp();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException or TaskCanceledException)
        {
            _updateProgress = null;
            _updating = false;
            ConfirmDialog.Inform(owner, "Update", $"Update fehlgeschlagen: {ex.Message}\nDie bisherige Version bleibt unverändert.");
        }
    }

    private void ExitApp()
    {
        _exiting = true;
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _tray = null;
        _settingsWindow?.Close();
        Cleanup(); // Aufnahme stoppen, Overlay-Einstellungen speichern
        Application.Current.Shutdown();
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        if (AdapterBox.SelectedItem is not ComboBoxItem { Tag: NetworkAdapterInfo adapter }) return;

        ProcessSelection? process = null;
        string? processText = null;
        if (ProcessFilterCheck.IsChecked == true)
        {
            if (ProcessBox.SelectedItem is not ComboBoxItem { Tag: int pid, Content: var name })
            {
                DetailText.Text = "Bitte zuerst „Programme laden“ und ein Programm auswählen.";
                return;
            }

            // Die Verbindungen werden während der Aufnahme laufend nachgelesen, damit auch später
            // geöffnete Verbindungen (z. B. nach dem Einloggen) erfasst werden.
            process = new ProcessSelection(pid, _connections);
            processText = $"Nur Programm: {name} – Verbindungen werden laufend nachgelesen, Anzeige ca. 2 s verzögert";
        }

        var savePath = SaveCheck.IsChecked == true
            ? ProcessConnectionFilter.CreateRecordingPath(RecordingDirectory, DateTimeOffset.Now)
            : null;

        Action<RawPacket>? onRaw = null;
        if (LiveCheck.IsChecked == true)
        {
            // Neuer Decoder je Aufnahme; der Zähler (und das Live-Fenster) beginnen von vorn.
            _liveMeter.Reset();
            // Mit Programmauswahl kommen Treffer um die Zurückhaltezeit verzögert an – für die laufende Kampfzeit abziehen.
            _liveMeter.Lag = process is not null ? ProcessPacketSelector<object>.DefaultHoldBack : TimeSpan.Zero;
            // Namen bleiben nur im Speicher dieses Programms.
            var decoder = new LiveCombatDecoder(
                _liveMeter.Add,
                p =>
                {
                    _liveMeter.ReportPlayer(p.EntityId, p.Name, p.IsSelf);
                    if (p.IsSelf) RememberOwnCharacter(p.EntityId, p.Name);
                },
                s => _liveMeter.ReportSummon(s.RawId, s.Owner),
                (hp, time) =>
                {
                    _enemyHp.Report(hp.Target, hp.Hp, hp.MaxHp, time, hp.NpcCode);
                    // Nur 00 8D (Art 2 = Gegner) markiert sicher; 41 36 meldet auch Beschwörungen von Spielern.
                    if (hp.MaxHp is null) _liveMeter.MarkEnemy(hp.Target);
                    if (hp.Hp == 0) EndFightIfBossDied(hp.Target, time);
                },
                _liveMeter.ReportParty,
                list => Dispatcher.BeginInvoke(() => OnFieldBossList(list)),
                _liveMeter.ReportOwnCombatPower,
                _enemyHp.ReportBattleState,
                _liveMeter.ReportDeath,
                od => _od = od);
            _enemyHp.Reset();
            _liveDecoder = decoder;
            Interlocked.Exchange(ref _liveErrors, 0);
            onRaw = raw =>
            {
                try
                {
                    decoder.Add(raw);
                }
                catch (Exception)
                {
                    // Ein Auswertungsfehler darf die Aufnahme nicht beenden; er wird nur gezählt.
                    Interlocked.Increment(ref _liveErrors);
                }
            };
        }
        else
        {
            _liveDecoder = null;
        }

        _diagnostics.Start(adapter.Id, new CaptureOptions(SaveFilePath: savePath, Process: process, OnRawPacket: onRaw));
        DetailText.Text = string.Join("\n", new[]
        {
            processText,
            savePath is null ? null : $"Datei: {savePath}",
        }.Where(s => s is not null));
        _refreshTimer.Start();
        ShowStatistics();
    }

    private async void OnAnalyze(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(RecordingDirectory);
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Aufnahme auswählen",
            Filter = "Paketaufzeichnungen (*.pcap;*.pcapng)|*.pcap;*.pcapng|Alle Dateien (*.*)|*.*",
            InitialDirectory = RecordingDirectory,
        };
        if (dialog.ShowDialog(this) != true) return;

        AnalyzeButton.IsEnabled = false;
        DetailText.Text = $"Werte {Path.GetFileName(dialog.FileName)} aus …";
        try
        {
            var localAddresses = _diagnostics.Adapters.SelectMany(a => a.Addresses);
            var analysis = await RecordingReport.AnalyzeAsync(new PcapFileSource(), dialog.FileName, localAddresses);
            DetailText.Text = string.Empty;
            ShowReport(RecordingReport.Format(analysis, Path.GetFileName(dialog.FileName)));
        }
        catch (CaptureException ex)
        {
            DetailText.Text = $"{CaptureErrorMessages.Describe(ex.Kind)} ({ex.Message})";
        }
        finally
        {
            AnalyzeButton.IsEnabled = true;
        }
    }

    private void OnToggleOverlay(object sender, RoutedEventArgs e)
    {
        if (_overlay is { IsLoaded: true })
        {
            SaveOverlaySettings();
            _overlay.Close();
            _overlay = null;
            OverlayButton.Content = "Overlay ein";
            return;
        }

        _overlay = new OverlayWindow(
            _liveMeter,
            _overlaySettings,
            change: ChangeOverlay,
            toggleLock: ToggleClickThrough,
            // Im Overlay-Modus beendet ✕ das Programm, sonst blendet es nur das Overlay aus.
            close: () =>
            {
                if (_overlayMode) ExitApp();
                else OnToggleOverlay(this, new RoutedEventArgs());
            },
            openDetail: OpenPlayerDetail,
            openSettings: OpenOverlaySettings,
            openHistory: OpenFightHistory,
            status: () => _overlayStatus,
            banner: OverlayBanner,
            boss: OverlayBoss,
            bestOwnDps: BestOwnDps,
            npcInfo: code => _npcNames.Lookup(code),
            update: UpdateLine,
            applyUpdate: StartUpdate,
            ping: () => _liveDecoder?.Ping.Current(DateTimeOffset.UtcNow),
            od: () => _od);
        _overlay.Closed += (_, _) => OverlayButton.Content = "Overlay ein";
        _overlay.Show();
        OverlayButton.Content = "Overlay aus";
    }

    private void OnResetLive(object sender, RoutedEventArgs e) => _liveMeter.Reset();

    private void OnOverlaySettingChanged(object sender, RoutedEventArgs e) =>
        ChangeOverlay(s => s with { ClickThrough = ClickThroughCheck.IsChecked == true });

    private void OnFontSmaller(object sender, RoutedEventArgs e) => ChangeOverlay(s => s with { FontSize = s.FontSize - 1 });

    private void OnFontLarger(object sender, RoutedEventArgs e) => ChangeOverlay(s => s with { FontSize = s.FontSize + 1 });

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_overlayControlsReady) ChangeOverlay(s => s with { BackgroundOpacity = OpacitySlider.Value });
    }

    private void ChangeOverlay(Func<OverlaySettings, OverlaySettings> change)
    {
        var current = _overlay is { IsLoaded: true } overlay ? overlay.CurrentSettings : _overlaySettings;
        _overlaySettings = change(current).Normalized();
        if (_overlaySettings.ClickThrough && !current.ClickThrough) ShowClickThroughHint();
        else if (!_overlaySettings.ClickThrough) _hintText = null;
        _liveMeter.IdleTimeout = TimeSpan.FromSeconds(_overlaySettings.IdleSeconds);
        _overlay?.Apply(_overlaySettings);
        SaveOverlaySettings();
    }

    private void SaveOverlaySettings()
    {
        if (_overlay is { IsLoaded: true } overlay) _overlaySettings = overlay.CurrentSettings.Normalized();
        try
        {
            if (!_overlayStore.Save(_overlaySettings))
            {
                DetailText.Text = $"Overlay-Einstellungen nicht gespeichert: {_overlayStore.Path} stammt nicht von diesem Programm und wird nicht überschrieben.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DetailText.Text = $"Overlay-Einstellungen konnten nicht gespeichert werden: {ex.Message}";
        }
    }

    /// <summary>Wird vom Aufnahme-Thread aufgerufen; speichert nur den eigenen Charakter, lokal.</summary>
    private void RememberOwnCharacter(uint id, string name)
    {
        try
        {
            // Bekannte Klasse desselben Charakters behalten (Symbol schon vor dem ersten Treffer).
            var previousClass = _ownStore.Load(DateTimeOffset.UtcNow) is { } old && old.Name == name ? old.ClassPrefix : null;
            if (!_ownStore.Save(new RememberedOwnCharacter(id, name, DateTimeOffset.UtcNow, _liveMeter.ClassPrefixOf(id) ?? previousClass)))
            {
                Dispatcher.BeginInvoke(() => DetailText.Text =
                    $"Eigener Charakter nicht gemerkt: {_ownStore.Path} stammt nicht von diesem Programm und wird nicht überschrieben.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dispatcher.BeginInvoke(() => DetailText.Text = $"Eigener Charakter konnte nicht gemerkt werden: {ex.Message}");
        }
    }

    /// <summary>Öffnet die Details zu einem Spieler (aus Overlay oder Tabelle).</summary>
    private void OpenPlayerDetail(uint actor)
    {
        new PlayerDetailWindow(_liveMeter, actor, _skillNames).Show();
    }

    private void OnShowLive(object sender, RoutedEventArgs e)
    {
        if (_liveWindow is { IsLoaded: true })
        {
            _liveWindow.Activate();
            return;
        }

        _liveWindow = new LiveMeterWindow(_liveMeter, LiveStatus, OpenPlayerDetail) { Owner = this };
        _liveWindow.Show();
    }

    private string LiveStatus()
    {
        var capturing = _diagnostics.Status.State == CaptureState.Capturing;
        if (_liveDecoder is not { } decoder)
        {
            return capturing ? "Aufnahme läuft ohne Live-Auswertung (Haken nicht gesetzt)" : "Keine Aufnahme";
        }

        var errors = Interlocked.Read(ref _liveErrors);
        return $"{(capturing ? "Aufnahme läuft" : "Aufnahme beendet")} · Treffer gelesen: {decoder.HitCount:#,0}" +
               (errors > 0 ? $" · Auswertungsfehler: {errors:#,0}" : string.Empty);
    }

    private static string EvaluationDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Aion2DpsMeter", "Auswertungen");

    private async void OnCandidates(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(RecordingDirectory);
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Aufnahme für 04-38-Kandidaten auswählen",
            Filter = "Paketaufzeichnungen (*.pcap;*.pcapng)|*.pcap;*.pcapng|Alle Dateien (*.*)|*.*",
            InitialDirectory = RecordingDirectory,
        };
        if (dialog.ShowDialog(this) != true) return;

        CandidatesButton.IsEnabled = false;
        DetailText.Text = $"Lese 04-38-Kandidaten aus {Path.GetFileName(dialog.FileName)} …";
        try
        {
            var localAddresses = _diagnostics.Adapters.SelectMany(a => a.Addresses);
            var analysis = await DamageCandidateReport.AnalyzeAsync(new PcapFileSource(), dialog.FileName, localAddresses);

            // CSV nur lokal speichern; vorhandene Dateien werden nie überschrieben.
            Directory.CreateDirectory(EvaluationDirectory);
            var csvPath = DamageCandidateReport.CreateCsvPath(EvaluationDirectory, dialog.FileName);
            await using (var stream = new FileStream(csvPath, FileMode.CreateNew, FileAccess.Write))
            await using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(true)))
            {
                await writer.WriteAsync(DamageCandidateReport.ToCsv(analysis));
            }

            DetailText.Text = $"CSV gespeichert: {csvPath}";
            ShowReport($"CSV-Datei: {csvPath}\n\n" + DamageCandidateReport.Format(analysis, Path.GetFileName(dialog.FileName)));
        }
        catch (CaptureException ex)
        {
            DetailText.Text = $"{CaptureErrorMessages.Describe(ex.Kind)} ({ex.Message})";
        }
        catch (IOException ex)
        {
            DetailText.Text = $"CSV konnte nicht gespeichert werden: {ex.Message}";
        }
        finally
        {
            CandidatesButton.IsEnabled = true;
        }
    }

    private void ShowReport(string text)
    {
        var box = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(8),
        };
        new Window
        {
            Title = "Auswertung der Aufnahme",
            Owner = this,
            Width = 820,
            Height = 600,
            Content = box,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        }.Show();
    }

    private static string? ProcessName(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    private async void OnStop(object sender, RoutedEventArgs e)
    {
        StopButton.IsEnabled = false;
        await _diagnostics.StopAsync();
        _refreshTimer.Stop();
        ShowStatistics();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Im Overlay-Modus ist dieses Fenster nur „Erweitert“: Schließen blendet es aus, das Overlay läuft weiter.
        if (_overlayMode && !_exiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        Cleanup();
        if (!_overlayMode) Application.Current.Shutdown();
    }

    private bool _cleanedUp;

    /// <summary>Aufnahme beenden und Overlay-Einstellungen speichern (nur einmal).</summary>
    private void Cleanup()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;

        // Die Npcap-Schleife prüft den Abbruch spätestens nach 0,5 s.
        _refreshTimer.Stop();
        _autoStartTimer.Stop();
        _alarmTimer.Stop();
        if (_overlay is { IsLoaded: true })
        {
            SaveOverlaySettings();
            _overlay.Close();
        }

        _diagnostics.StopAsync().Wait(TimeSpan.FromSeconds(2));

        // Fertige und den noch laufenden Kampf in den Verlauf.
        _historyTimer.Stop();
        SaveFinishedFights(_liveMeter.DrainFinishedFights(DateTimeOffset.UtcNow));
        if (_liveMeter.ExportCurrentFight() is { } current) SaveFinishedFights([current]);
    }

    private void ShowStatus(CaptureStatus status)
    {
        StatusText.Text = status.Message;
        StatusText.Foreground = status.State switch
        {
            CaptureState.Unavailable or CaptureState.Faulted => Brushes.Firebrick,
            CaptureState.Capturing => Brushes.ForestGreen,
            _ => SystemColors.ControlTextBrush,
        };
        // Beim Start stehen Filter/Datei in der Detailzeile; nur Fehlerdetails überschreiben sie.
        if (status.State != CaptureState.Capturing && status.Detail is not null) DetailText.Text = status.Detail;

        if (status.State != CaptureState.Capturing) _refreshTimer.Stop();
        ShowStatistics();
        UpdateButtons();
    }

    private void ShowStatistics()
    {
        var stats = _diagnostics.Statistics;
        PacketsText.Text = CaptureDisplay.FormatCount(stats.PacketCount);
        BytesText.Text = CaptureDisplay.FormatBytes(stats.ByteCount);
        DurationText.Text = CaptureDisplay.FormatDuration(stats);
        RateText.Text = CaptureDisplay.FormatPacketRate(stats);
    }

    private void UpdateButtons()
    {
        var state = _diagnostics.Status.State;
        var capturing = state == CaptureState.Capturing;
        var canStart = state is CaptureState.Ready or CaptureState.Stopped or CaptureState.Faulted;

        StartButton.IsEnabled = canStart && AdapterBox.Items.Count > 0;
        QuickStartButton.IsEnabled = !capturing; // auch ohne Npcap klickbar, dann mit verständlicher Meldung
        StopButton.IsEnabled = capturing;
        ReloadButton.IsEnabled = !capturing;
        AdapterBox.IsEnabled = !capturing;
        ProcessBox.IsEnabled = !capturing;
        ProcessReloadButton.IsEnabled = !capturing;
        ProcessFilterCheck.IsEnabled = !capturing;
        LiveCheck.IsEnabled = !capturing;
        SaveCheck.IsEnabled = !capturing;
    }
}
