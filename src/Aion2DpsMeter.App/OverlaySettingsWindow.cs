using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.App;

/// <summary>
/// Einstellungen im dunklen Stil des Overlays: links Bereiche, rechts je Einstellung Titel, Steuerelement und
/// Erklärtext. Änderungen gelten sofort und werden über das Hauptfenster gespeichert.
/// </summary>
public sealed class OverlaySettingsWindow : Window
{
    // Farben wie Overlay/Boss-Leiste: sehr dunkler Grund, Karten etwas heller, violetter Akzent.
    private static readonly Brush Bg = Frozen(Color.FromRgb(0x10, 0x12, 0x18));
    private static readonly Brush NavBg = Frozen(Color.FromRgb(0x14, 0x16, 0x1D));
    private static Brush SelectedBg => ThemeColors.AccentAlpha(0x55); // Farbthema
    private static readonly Brush HoverBg = Frozen(Color.FromRgb(0x1F, 0x22, 0x2C));
    private static readonly Brush CardBg = Frozen(Color.FromRgb(0x18, 0x1B, 0x23));
    private static readonly Brush CardBorder = Frozen(Color.FromRgb(0x2A, 0x2E, 0x3A));
    private static readonly Brush TextBrush = Frozen(Color.FromRgb(0xE6, 0xE8, 0xEE));
    private static readonly Brush DimBrush = Frozen(Color.FromRgb(0x9A, 0xA0, 0xAE));
    private static Brush AccentBrush => ThemeColors.AccentBrush; // Farbthema

    private readonly Func<OverlaySettings> _get;
    private readonly Action<Func<OverlaySettings, OverlaySettings>> _change;
    private readonly StackPanel _nav = new() { Margin = new Thickness(8) };
    private readonly ContentControl _page = new();
    private readonly Dictionary<string, Func<FrameworkElement>> _pages = new();
    private readonly Func<IReadOnlyList<Aion2DpsMeter.Core.Events.WorldBoss>> _worldBosses;
    private readonly Func<IReadOnlyList<Aion2DpsMeter.Core.Events.WorldBoss>, string?> _setWorldBosses;

    private readonly Action? _showPatchNotes;

    public OverlaySettingsWindow(
        Func<OverlaySettings> get,
        Action<Func<OverlaySettings, OverlaySettings>> change,
        Action reset,
        Action showTable,
        Action showHistory,
        Func<Aion2DpsMeter.Core.Events.GameEventSchedule> schedule,
        Func<string> scheduleInfo,
        Func<IReadOnlyList<Aion2DpsMeter.Core.Events.WorldBoss>> worldBosses,
        Func<IReadOnlyList<Aion2DpsMeter.Core.Events.WorldBoss>, string?> setWorldBosses,
        Action showAdvanced,
        Action exit,
        IReadOnlyList<string> hotkeys,
        IReadOnlyList<string> failedHotkeys,
        Action? showPatchNotes = null)
    {
        _showPatchNotes = showPatchNotes;
        _get = get;
        _change = change;
        _worldBosses = worldBosses;
        _setWorldBosses = setWorldBosses;

        Title = "AionMeta – Einstellungen";
        Width = 880;
        Height = 640;
        Background = Bg;
        DarkTheme.Apply(this);
        Foreground = TextBrush;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _pages["⚙  Allgemein"] = () => Page(
            Section("Allgemein"),
            Info("Das Meter arbeitet rein passiv: Es liest nur Netzwerkpakete mit (über Npcap), verändert nichts am Spiel, " +
                 "liest keinen Spielspeicher und sendet nichts. Alle Daten bleiben auf diesem PC."),
            Info("Hinweis: Die Nutzungsbedingungen von AION 2 verbieten „nicht autorisierte Programme“. Auch passive Meter " +
                 "können darunter fallen – Nutzung auf eigenes Risiko, eine Kontosperre ist möglich."),
            Info("Das Overlay ist ein normales Fenster über dem Spiel: sichtbar nur im Fenstermodus oder randlosen Vollbild, " +
                 "nicht im exklusiven Vollbild. Werte nach Community-Hypothese (abgeglichen, nicht offiziell)."),
            Setting("Overlay-Position", ActionButton("Zurücksetzen", () => Change(s => s with { Left = 40, Top = 120, Width = 360 })),
                "Holt das Overlay an die Standardstelle oben links zurück."),
            Setting("Was ist neu", ActionButton("Anzeigen", () => _showPatchNotes?.Invoke()), "Patchnotes: was sich in den letzten Versionen geändert hat."),
            Setting("Bestenliste (Teilen)", ShareChoice(),
                "Aus (Standard): nichts wird gesendet. Anonym / Mit Name: nach jedem Bosskampf gehen Boss, Dauer, dein Schaden, deine DPS, " +
                "Klasse, dein GS, deine KS und die Version an die Bestenliste der AionMeta-Webseite – bei „Mit Name“ zusätzlich dein Charaktername. Nie Werte oder Namen anderer Spieler."),
            Setting("Auf Updates prüfen", Toggle(() => _get().CheckForUpdates, v => Change(s => s with { CheckForUpdates = v })),
                $"Du hast {Aion2DpsMeter.Core.Updates.AppRelease.Current}. Fragt beim Start und alle 30 Minuten bei GitHub nach einer neuen Version " +
                "(nur lesen, es werden keine Daten gesendet). Gibt es eine, erscheint im Overlay ein Hinweis – ein Klick darauf installiert sie nach Rückfrage."),
            Setting("Programm beenden", ActionButton("Beenden", exit), "Aufnahme stoppen, Einstellungen speichern, schließen."));

        _pages["⚔  Kampf"] = () => Page(
            Section("Kampf"),
            Setting("Inaktivitäts-Timeout (Sekunden)", NumberBox(() => _get().IdleSeconds, v => Change(s => s with { IdleSeconds = v }), 3, 120, 1),
                "Nach so vielen Sekunden ohne Treffer gilt der Kampf als beendet; der nächste Treffer beginnt einen neuen Kampf."),
            Setting("Kampf zurücksetzen", ActionButton("Zurücksetzen", reset), "Alle Werte des aktuellen Kampfs löschen (er wird dann nicht gespeichert)."),
            Setting("Kämpfe", ActionButton("Öffnen", showHistory),
                "Verlauf aller beendeten Kämpfe, nur lokal gespeichert (Dokumente\\Aion2DpsMeter\\Kaempfe). Umbenennen, löschen, Details ansehen."),
            Info("Berserk-Timer: läuft automatisch in der Boss-Leiste, wenn ein Boss sein Zeitlimit beim Kampfbeginn meldet."));
        // Zielmodus, „Nur Gruppe“ und „Nur Bosse / Trainingspuppen“ sind ausgeblendet, bis sie an Aufnahmen belegt sind.

        _pages["▣  Anzeige"] = () => Page(
            Section("Anzeige"),
            Setting("Kontrast / Lesbarkeit", ContrastChoice(),
                "„Hoch“ (Standard): hellere Schrift mit dunklem Rand und mindestens 75 % Hintergrund – gut lesbar auch über hellem " +
                "oder buntem Spiel. „Sehr hoch“: fast weiße Schrift, mindestens 90 % Hintergrund. „Normal“: wie früher, dezenter."),
            Setting("Deckkraft des Hintergrunds", OpacitySlider(), "Wie stark der dunkle Hintergrund des Overlays deckt. Bei Kontrast „Hoch“/„Sehr hoch“ gilt die Mindest-Deckkraft der Stufe."),
            Setting("Schriftgröße", NumberBox(() => (int)_get().FontSize, v => Change(s => s with { FontSize = v }), 9, 24, 1),
                "Größe von Text und Zeilen im Overlay."),
            Setting("Spieler im Overlay", ViewChoice(),
                "Automatik (Standard): allein nur du, in einer Gruppe du und deine Gruppenmitglieder – ohne Knopfdruck. " +
                "„Nur ich“: immer nur du. „Alle“: alle Spieler, auch Fremde. Der Knopf Ⓐ/👥 im Overlay schaltet vorübergehend zwischen " +
                "dieser Ansicht und „Alle“ um (wird nicht gespeichert – nach dem Neustart gilt wieder die Auswahl hier). Die Gruppe wird aus der Gruppenliste des Spiels " +
                "erkannt (Community-Hypothese 02 97, an einer Aufnahme mit 5er-Gruppe belegt)."),
            Setting("Kampfkraft anzeigen", Toggle(() => _get().ShowCombatPower, v => Change(s => s with { ShowCombatPower = v })),
                "Kampfkraft neben dem Namen – für dich (aus deinem Charakter-Datensatz) und deine Gruppenmitglieder (aus der Gruppenliste). " +
                "Für fremde Spieler schickt das Spiel sie nicht. Community-Hypothese, noch nicht mit dem Spiel abgeglichen."),
            Setting("Gruppenhinweis anzeigen", Toggle(() => _get().ShowPartyHint, v => Change(s => s with { ShowPartyHint = v })),
                "Zeigt „Gruppe 2/5 erkannt“, wenn das Spiel eine größere Gruppe meldet, als Namen bekannt sind – nur im Kampf und bis " +
                "60 s danach. Fehlende Namen kommen beim Teleport bzw. wenn die Mitglieder ins Blickfeld kommen."),
            Setting("Od-Energie anzeigen", Toggle(() => _get().ShowOd, v => Change(s => s with { ShowOd = v })),
                "Zusatz-Od-Energie (der blaue Wert, z. B. +95) unten in der Fußzeile. Erscheint, sobald das Spiel ihn schickt (beim Einloggen oder Einlösen)."),
            Setting("Od-Energie zusammenrechnen", Toggle(() => _get().OdTotal, v => Change(s => s with { OdTotal = v })),
                "An: eine Zahl (z. B. 110 = 15 + 95 Zusatz). Aus: aufgeteilt wie im Spiel, „15 (+95)“. Die Aufteilung steht immer im Tooltip."),
            Setting("Berserk-Prognose", Toggle(() => _get().ShowBerserkForecast, v => Change(s => s with { ShowBerserkForecast = v })),
                "In der Boss-Karte bei Bossen mit Zeitlimit: „Schafft ihr“ (grün), „Knapp“ (gelb) oder „Zu wenig DPS – so viel % mehr nötig“ (rot). Erscheint nach ca. 15 s Kampf."),
            Setting("Gruppen-Lobby anzeigen", Toggle(() => _get().ShowLobby, v => Change(s => s with { ShowLobby = v })),
                "Wenn du in einer Gruppe bist und gerade kein Kampf läuft: alle Mitglieder mit Stufe, GS und KS – Beitritt und Austritt sofort sichtbar."),
            Setting("Ping anzeigen", Toggle(() => _get().ShowPing, v => Change(s => s with { ShowPing = v })),
                "Ping zum Spielserver unten in der Fußzeile, grün/gelb/rot. Rein passiv aus der Spielverbindung gemessen – es wird nichts gesendet."),
            Setting("Automatisch ausblenden", AutoHideChoice(),
                "Blendet das Overlay aus, wenn so lange kein Kampf war (Standard aus). Es kommt beim nächsten Treffer von dir bzw. " +
                "deiner Gruppe oder bei einem Alarm von selbst wieder. Tastenkürzel und Tray-Symbol funktionieren weiter."),
            Setting("Farbthema", ThemeChoice(), "Akzentfarbe für Overlay, Einstellungen und Statistik. Die Klassenfarben der Spieler bleiben."),
            Setting("Kampfende-Karte", Toggle(() => _get().ShowFightEndCard, v => Change(s => s with { ShowFightEndCard = v })),
                "Nach jedem Kampf kurz eine Karte mit Dauer, DPS, Anteil, Platz und Bestwert einblenden (Standard aus)."),
            Setting("Effekte & Animationen", Toggle(() => _get().Effects, v => Change(s => s with { Effects = v })),
                "Gleitende Balken, hochzählende Zahlen, sanfter Platztausch, Aufblitzen bei großen Treffern, pulsierender Kampf-Punkt, " +
                "Nachzieh-Effekt und Blinken der Boss-Leiste, Schatten, Einblenden der Kampfende-Karte. Aus = ruhige Anzeige ohne Gleiten (spart etwas Rechenzeit)."),
            Setting("Platzwechsel-Pfeile", Toggle(() => _get().ShowRankArrows, v => Change(s => s with { ShowRankArrows = v })),
                "Überholt jemand einen anderen, steht kurz (ca. 3 s) ein grünes ▲ oder rotes ▼ neben dem Namen. Nur mit „Effekte & Animationen“."),
            Setting("Boss-Leiste anzeigen", Toggle(() => _get().ShowBossBar, v => Change(s => s with { ShowBossBar = v })),
                "HP-Leiste des Gegners, den du gerade am meisten triffst: Prozent, Rest-HP und geschätzte Zeit bis zum Kill. " +
                "Ohne Max-HP (mitten im Kampf eingestiegen) nur die Rest-HP. Noch nicht im Spiel bestätigt (*)."),
            Setting("Höchstens Spieler", NumberBox(() => _get().MaxRows, v => Change(s => s with { MaxRows = v }), 3, 20, 1),
                "So viele Spieler zeigt das Overlay; der eigene Charakter ist immer dabei."),
            Setting("Spieler unter 1 % ausblenden", Toggle(() => _get().HideTinyRows, v => Change(s => s with { HideTinyRows = v })),
                "Sehr kleine Anteile (z. B. kurz vorbeischauende Spieler) zählen dann nur unter „+ n weitere“."),
            Setting("Klicks durchlassen", Toggle(() => _get().ClickThrough, v => Change(s => s with { ClickThrough = v })),
                "Mausklicks gehen durch das Overlay ans Spiel. Zurück mit dem Tastenkürzel (siehe Tastenbelegung)."),
            Setting("Eigene Klassen-Symbole", ActionButton("Icon-Ordner öffnen", OpenIconFolder),
                "Standard sind eigene einfache Symbole. Wer eigene Bilder nutzen möchte, legt PNG-Dateien in diesen Ordner: " +
                "kantor, templer, assassine, bogenschuetze, magier, beschwoerer, kleriker, gladiator, pet, unbekannt (.png). " +
                "Fehlt eine Datei oder ist sie ungültig, wird das eigene Symbol gezeigt. Offizielle Spielgrafiken sind Eigentum " +
                "von NCSoft und werden nicht mitgeliefert."));

        _pages["🔔  Event-Alarme"] = () => EventPage(schedule(), scheduleInfo());

        _pages["⌨  Tastenbelegung"] = () =>
        {
            var items = new List<FrameworkElement> { Section("Tastenbelegung") };
            foreach (var h in hotkeys) items.Add(Info("✓  " + h));
            foreach (var h in failedHotkeys) items.Add(Info("✗  " + h + " – von einem anderen Programm belegt, ohne Wirkung"));
            items.Add(Info("Die Kürzel meldet Windows nur diesem Programm (RegisterHotKey). Es werden keine Tasten ans Spiel " +
                           "gesendet und keine Eingaben automatisiert. Die Belegung ist derzeit fest."));
            return Page(items.ToArray());
        };

        _pages["🛠  Debug"] = () => Page(
            Section("Debug"),
            Setting("Tabelle", ActionButton("Öffnen", showTable), "Ausführliche Tabelle mit allen Spielern, Treffern und Pets."),
            Setting("Entwickler-Fenster", ActionButton("Öffnen", showAdvanced),
                "Netzwerkkarte/Programm von Hand wählen, Aufnahme als .pcap speichern (für Fehlersuche), Aufnahmen auswerten."),
            Info("Einstellungen liegen in %LOCALAPPDATA%\\Aion2DpsMeter, Fähigkeitsnamen in Dokumente\\Aion2DpsMeter\\skills.json. " +
                 "Kein Konto, kein Upload, keine Online-Verbindung."));

        // Navigation: Kopf mit Programmname, darunter die Seiten; die gewählte Seite mit Akzentstreifen links.
        var brand = new StackPanel { Margin = new Thickness(14, 16, 14, 18) };
        brand.Children.Add(new TextBlock { Text = "AionMeta", FontSize = 18, FontWeight = FontWeights.Bold });
        brand.Children.Add(new TextBlock { Text = "Einstellungen", Foreground = DimBrush, FontSize = 12 });
        _nav.Children.Add(brand);
        foreach (var name in _pages.Keys)
        {
            var item = new Border
            {
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 0, 0, 2),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(3, 0, 0, 0),
                BorderBrush = Brushes.Transparent,
                Background = Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = name,
                Child = new TextBlock { Text = name, FontSize = 13.5 },
            };
            item.MouseEnter += (_, _) => { if (!Equals(item.Tag, _current)) item.Background = HoverBg; };
            item.MouseLeave += (_, _) => { if (!Equals(item.Tag, _current)) item.Background = Brushes.Transparent; };
            item.MouseLeftButtonUp += (_, _) => ShowPage(name);
            _nav.Children.Add(item);
        }

        var navPanel = new Border
        {
            Background = NavBg,
            Width = 210,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = _nav,
        };
        var root = new DockPanel();
        DockPanel.SetDock(navPanel, Dock.Left);
        root.Children.Add(navPanel);
        root.Children.Add(new ScrollViewer { Content = _page, Padding = new Thickness(24, 18, 24, 18), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        ShowPage(_pages.Keys.First());
    }

    private string? _current;

    private void ShowPage(string name)
    {
        _current = name;
        foreach (var b in _nav.Children.OfType<Border>().Where(b => b.Tag is string))
        {
            var selected = (string)b.Tag == name;
            b.Background = selected ? SelectedBg : Brushes.Transparent;
            b.BorderBrush = selected ? AccentBrush : Brushes.Transparent;
        }

        // Seitenkopf: großer Titel (ohne Symbol), darunter der Inhalt.
        var title = name.Length > 3 && !char.IsLetter(name[0]) ? name[name.IndexOf(' ')..].Trim() : name;
        var page = new StackPanel();
        page.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
        page.Children.Add(_pages[name]());
        _page.Content = page;
    }

    private void Change(Func<OverlaySettings, OverlaySettings> change) => _change(change);

    private FrameworkElement EventPage(Aion2DpsMeter.Core.Events.GameEventSchedule schedule, string info)
    {
        var page = new StackPanel();
        page.Children.Add(Section("Event-Alarme"));

        var volume = new Slider { Minimum = 0, Maximum = 1, Width = 160, Value = _get().AlarmVolume };
        volume.ValueChanged += (_, e) => Change(s => s with { AlarmVolume = e.NewValue });
        page.Children.Add(Setting("Lautstärke", volume, "Lautstärke des Erinnerungstons."));

        var tone = new ComboBox { Width = 120, ItemsSource = OverlaySettings.AlarmTones, SelectedItem = _get().AlarmTone };
        tone.SelectionChanged += (_, _) =>
        {
            if (tone.SelectedItem is string t) Change(s => s with { AlarmTone = t });
        };
        var test = new Button { Content = "▶ Test", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 1, 8, 1) };
        test.Click += (_, _) => AlarmSound.Play(_get().AlarmTone, _get().AlarmVolume);
        var tonePanel = new StackPanel { Orientation = Orientation.Horizontal };
        tonePanel.Children.Add(tone);
        tonePanel.Children.Add(test);
        page.Children.Add(Setting("Ton", tonePanel, "Selbst erzeugte Töne (keine fremden Audiodateien)."));

        page.Children.Add(Setting("Nächstes Event im Overlay", Toggle(() => _get().ShowNextEvent, v => Change(s => s with { ShowNextEvent = v })),
            "Zeigt unter der Kopfzeile des Overlays das nächste Event (oder den nächsten Weltboss) mit Restzeit."));

        var leadChoice = new ComboBox
        {
            Width = 150,
            ItemsSource = OverlaySettings.NextEventLeadTimes.Select(m => m == 0 ? "immer" : $"{m} Min. vorher").ToList(),
            SelectedIndex = Math.Max(0, Array.IndexOf(OverlaySettings.NextEventLeadTimes, _get().NextEventMinutes)),
        };
        leadChoice.SelectionChanged += (_, _) =>
        {
            var minutes = OverlaySettings.NextEventLeadTimes[Math.Max(0, leadChoice.SelectedIndex)];
            Change(s => s with { NextEventMinutes = minutes });
        };
        page.Children.Add(Setting("„Nächstes“ erst ab", leadChoice,
            "Die Zeile erscheint erst so kurz vor dem Event (Standard 10 Min.). Erinnerungen mit Ton bleiben davon unberührt."));

        var now = DateTimeOffset.Now;
        var zone = schedule.Zone;
        foreach (var e in schedule.Events)
        {
            var next = e.NextAfter(now, zone).ToLocalTime();
            var card = new Border
            {
                Background = CardBg,
                BorderBrush = CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 10, 16, 10),
                Margin = new Thickness(0, 0, 0, 8),
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = e.Name, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock
            {
                Text = $"{e.Description} · nächstes {next.ToString("ddd HH:mm", CultureInfo.GetCultureInfo("de-DE"))}, in {Aion2DpsMeter.Core.Events.GameEventSchedule.Countdown(next - now)}",
                Foreground = DimBrush,
                FontSize = 12,
            });
            grid.Children.Add(text);

            var id = e.Id;
            int? Current() => _get().EventAlarms is { } a && a.TryGetValue(id, out var m) ? m : null;
            var lead = new ComboBox
            {
                Width = 120,
                ItemsSource = OverlaySettings.AlarmLeadTimes.Select(m => $"{m} Min. vorher").ToList(),
                SelectedIndex = Array.IndexOf(OverlaySettings.AlarmLeadTimes, Current() ?? 5),
                IsEnabled = Current() is not null,
            };
            var on = new CheckBox { IsChecked = Current() is not null, Content = "Erinnern", Foreground = TextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };

            void Save()
            {
                var minutes = OverlaySettings.AlarmLeadTimes[Math.Max(0, lead.SelectedIndex)];
                Change(s =>
                {
                    var map = new Dictionary<string, int>(s.EventAlarms ?? new Dictionary<string, int>());
                    if (on.IsChecked == true) map[id] = minutes;
                    else map.Remove(id);
                    return s with { EventAlarms = map };
                });
                lead.IsEnabled = on.IsChecked == true;
            }

            on.Click += (_, _) => Save();
            lead.SelectionChanged += (_, _) =>
            {
                if (on.IsChecked == true) Save();
            };

            var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            controls.Children.Add(on);
            controls.Children.Add(lead);
            Grid.SetColumn(controls, 1);
            grid.Children.Add(controls);
            card.Child = grid;
            page.Children.Add(card);
        }

        page.Children.Add(Info($"Zeitplan (EU, Zeitzone {schedule.TimeZoneId}, Sommerzeit berücksichtigt) aus: {info}. " +
                               "Die Datei kann man selbst anpassen; nach einem Neustart gilt sie. Kein Internetabruf. " +
                               "Der Alarm ist nur ein Ton und eine Zeile im Overlay."));

        page.Children.Add(Section("Weltbosse"));
        var bossPanel = new StackPanel();
        page.Children.Add(bossPanel);
        BuildWorldBosses(bossPanel, null);
        return page;
    }

    /// <summary>
    /// Weltbosse mit selbst eingetragener Zeit: Restzeit aus dem Spiel abschreiben („4:16:50“ oder „4 Std. 16 Min. 50 Sek.“),
    /// daraus Uhrzeit, Restzeit und Erinnerung. Zeiten sind je Server verschieden, daher keine feste Tabelle.
    /// </summary>
    private void BuildWorldBosses(StackPanel panel, string? message)
    {
        panel.Children.Clear();
        var bosses = _worldBosses().ToList();
        var now = DateTimeOffset.Now;
        var german = CultureInfo.GetCultureInfo("de-DE");

        void Save(List<Aion2DpsMeter.Core.Events.WorldBoss> list, string? note = null)
        {
            var problem = _setWorldBosses(list);
            BuildWorldBosses(panel, problem ?? note);
        }

        for (var i = 0; i < bosses.Count; i++)
        {
            var index = i;
            var boss = bosses[i];
            var card = new Border { Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 8) };
            var content = new StackPanel();

            var top = new DockPanel();
            var remove = new Button { Content = "✕", Padding = new Thickness(8, 1, 8, 1), ToolTip = "Weltboss entfernen" };
            remove.Click += (_, _) =>
            {
                bosses.RemoveAt(index);
                Save(bosses);
            };
            DockPanel.SetDock(remove, Dock.Right);
            top.Children.Add(remove);
            var name = new TextBox { Text = boss.Name, MinWidth = 200, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
            name.LostFocus += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(name.Text) && name.Text.Trim() != boss.Name)
                {
                    bosses[index] = boss with { Name = name.Text.Trim()[..Math.Min(name.Text.Trim().Length, 40)] };
                    Save(bosses);
                }
            };
            top.Children.Add(name);
            content.Children.Add(top);

            var status = boss.RespawnAt is not { } at
                ? "Noch keine Zeit eingetragen."
                : at > now
                    ? $"Erscheint {at.ToLocalTime().ToString("ddd HH:mm:ss", german)} – in {Aion2DpsMeter.Core.Events.GameEventSchedule.Countdown(at - now)}"
                    : $"Boss sollte da sein (seit {at.ToLocalTime().ToString("ddd HH:mm", german)}). Nach dem nächsten Kill „Gerade getötet“ drücken.";
            if (boss.FromGame) status += "  (aus der Boss-Übersicht des Spiels*)";
            content.Children.Add(new TextBlock { Text = status, Foreground = DimBrush, FontSize = 12, Margin = new Thickness(0, 4, 0, 6), TextWrapping = TextWrapping.Wrap });

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = "Kommt wieder in", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var input = new TextBox { Width = 150, ToolTip = "Restzeit aus dem Spiel, z. B. 4:16:50 oder 4 Std. 16 Min. 50 Sek.", VerticalContentAlignment = VerticalAlignment.Center };
            row.Children.Add(input);
            var apply = new Button { Content = "Übernehmen", Margin = new Thickness(6, 0, 12, 0), Padding = new Thickness(10, 1, 10, 1) };
            apply.Click += (_, _) =>
            {
                if (Aion2DpsMeter.Core.Events.WorldBossTimes.ParseCountdown(input.Text) is { } span)
                {
                    bosses[index] = boss with { RespawnAt = DateTimeOffset.Now + span, FromGame = false };
                    Save(bosses);
                }
                else
                {
                    BuildWorldBosses(panel, $"„{input.Text}“ nicht verstanden – bitte z. B. 4:16:50 oder 4 Std. 16 Min. 50 Sek. eingeben.");
                }
            };
            input.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter) apply.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            };
            row.Children.Add(apply);

            var remind = new CheckBox { IsChecked = boss.Remind, Content = "Erinnern", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            remind.Click += (_, _) =>
            {
                bosses[index] = boss with { Remind = remind.IsChecked == true };
                Save(bosses);
            };
            row.Children.Add(remind);
            var lead = new ComboBox
            {
                Width = 120,
                ItemsSource = OverlaySettings.AlarmLeadTimes.Select(m => $"{m} Min. vorher").ToList(),
                SelectedIndex = Math.Max(0, Array.IndexOf(OverlaySettings.AlarmLeadTimes, boss.LeadMinutes)),
            };
            lead.SelectionChanged += (_, _) =>
            {
                var minutes = OverlaySettings.AlarmLeadTimes[Math.Max(0, lead.SelectedIndex)];
                if (minutes == boss.LeadMinutes) return;
                bosses[index] = boss with { LeadMinutes = minutes };
                Save(bosses);
            };
            row.Children.Add(lead);
            content.Children.Add(row);

            // Zweite Zeile: Respawn-Abstand, Kill-Uhrzeit oder „Gerade getötet“ → nächster Spawn = Kill + Abstand.
            var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            row2.Children.Add(new TextBlock { Text = "Abstand", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var interval = new TextBox
            {
                Width = 80,
                Text = boss.Interval is { } iv ? $"{(int)iv.TotalHours}:{iv.Minutes:00}:{iv.Seconds:00}" : string.Empty,
                ToolTip = "Respawn-Abstand nach einem Kill, z. B. 12:00:00",
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            interval.LostFocus += (_, _) =>
            {
                var parsed = Aion2DpsMeter.Core.Events.WorldBossTimes.ParseCountdown(interval.Text);
                if (string.IsNullOrWhiteSpace(interval.Text) ? boss.Interval is null : parsed == boss.Interval) return;
                if (!string.IsNullOrWhiteSpace(interval.Text) && parsed is null)
                {
                    BuildWorldBosses(panel, $"Abstand „{interval.Text}“ nicht verstanden – z. B. 12:00:00.");
                    return;
                }

                bosses[index] = boss with { Interval = parsed };
                Save(bosses);
            };
            row2.Children.Add(interval);
            row2.Children.Add(new TextBlock { Text = "Kill um", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) });
            var kill = new TextBox { Width = 70, ToolTip = "Uhrzeit des Kills, z. B. 17:42", VerticalContentAlignment = VerticalAlignment.Center };
            row2.Children.Add(kill);
            void Killed(DateTimeOffset at)
            {
                if (boss.SpawnAfterKill(at) is { } next)
                {
                    bosses[index] = boss with { RespawnAt = next, FromGame = false };
                    Save(bosses);
                }
                else
                {
                    BuildWorldBosses(panel, "Erst den Respawn-Abstand eintragen (z. B. 12:00:00).");
                }
            }

            var killApply = new Button { Content = "Übernehmen", Margin = new Thickness(6, 0, 6, 0), Padding = new Thickness(10, 1, 10, 1) };
            killApply.Click += (_, _) =>
            {
                if (Aion2DpsMeter.Core.Events.WorldBossTimes.ParseKillTime(kill.Text, DateTimeOffset.Now) is { } at) Killed(at);
                else BuildWorldBosses(panel, $"Uhrzeit „{kill.Text}“ nicht verstanden – z. B. 17:42.");
            };
            row2.Children.Add(killApply);
            var now2 = new Button { Content = "Gerade getötet", Padding = new Thickness(10, 1, 10, 1), ToolTip = "Kill jetzt: nächster Spawn = jetzt + Abstand" };
            now2.Click += (_, _) => Killed(DateTimeOffset.Now);
            row2.Children.Add(now2);
            content.Children.Add(row2);

            card.Child = content;
            panel.Children.Add(card);
        }

        var add = new Button { Content = "+ Weltboss hinzufügen", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 0, 8) };
        add.Click += (_, _) =>
        {
            bosses.Add(new Aion2DpsMeter.Core.Events.WorldBoss(Guid.NewGuid().ToString("N")[..8], "Neuer Weltboss", null));
            Save(bosses);
        };
        panel.Children.Add(add);
        if (message is not null) panel.Children.Add(new TextBlock { Text = message, Foreground = Brushes.Khaki, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(Info("Zeiten sind je Server verschieden: Restzeit aus dem Spiel (Karte → Weltboss) abschreiben und „Übernehmen“ – " +
                                "oder nach einem Kill „Gerade getötet“ bzw. die Kill-Uhrzeit eintragen (nächster Spawn = Kill + Abstand). " +
                                "Ist die Zeit abgelaufen, rät der Meter keinen weiteren Zyklus. " +
                                "Gespeichert nur lokal (Dokumente\\Aion2DpsMeter\\weltbosse.json). Gartua wird automatisch gefüllt, sobald das Spiel " +
                                "die Boss-Übersicht von Altgard schickt (z. B. Karte öffnen oder in Altgard sein); * noch nicht im Spiel bestätigt."));
    }

    /// <summary>Legt nur den (leeren) Ordner an, falls er fehlt, und öffnet ihn im Explorer.</summary>
    private void OpenIconFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(ClassIcons.IconDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{ClassIcons.IconDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, ex.Message, "Ordner nicht geöffnet");
        }
    }

    private static StackPanel Page(params FrameworkElement[] items)
    {
        var panel = new StackPanel();
        foreach (var i in items) panel.Children.Add(i);
        return panel;
    }

    /// <summary>Abschnittsüberschrift in Akzentfarbe mit feiner Linie.</summary>
    private static FrameworkElement Section(string title)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 10) };
        panel.Children.Add(new TextBlock
        {
            Text = title.ToUpper(CultureInfo.GetCultureInfo("de-DE")),
            Foreground = AccentBrush,
            FontWeight = FontWeights.Bold,
            FontSize = 11.5,
        });
        panel.Children.Add(new Border { Height = 1, Background = CardBorder, Margin = new Thickness(0, 6, 0, 0) });
        return panel;
    }

    /// <summary>Hinweis als dezente Karte mit ⓘ.</summary>
    private static FrameworkElement Info(string text)
    {
        var line = new DockPanel();
        var icon = new TextBlock { Text = "ⓘ", Foreground = AccentBrush, FontSize = 14, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(icon, Dock.Left);
        line.Children.Add(icon);
        line.Children.Add(new TextBlock { Text = text, Foreground = DimBrush, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        return new Border
        {
            Child = line,
            Background = ThemeColors.AccentAlpha(0x40),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 4, 0, 10),
        };
    }

    /// <summary>Eine Einstellung als Karte: Titel und Beschreibung links, Bedienelement rechts; leichtes Hervorheben beim Überfahren.</summary>
    private static FrameworkElement Setting(string title, FrameworkElement control, string description)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14 });
        text.Children.Add(new TextBlock { Text = description, Foreground = DimBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 16, 0) });
        grid.Children.Add(text);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        var card = new Border
        {
            Child = grid,
            Background = CardBg,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 8),
        };
        card.MouseEnter += (_, _) => card.BorderBrush = AccentBrush;
        card.MouseLeave += (_, _) => card.BorderBrush = CardBorder;
        return card;
    }

    private static Button ActionButton(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 3, 14, 3), MinWidth = 110 };
        button.Click += (_, _) => action();
        return button;
    }

    private ComboBox AutoHideChoice()
    {
        var box = new ComboBox
        {
            Width = 170,
            ItemsSource = OverlaySettings.AutoHideChoices.Select(s => s == 0 ? "aus" : $"nach {s} s ohne Kampf").ToList(),
            SelectedIndex = Math.Max(0, Array.IndexOf(OverlaySettings.AutoHideChoices, _get().AutoHideSeconds)),
        };
        box.SelectionChanged += (_, _) =>
        {
            var seconds = OverlaySettings.AutoHideChoices[Math.Max(0, box.SelectedIndex)];
            Change(s => s with { AutoHideSeconds = seconds });
        };
        return box;
    }

    private ComboBox ContrastChoice()
    {
        var box = new ComboBox { Width = 150, ItemsSource = OverlayContrast.Levels, SelectedItem = _get().Contrast };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is string level) Change(s => s with { Contrast = level });
        };
        return box;
    }

    private ComboBox ShareChoice()
    {
        var box = new ComboBox { Width = 150, ItemsSource = ShareModes.All, SelectedItem = _get().ShareMode };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is string mode) Change(s => s with { ShareMode = mode, ShareAsked = true, ShareConsent = ShareModes.ConsentVersion });
        };
        return box;
    }

    private ComboBox ThemeChoice()
    {
        var box = new ComboBox { Width = 150, ItemsSource = OverlaySettings.Themes, SelectedItem = _get().Theme };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is not string theme) return;
            Change(s => s with { Theme = theme });
            ThemeColors.Set(theme);
            if (_current is { } page) ShowPage(page); // Akzent im Einstellungsfenster sofort neu zeichnen
        };
        return box;
    }

    private ComboBox ViewChoice()
    {
        var box = new ComboBox { Width = 150, ItemsSource = OverlaySettings.PlayerViews, SelectedItem = _get().PlayerView };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is string view) Change(s => s with { PlayerView = view, PlayerViewChosen = true });
        };
        return box;
    }

    private static CheckBox Toggle(Func<bool> get, Action<bool> set)
    {
        var box = new CheckBox { IsChecked = get(), Content = get() ? "an" : "aus", Foreground = TextBrush };
        box.Click += (_, _) =>
        {
            set(box.IsChecked == true);
            box.Content = box.IsChecked == true ? "an" : "aus";
        };
        return box;
    }

    private static FrameworkElement NumberBox(Func<int> get, Action<int> set, int min, int max, int step)
    {
        var value = new TextBlock { Text = get().ToString(CultureInfo.InvariantCulture), MinWidth = 36, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Button Step(string label, int delta)
        {
            var b = new Button { Content = label, Width = 28, Height = 26, Padding = new Thickness(0) };
            b.Click += (_, _) =>
            {
                var v = Math.Clamp(get() + delta, min, max);
                set(v);
                value.Text = get().ToString(CultureInfo.InvariantCulture);
            };
            return b;
        }

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(Step("–", -step));
        panel.Children.Add(value);
        panel.Children.Add(Step("+", step));
        return panel;
    }

    private FrameworkElement OpacitySlider()
    {
        var slider = new Slider { Minimum = 0.1, Maximum = 1, Width = 160, Value = _get().BackgroundOpacity };
        slider.ValueChanged += (_, e) => Change(s => s with { BackgroundOpacity = e.NewValue });
        return slider;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
