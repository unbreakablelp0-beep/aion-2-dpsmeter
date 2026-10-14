using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.App;

/// <summary>
/// Overlay als ganz normales, randloses WPF-Fenster über dem Spiel: durchsichtig, immer im Vordergrund und nicht
/// aktivierbar (das Spiel behält den Fokus). Es hakt sich nicht in die Grafik des Spiels ein, liest oder ändert
/// keinen Spielspeicher und sendet keine Eingaben. Sichtbar nur im Fenstermodus oder randlosen Vollbild.
/// Aufbau: Kopfzeile (Status, Kampfzeit, Knöpfe), Spielerbalken, Fußzeile (eigener Charakter, Sperre).
/// Werte kommen 10× pro Sekunde; Zahlen, Balken und Zeilenreihenfolge gleiten weich (ca. 250 ms) zum neuen Wert.
/// </summary>
public sealed class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExAppWindow = 0x00040000;

    private static readonly Brush TextBrush = Frozen(Color.FromRgb(0xE6, 0xE8, 0xEE));
    // Nebentexte und Namen: Farbe je Kontraststufe (Einstellungen → Anzeige). Je Thread eigene, nicht eingefrorene Pinsel,
    // damit ein Wechsel sofort wirkt und Fenster auf anderen Threads (Tests) nichts teilen.
    private static readonly ThreadLocal<SolidColorBrush> DimLocal = new(() => new SolidColorBrush(Color.FromRgb(0xC8, 0xCD, 0xD6)));
    private static readonly ThreadLocal<SolidColorBrush> NameLocal = new(() => new SolidColorBrush(Color.FromRgb(0xF0, 0xF2, 0xF6)));
    private static SolidColorBrush DimBrush => DimLocal.Value!;
    private static SolidColorBrush NameBrush => NameLocal.Value!; // etwas dezenter als die DPS-Zahl
    private static readonly Brush ActiveDot = Frozen(Color.FromRgb(0x4C, 0xC3, 0x5A));
    private static readonly Brush IdleDot = Frozen(Color.FromRgb(0x77, 0x7C, 0x88));
    private static Brush AccentBrush => ThemeColors.AccentBrush; // Farbthema
    private static readonly FontFamily SymbolFont = new("Segoe UI Symbol");

    private readonly LiveCombatMeter _meter;
    private readonly Action _toggleLock;
    private readonly Action<uint> _openDetail;
    private readonly Action<Func<OverlaySettings, OverlaySettings>> _change;
    private readonly Func<string?> _status;
    private readonly Func<string?> _banner;
    private readonly Func<BossStatus?> _boss;
    private readonly Func<DateTimeOffset?, double?> _bestOwnDps;
    private readonly Func<int?, NpcInfo?> _npcInfo;
    private uint? _bossTarget;
    private readonly Border _bossTrail = new() { RenderTransform = new ScaleTransform(0, 1), RenderTransformOrigin = new Point(0, 0.5), CornerRadius = new CornerRadius(3), Background = Frozen(Color.FromArgb(0xB0, 0xF2, 0xC2, 0x7A)) };
    private readonly Border _endCard = new()
    {
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(8, 4, 8, 4),
        Margin = new Thickness(0, 5, 0, 0),
        Background = Frozen(Color.FromArgb(0x50, 0x7B, 0x5C, 0xD6)),
        BorderBrush = Frozen(Color.FromArgb(0xA0, 0x7B, 0x5C, 0xD6)),
        BorderThickness = new Thickness(1),
        Visibility = Visibility.Collapsed,
    };
    private readonly TextBlock _endText = new() { TextWrapping = TextWrapping.Wrap };
    private DateTime _endCardUntil;
    private bool _wasActive;
    private bool _dotPulsing;
    private bool _bossBlinking;
    private readonly Grid _bossBar = new() { Margin = new Thickness(0, 5, 0, 0), ToolTip = "HP des Gegners, den du gerade am meisten triffst (Community-Hypothese 00 8D/41 36). Name aus der lokalen Community-Gegnerliste. * = noch nicht im Spiel bestätigt" };
    private readonly Border _bossFill = new() { RenderTransform = new ScaleTransform(0, 1), RenderTransformOrigin = new Point(0, 0.5), CornerRadius = new CornerRadius(3), Background = new LinearGradientBrush(Color.FromArgb(0xE0, 0xD0, 0x45, 0x45), Color.FromArgb(0xE0, 0x9A, 0x26, 0x26), 90) };
    private readonly TextBlock _bossName = new() { FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _bossValue = new() { Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
    private readonly StackPanel _bossCard = new();

    // Eigene Karte für den Boss: dunkler Hintergrund, abgerundet, dünner roter Rahmen (feste Farbe, nicht das Farbthema).
    private readonly Border _bossFrame = new()
    {
        Background = Frozen(Color.FromArgb(0xE6, 0x1A, 0x0C, 0x0E)),
        BorderBrush = Frozen(Color.FromRgb(0xC8, 0x3C, 0x3C)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(8, 2, 8, 8),
        Margin = new Thickness(0, 4, 0, 4),
    };
    private readonly TextBlock _bossStatus = new() { Foreground = DimBrush };
    private readonly TextBlock _bossHp = new() { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _bossTopHit = new() { HorizontalAlignment = HorizontalAlignment.Right, FontWeight = FontWeights.Bold, Foreground = Frozen(Color.FromRgb(0xF2, 0xC2, 0x6A)) };
    private readonly TextBlock _bossTopHitLabel = new() { Text = "TOP-TREFFER", HorizontalAlignment = HorizontalAlignment.Right, Foreground = DimBrush };
    private readonly TextBlock _bannerText = new() { Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };

    // Update-Hinweis (neue Version auf GitHub): anklickbar, startet nach Rückfrage das Update.
    private readonly TextBlock _updateText = new()
    {
        Margin = new Thickness(0, 3, 0, 0),
        TextTrimming = TextTrimming.CharacterEllipsis,
        Foreground = Brushes.Gold,
        FontWeight = FontWeights.SemiBold,
        Cursor = Cursors.Hand,
        Visibility = Visibility.Collapsed,
        ToolTip = "Neue Version auf GitHub – klicken, um sie (nach Rückfrage) zu installieren",
    };
    private Func<string?>? _update;
    private Action? _applyUpdate;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Border _panel = new() { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };

    /// <summary>Kräftige, schmale Zahlenschrift (Windows-Systemschrift Bahnschrift, Rückfall Segoe UI).</summary>
    private static readonly FontFamily NumberFont = new("Bahnschrift SemiBold Condensed, Bahnschrift, Segoe UI");
    private readonly Ellipse _dot = new() { Width = 8, Height = 8, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
    // Titel und Kampfzeit in EINER Textzeile (Runs): so stehen beide auf derselben Grundlinie, auch mit der schmalen
    // Zahlenschrift – vorher saß die Zeit sichtbar höher als „Kampf“.
    private readonly Run _title = new() { FontWeight = FontWeights.SemiBold };
    private readonly Run _elapsed = new();
    private readonly TextBlock _headerText = new() { VerticalAlignment = VerticalAlignment.Center, ToolTip = $"AionMeta {Aion2DpsMeter.Core.Updates.AppRelease.Current}" };
    private readonly TextBlock _modeButton;
    private readonly Grid _rowsHost = new() { ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _emptyText = new()
    {
        Foreground = DimBrush,
        HorizontalAlignment = HorizontalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 12, 0, 12),
    };
    private readonly TextBlock _moreText = new() { Foreground = DimBrush, Margin = new Thickness(2, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel _body = new() { Margin = new Thickness(0, 6, 0, 4) };
    private readonly DockPanel _footer = new() { LastChildFill = true };
    private readonly StackPanel _ownInfo = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _lockIcon = new();

    // Ping zum Spielserver (passiv gemessen): Signalbalken + „12 ms“, grün/gelb/rot.
    private readonly StackPanel _ping = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Ping zum Spielserver (passiv aus der Spielverbindung gemessen)" };
    private readonly Rectangle[] _pingBars = [new(), new(), new()];
    private readonly TextBlock _pingText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
    private Func<TimeSpan?>? _pingValue;

    // Zusatz-Od-Energie (blauer Wert, sicher aus dem Spiel): eigenes Kristall-Symbol + „+95“, links neben dem Ping.
    private readonly StackPanel _od = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Zusatz-Od-Energie (aus dem Spiel gelesen)" };
    private readonly TextBlock _odText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0), Foreground = Frozen(Color.FromRgb(0x5C, 0xC8, 0xFF)), FontWeight = FontWeights.SemiBold };
    private Func<Aion2DpsMeter.Core.Analysis.OdEnergy?>? _odValue;

    // Gruppen-Lobby: Mitglieder der Gruppenliste, solange kein Kampf läuft.
    private readonly StackPanel _lobby = new() { Margin = new Thickness(0, 2, 0, 4), Visibility = Visibility.Collapsed };
    private string? _lobbyKey;
    private readonly Dictionary<uint, RowView> _rows = new();
    private OverlaySettings _settings;
    private bool _compact;
    private bool _collapsed;
    private bool _animating;
    private string? _footerKey;
    private DateTimeOffset? _lastOwnFightAt; // für den Gruppenhinweis (nur im und kurz nach dem Kampf)
    private OverlayContent? _lastContent;

    /// <param name="meter">Live-Zähler.</param>
    /// <param name="settings">Gemerkte Einstellungen.</param>
    /// <param name="change">Ändert und speichert Einstellungen (über das Hauptfenster).</param>
    /// <param name="toggleLock">Schaltet „Klicks durchlassen“ um.</param>
    /// <param name="close">Blendet das Overlay aus (über das Hauptfenster).</param>
    /// <param name="openDetail">Öffnet die Details zu einem Spieler (Klick auf seine Zeile).</param>
    /// <param name="openSettings">Öffnet die Einstellungen (Zahnrad).</param>
    /// <param name="status">Meldung statt „Warte auf Kampf“ (z. B. „Warte auf AION 2 …“) oder null.</param>
    public OverlayWindow(
        LiveCombatMeter meter,
        OverlaySettings settings,
        Action<Func<OverlaySettings, OverlaySettings>> change,
        Action toggleLock,
        Action close,
        Action<uint> openDetail,
        Action openSettings,
        Action openHistory,
        Func<string?> status,
        Func<string?> banner,
        Func<BossStatus?> boss,
        Func<DateTimeOffset?, double?> bestOwnDps,
        Func<int?, NpcInfo?> npcInfo,
        Func<string?>? update = null,
        Action? applyUpdate = null,
        Func<TimeSpan?>? ping = null,
        Func<Aion2DpsMeter.Core.Analysis.OdEnergy?>? od = null)
    {
        _pingValue = ping;
        _odValue = od;
        _npcInfo = npcInfo;
        _update = update;
        _applyUpdate = applyUpdate;
        _banner = banner;
        _boss = boss;
        _bestOwnDps = bestOwnDps;
        _meter = meter;
        _change = change;
        _toggleLock = toggleLock;
        _openDetail = openDetail;
        _status = status;
        _settings = settings.Normalized();
        ThemeColors.Set(_settings.Theme);

        Title = "AionMeta";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = true;
        Icon = AppIcon.Window;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = _settings.Left;
        Top = _settings.Top;
        Width = _settings.Width;
        Height = _settings.Height;
        SizeToContent = SizeToContent.Height; // Höhe folgt der Zeilenzahl, Breite bleibt am Griff einstellbar
        Foreground = TextBrush;
        FontFamily = new FontFamily("Segoe UI");
        System.Windows.Documents.Typography.SetNumeralAlignment(this, FontNumeralAlignment.Tabular); // Ziffern gleich breit: kein Zittern beim Hochzählen

        // Kopfzeile: Statuspunkt, Titel, Kampfzeit, Knöpfe.
        // Klick: vorübergehend „Alle“ ↔ zurück zur eingestellten Ansicht (Standard Automatik). Wird nicht gespeichert,
        // damit man nicht versehentlich in einer Ansicht hängen bleibt (Gruppe fehlte sonst z. B. im Dungeon).
        _modeButton = IconButton("Ⓐ", "Ansicht", () =>
        {
            _viewOverride = EffectiveView == OverlaySettings.ViewAll ? null : OverlaySettings.ViewAll;
            if (_viewOverride is null && _settings.PlayerView == OverlaySettings.ViewAll) _viewOverride = OverlaySettings.ViewAuto;
            Refresh();
        });
        _modeButton.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            _viewOverride = null;
            Refresh();
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_modeButton);
        buttons.Children.Add(IconButton("⚙", "Einstellungen", openSettings));
        buttons.Children.Add(IconButton("☷", "Kämpfe (Verlauf)", openHistory));
        buttons.Children.Add(IconButton("⧉", "Ergebnis kopieren", () =>
        {
            if (_lastContent is { } c) TrySetClipboard(OverlayFormat.ToClipboardText(c));
        }));
        buttons.Children.Add(IconButton("↺", "Zurücksetzen", () =>
        {
            _meter.Reset();
            Refresh();
        }));
        buttons.Children.Add(IconButton("☰", "Kompakt (nur 3 Spieler)", () =>
        {
            _compact = !_compact;
            Refresh();
        }));
        buttons.Children.Add(IconButton("–", "Einklappen", () =>
        {
            _collapsed = !_collapsed;
            Refresh();
        }));
        buttons.Children.Add(IconButton("✕", "Schließen", close));

        var headerLine = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        headerLine.Children.Add(buttons);
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(_dot);
        _headerText.Inlines.Add(_title);
        _headerText.Inlines.Add(new Run("  "));
        _headerText.Inlines.Add(_elapsed);
        left.Children.Add(_headerText);
        headerLine.Children.Add(left);

        // Glas-Look: leicht durchscheinende, nach unten auslaufende Kopfzeile mit feinem Rand.
        var header = new Border
        {
            Child = headerLine,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 4, 6, 4),
            BorderThickness = new Thickness(1),
            BorderBrush = Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            Background = new LinearGradientBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x06, 0xFF, 0xFF, 0xFF), 90),
        };
        _elapsed.FontFamily = NumberFont;
        _endCard.Child = _endText;

        _body.Children.Add(_lobby);
        _body.Children.Add(_emptyText);
        _body.Children.Add(_rowsHost);
        _body.Children.Add(_moreText);

        // Fußzeile: eigener Charakter bzw. Hinweis; rechts die Sperre (Klicks durchlassen).
        _lockIcon.FontFamily = SymbolFont;
        _lockIcon.Cursor = Cursors.Hand;
        _lockIcon.VerticalAlignment = VerticalAlignment.Center;
        _lockIcon.Margin = new Thickness(8, 0, 0, 0);
        WireClick(_lockIcon, _toggleLock);

        // Deckkraft nur in den Einstellungen (Anzeige) – die Fußzeile bleibt schlicht.
        DockPanel.SetDock(_lockIcon, Dock.Right);
        _footer.Children.Add(_lockIcon);
        for (var i = 0; i < _pingBars.Length; i++)
        {
            _pingBars[i].Width = 3;
            _pingBars[i].Height = 4 + i * 3;
            _pingBars[i].RadiusX = 1;
            _pingBars[i].RadiusY = 1;
            _pingBars[i].Margin = new Thickness(i == 0 ? 0 : 1.5, 0, 0, 0);
            _pingBars[i].VerticalAlignment = VerticalAlignment.Bottom;
        }

        var bars = new StackPanel { Orientation = Orientation.Horizontal, Height = 10, VerticalAlignment = VerticalAlignment.Center };
        foreach (var bar in _pingBars) bars.Children.Add(bar);
        _ping.Children.Add(bars);
        _ping.Children.Add(_pingText);
        DockPanel.SetDock(_ping, Dock.Right);
        _footer.Children.Add(_ping);
        _od.Children.Add(OdIcon(10));
        _od.Children.Add(_odText);
        DockPanel.SetDock(_od, Dock.Right);
        _footer.Children.Add(_od);
        _footer.Children.Add(_ownInfo);

        var root = new DockPanel { Margin = new Thickness(10, 8, 10, 6) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_footer, Dock.Bottom);
        root.Children.Add(header);
        DockPanel.SetDock(_bannerText, Dock.Top);
        root.Children.Add(_bannerText);
        DockPanel.SetDock(_updateText, Dock.Top);
        root.Children.Add(_updateText);
        WireClick(_updateText, () => _applyUpdate?.Invoke());

        // Boss-Leiste: dünner roter Balken mit Name links und Werten rechts.
        // Volle Breite: dunkelrote Bahn = verlorene HP, hellrote Füllung = Rest-HP; Text liegt darauf.
        _bossBar.Children.Add(new Border
        {
            Background = Frozen(Color.FromArgb(0x55, 0x5A, 0x16, 0x16)),
            BorderBrush = Frozen(Color.FromArgb(0x90, 0xB8, 0x3A, 0x3A)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
        });
        _bossBar.Children.Add(_bossTrail); // heller Streifen, der verlorene HP langsam nachzieht
        _bossBar.Children.Add(_bossFill);
        // In der Leiste: aktuelle HP links, Prozent rechts.
        var bossLine = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(_bossValue, Dock.Right);
        bossLine.Children.Add(_bossValue);
        bossLine.Children.Add(_bossHp);
        _bossBar.Children.Add(bossLine);

        // Boss-Kopf darüber: großer Name und Status links, höchster Einzeltreffer rechts.
        var topHit = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        topHit.Children.Add(_bossTopHit);
        topHit.Children.Add(_bossTopHitLabel);
        var bossHead = new DockPanel { Margin = new Thickness(2, 6, 2, 0) };
        DockPanel.SetDock(topHit, Dock.Right);
        bossHead.Children.Add(topHit);
        var nameBlock = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        nameBlock.Children.Add(_bossName);
        nameBlock.Children.Add(_bossStatus);
        bossHead.Children.Add(nameBlock);
        _bossCard.Children.Add(bossHead);
        _bossCard.Children.Add(_bossBar);
        _bossFrame.Child = _bossCard;
        DockPanel.SetDock(_bossFrame, Dock.Top);
        root.Children.Add(_bossFrame);
        DockPanel.SetDock(_endCard, Dock.Top);
        root.Children.Add(_endCard);
        root.Children.Add(_footer);
        root.Children.Add(_body);
        _panel.Child = root;
        _panel.Margin = new Thickness(8); // Platz für den weichen Schatten
        Content = _panel;
        ApplyLook();

        // Verschieben per Ziehen (nur wenn Klicks nicht durchgelassen werden).
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };

        SourceInitialized += (_, _) => ApplyWindowStyles();
        _timer.Tick += (_, _) => Refresh();
        _rowsHost.SizeChanged += (_, _) => StartAnimation();
        Loaded += (_, _) =>
        {
            Refresh();
            _timer.Start();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            StopAnimation();
        };
    }

    private string? _viewOverride;

    /// <summary>Ansicht, die gerade gilt: vorübergehende Umschaltung über den Knopf, sonst die Einstellung.</summary>
    public string EffectiveView => _viewOverride ?? _settings.PlayerView;

    /// <summary>Aktuelle Einstellungen inkl. Position und Größe.</summary>
    public OverlaySettings CurrentSettings => _settings with { Left = Left, Top = Top, Width = ActualWidth > 0 ? ActualWidth : Width, Height = ActualHeight > 0 ? ActualHeight : Height };

    public void Apply(OverlaySettings settings)
    {
        var fontChanged = Math.Abs(settings.FontSize - _settings.FontSize) > 0.01;
        if (settings.PlayerView != _settings.PlayerView) _viewOverride = null; // in den Einstellungen neu gewählt
        if (ThemeColors.Set(settings.Theme)) fontChanged = true; // neues Farbthema: Zeilen neu aufbauen
        _settings = settings.Normalized();
        if (fontChanged)
        {
            _rows.Clear(); // Zeilenhöhe hängt an der Schrift: neu aufbauen
            _rowsHost.Children.Clear();
        }

        ApplyLook();
        ApplyWindowStyles();
        Refresh();
    }

    private double RowHeight => Math.Round(FontSize * 1.85);

    /// <summary>Schriftgröße der Nebenzeilen: bei hohem Kontrast nur 1 pt kleiner statt 2.</summary>
    private double SecondaryFontSize => Math.Max(9, FontSize - (_settings.Contrast == OverlayContrast.Normal ? 2 : 1));

    private static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private void ApplyLook()
    {
        var style = OverlayContrast.For(_settings.Contrast);
        DimBrush.Color = Rgb(style.Secondary);
        NameBrush.Color = Rgb(style.Name);
        var opacity = OverlayContrast.EffectiveOpacity(_settings.BackgroundOpacity, _settings.Contrast);
        var alpha = (byte)(opacity * 255);
        // Dunkler Rand um die Schrift: bleibt auch über hellem oder buntem Spielhintergrund lesbar.
        if (_panel.Child is UIElement content)
        {
            content.Effect = style.Shadow
                ? new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = 0.95, Color = Colors.Black }
                : null;
        }

        _panel.Background = new SolidColorBrush(Color.FromArgb(alpha, 0x12, 0x14, 0x1A));
        // Rand in der Akzentfarbe des Themas (gedämpft); Kampfende-Karte ebenso.
        var accent = ThemeColors.Accent;
        _panel.BorderBrush = new SolidColorBrush(Color.FromArgb((byte)Math.Max(alpha / 2, 0x50), accent.R, accent.G, accent.B));
        _endCard.Background = ThemeColors.AccentAlpha(0x50);
        _endCard.BorderBrush = ThemeColors.AccentAlpha(0xA0);
        _panel.Effect = _settings.Effects
            ? new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.55 * opacity, Color = Colors.Black }
            : null;
        FontSize = _settings.FontSize;
    }

    private void ApplyWindowStyles()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        // Taskleistenknopf (App-Fenster) statt Werkzeugfenster: AionMeta ist in der Taskleiste sichtbar, nimmt aber weiter keinen Fokus.
        var style = (GetWindowLong(handle, GwlExStyle) | WsExNoActivate | WsExAppWindow) & ~WsExToolWindow;
        style = _settings.ClickThrough ? style | WsExTransparent : style & ~WsExTransparent;
        SetWindowLong(handle, GwlExStyle, style);
    }

    private void Refresh()
    {
        // Nach Ziehen am Griff setzt WPF die Größe auf „manuell“ – Breite übernehmen, Höhe wieder dem Inhalt folgen lassen.
        if (SizeToContent != SizeToContent.Height)
        {
            Width = ActualWidth;
            SizeToContent = SizeToContent.Height;
        }

        var snapshot = _meter.Snapshot(DateTimeOffset.UtcNow);
        var view = EffectiveView;
        var ownOnly = view != OverlaySettings.ViewAll;
        var party = view == OverlaySettings.ViewAuto ? _meter.PartyMembers() : null;
        var content = OverlayFormat.Build(snapshot, _compact ? 3 : _settings.MaxRows, _settings.HideTinyRows, ownOnly, party, TimeSpan.FromSeconds(_settings.IdleSeconds),
            _settings.ShowCombatPower ? _meter.CombatPowerOf : null);
        _lastContent = content;

        var inParty = party is { Count: > 0 };
        (_modeButton.Text, var state) = view switch
        {
            OverlaySettings.ViewAll => ("👥", "Alle Spieler"),
            OverlaySettings.ViewOwn => ("👤", "Nur ich"),
            _ => ("Ⓐ", inParty ? "Automatik: Gruppe" : "Automatik: nur ich (keine Gruppe)"),
        };
        // Gruppe gemeldet, aber nicht alle Namen bekannt (z. B. Meter erst im Dungeon gestartet): ehrlich anzeigen.
        if (view == OverlaySettings.ViewAuto && _meter.PartySize is { } size && size > 1 && (party?.Count ?? 0) < size)
        {
            state += $"\nGruppe {party?.Count ?? 0}/{size} erkannt – fehlende Namen kommen beim Teleport bzw. wenn die Mitglieder ins Blickfeld kommen";
        }
        _modeButton.ToolTip = $"{state}\nKlicken: {(view == OverlaySettings.ViewAll ? "zurück zur eingestellten Ansicht" : "vorübergehend alle Spieler zeigen")}" +
                              "\n(Dauerhafte Ansicht: Einstellungen → Anzeige)";

        // Event-Zeile: laufender Alarm hervorgehoben, sonst das nächste Event gedämpft.
        var banner = _banner();
        _bannerText.Text = banner ?? string.Empty;
        _bannerText.Visibility = banner is null || _collapsed ? Visibility.Collapsed : Visibility.Visible;
        _bannerText.Foreground = banner is not null && (banner.StartsWith('⏰') || banner.StartsWith("🔒", StringComparison.Ordinal)) ? Brushes.Gold : DimBrush;
        _bannerText.FontSize = SecondaryFontSize;
        var update = _update?.Invoke();
        _updateText.Text = update ?? string.Empty;
        _updateText.FontSize = SecondaryFontSize;
        _updateText.Visibility = update is null || _collapsed ? Visibility.Collapsed : Visibility.Visible;

        UpdateBossBar();

        _dot.Fill = content.Active ? ActiveDot : IdleDot;
        UpdatePulse(content.Active && _settings.Effects);
        UpdateAutoHide(content.Active, banner);
        UpdateEndCard(snapshot, content.Active);
        _title.Text = content.Title;
        _elapsed.Text = content.Elapsed;
        _elapsed.Foreground = DimBrush;

        _body.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible;
        _footer.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible;

        if (content.Rows.Count == 0)
        {
            // Eigener Charakter unbekannt: nur der Teleport-Hinweis unten, kein Kampf und kein zweiter Hinweis hier.
            _emptyText.Text = _status() ?? "Warte auf Kampf";
            _emptyText.Visibility = Visibility.Visible;
        }
        else
        {
            _emptyText.Visibility = Visibility.Collapsed;
        }

        UpdateRows(content);
        UpdateLobby(content, snapshot);
        _moreText.FontSize = SecondaryFontSize;
        _moreText.ToolTip = null;
        // Im Modus „Nur ich“ nichts über andere Spieler zeigen; bei „Alle“ nur die über das Zeilenlimit hinaus.
        _moreText.Text = $"+ {content.HiddenRows} weitere";
        _moreText.Visibility = !content.OwnOnly && content.HiddenRows > 0 && content.Rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Automatik: Gruppe gemeldet, aber nicht jeder erkannt (Namen fehlen noch) → sichtbar sagen statt still nur mich zeigen.
        var nowUtc = DateTimeOffset.UtcNow;
        if (content.Active) _lastOwnFightAt = nowUtc;
        if (EffectiveView == OverlaySettings.ViewAuto && _meter.PartySize is { } partySize &&
            OverlayFormat.ShowPartyHint(_settings.ShowPartyHint, partySize, party?.Count ?? 0, content.Active, nowUtc - _lastOwnFightAt))
        {
            _moreText.Text = $"Gruppe {party?.Count ?? 0}/{partySize} erkannt";
            _moreText.ToolTip = "Fehlende Namen kommen beim Teleport bzw. wenn die Mitglieder ins Blickfeld kommen.";
            _moreText.Visibility = Visibility.Visible;
        }

        UpdateFooter(snapshot);
    }

    private DateTime _lastFightAt = DateTime.UtcNow;
    private bool _autoHidden;

    /// <summary>
    /// Automatisch ausblenden (Einstellung, Standard aus): nach der gewählten Zeit ohne eigenen/Gruppen-Kampf wird das
    /// Panel unsichtbar; beim nächsten Treffer oder einem Alarm kommt es wieder. Das Fenster bleibt bestehen (Hotkeys,
    /// Tray, Klicks-durchlassen wirken weiter); unsichtbare Flächen fangen keine Klicks ab.
    /// </summary>
    private void UpdateAutoHide(bool active, string? banner)
    {
        var now = DateTime.UtcNow;
        var alarm = banner is not null && (banner.StartsWith('⏰') || banner.StartsWith("🔒", StringComparison.Ordinal));
        if (active || alarm) _lastFightAt = now;
        var hide = _settings.ShouldAutoHide(now - _lastFightAt);
        if (hide == _autoHidden) return;
        _autoHidden = hide;
        _panel.IsHitTestVisible = !hide;
        if (_settings.Effects)
        {
            _panel.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(hide ? 0 : 1, TimeSpan.FromMilliseconds(hide ? 600 : 200)));
        }
        else
        {
            _panel.BeginAnimation(OpacityProperty, null);
            _panel.Opacity = hide ? 0 : 1;
        }
    }

    /// <summary>Grüner Punkt pulsiert, solange gekämpft wird.</summary>
    private void UpdatePulse(bool on)
    {
        if (on == _dotPulsing) return;
        _dotPulsing = on;
        _dot.BeginAnimation(OpacityProperty, on
            ? new System.Windows.Media.Animation.DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(700))
            {
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
            }
            : null);
    }

    /// <summary>Beim Kampfende 8 s eine Karte mit Dauer, DPS, Anteil, Platz und Vergleich zum Bestwert.</summary>
    private void UpdateEndCard(LiveCombatSnapshot snapshot, bool active)
    {
        var now = DateTime.UtcNow;
        if (_settings.ShowFightEndCard && _wasActive && !active && OverlayFormat.FightEndText(snapshot, _bestOwnDps(snapshot.Start), _lastContent is { OwnOnly: true } c ? c.Rows.Select(r => r.Actor).ToHashSet() : null) is { } text)
        {
            _endText.Text = "🏁 Kampf beendet · " + text;
            _endText.FontSize = Math.Max(9, FontSize - 1);
            _endCardUntil = now.AddSeconds(8);
            _endCard.Visibility = Visibility.Visible;
            if (_settings.Effects)
            {
                _endCard.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
            }
        }

        if (active || now > _endCardUntil || _collapsed) _endCard.Visibility = Visibility.Collapsed;
        _wasActive = active;
    }

    private bool IsBoss(BossStatus boss) => OverlayFormat.IsBoss(boss, _npcInfo(boss.NpcCode));

    private void UpdateBossBar()
    {
        var boss = _settings.ShowBossBar && !_collapsed ? _boss() : null;
        // Nur bei Bossen (Dungeon-/Weltbosse): laut Gegnerliste „Boss“ (ohne Trainingspuppen), oder mit Zeitlimit vom Spiel.
        // Ohne Eintrag in der Liste gilt ein Gegner ab 5 Mio. Max-HP als Boss.
        if (boss is not null && !IsBoss(boss)) boss = null;
        _bossFrame.Visibility = boss is null ? Visibility.Collapsed : Visibility.Visible;
        if (boss is null) return;

        _bossBar.Height = Math.Round(FontSize * 1.7);
        _bossBar.Margin = new Thickness(0, 4, 0, 0);
        _bossName.FontSize = FontSize + 3;
        _bossStatus.FontSize = _bossTopHitLabel.FontSize = Math.Max(8, FontSize - 3);
        _bossTopHit.FontSize = FontSize + 3;
        _bossTopHit.FontFamily = NumberFont;
        _bossHp.FontSize = _bossValue.FontSize = Math.Max(9, FontSize - 1);
        _bossHp.FontFamily = _bossValue.FontFamily = NumberFont;

        // Name aus der lokalen Community-Gegnerliste (über die Gegner-Nummer beim Erscheinen), sonst die Kennung.
        var npc = _npcInfo(boss.NpcCode);
        _bossName.Text = npc is null
            ? $"Gegner #{boss.Target}"
            : npc.Name + (npc.IsDummy ? " (Puppe)" : string.Empty);
        _bossStatus.Text = OverlayFormat.BossStatusText(boss) + " *";

        // Berserk: nur automatisch aus der Deadline des Spiels (21 8D).
        var now = DateTimeOffset.UtcNow;
        TimeSpan? left = null;
        string? berserkText = null;
        if (boss.Hp > 0 && boss.Deadline is { } deadline)
        {
            left = deadline - now;
            berserkText = OverlayFormat.BerserkTextUntil(deadline, now);
        }

        if (berserkText is not null) _bossStatus.Text += " · " + berserkText;
        _bossStatus.Foreground = left is { } l && l < TimeSpan.FromSeconds(30) ? Brushes.OrangeRed : DimBrush;
        _bossHp.Text = OverlayFormat.BossHpText(boss);
        _bossValue.Text = boss.Fraction is { } f ? (f * 100).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("de-DE")) + " %" : string.Empty;
        var showTopHit = false; // Top-Treffer auf Wunsch des Besitzers ausgeblendet
        _bossTopHit.Text = showTopHit ? boss.TopHit!.Value.ToString("#,0", System.Globalization.CultureInfo.GetCultureInfo("de-DE")) : string.Empty;
        _bossTopHit.Visibility = _bossTopHitLabel.Visibility = showTopHit ? Visibility.Visible : Visibility.Collapsed;
        // Füllung und Nachzieh-Streifen über die volle Breite, skaliert auf den Rest-Anteil (unabhängig von der Pixelbreite).
        var target = Math.Clamp(boss.Fraction ?? 0, 0, 1);
        var fill = (ScaleTransform)_bossFill.RenderTransform;
        var trail = (ScaleTransform)_bossTrail.RenderTransform;
        if (boss.Target != _bossTarget || !_settings.Effects)
        {
            // Neuer Gegner (oder Effekte aus): sofort, ohne Nachziehen.
            fill.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            trail.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            fill.ScaleX = target;
            trail.ScaleX = target;
        }
        else if (Math.Abs(fill.ScaleX - target) > 0.001)
        {
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            fill.BeginAnimation(ScaleTransform.ScaleXProperty, new System.Windows.Media.Animation.DoubleAnimation(target, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            // Heller Streifen zieht verlorene HP verzögert nach (bei Heilung sofort mit).
            var trailFrom = trail.ScaleX;
            trail.BeginAnimation(ScaleTransform.ScaleXProperty, target < trailFrom
                ? new System.Windows.Media.Animation.DoubleAnimation(trailFrom, target, TimeSpan.FromMilliseconds(700)) { BeginTime = TimeSpan.FromMilliseconds(350), EasingFunction = ease }
                : new System.Windows.Media.Animation.DoubleAnimation(target, TimeSpan.FromMilliseconds(200)));
        }

        // Unter 20 % HP pulsiert die Füllung.
        var blink = _settings.Effects && boss.Fraction is < 0.2 and > 0;
        if (blink != _bossBlinking)
        {
            _bossBlinking = blink;
            _bossFill.BeginAnimation(OpacityProperty, blink
                ? new System.Windows.Media.Animation.DoubleAnimation(1, 0.45, TimeSpan.FromMilliseconds(450))
                {
                    AutoReverse = true,
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                }
                : null);
        }

        _bossTarget = boss.Target;
    }

    /// <summary>Zeilen bleiben bestehen (je Kennung) und gleiten zu Position, Balkenbreite und Zahlenwerten.</summary>
    private void UpdateRows(OverlayContent content)
    {
        var now = DateTime.UtcNow;
        var height = RowHeight;
        var maxShare = content.Rows.Count > 0 ? Math.Max(0.0001, content.Rows.Max(r => r.ShareValue)) : 1;
        var seen = new HashSet<uint>();

        for (var i = 0; i < content.Rows.Count; i++)
        {
            var row = content.Rows[i];
            seen.Add(row.Actor);
            if (!_rows.TryGetValue(row.Actor, out var view))
            {
                view = new RowView(this, row.Actor, height, i * (height + 3));
                _rows[row.Actor] = view;
                _rowsHost.Children.Add(view.Root);
            }

            view.Update(row, row.ShareValue / maxShare, i * (height + 3), now);
        }

        foreach (var gone in _rows.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _rowsHost.Children.Remove(_rows[gone].Root);
            _rows.Remove(gone);
        }

        _rowsHost.Height = Math.Max(0, content.Rows.Count * (height + 3) - 3);
        StartAnimation();
    }

    private void StartAnimation()
    {
        if (_animating) return;
        _animating = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopAnimation()
    {
        if (!_animating) return;
        _animating = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    /// <summary>Ein Bild: alle Zeilen einen Schritt weiter; ist nichts mehr in Bewegung, endet die Schleife.</summary>
    private void OnRendering(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var width = _rowsHost.ActualWidth;
        var moving = false;
        foreach (var view in _rows.Values) moving |= view.Render(now, width);
        if (!moving) StopAnimation();
    }

    private static readonly Brush[] PingBrushes = [Frozen(Color.FromRgb(0x4C, 0xD9, 0x64)), Frozen(Color.FromRgb(0xF2, 0xC1, 0x4E)), Frozen(Color.FromRgb(0xE8, 0x4C, 0x4C))];
    private static readonly Brush PingOff = Frozen(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF));

    /// <summary>Zusatz-Od-Energie „+95“; ausgeblendet, solange das Spiel den Wert noch nicht geschickt hat.</summary>
    private void UpdateOd()
    {
        if (!_settings.ShowOd)
        {
            _od.Visibility = Visibility.Collapsed;
            return;
        }

        _od.Visibility = Visibility.Visible;
        _odText.FontSize = Math.Max(9, FontSize - 2);
        if (_odValue?.Invoke() is { } odEnergy)
        {
            // Zusammengerechnet wie gewünscht: aufladender Wert + Zusatzwert; die Aufteilung steht im Tooltip.
            var german = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            _od.Opacity = 1;
            _odText.Text = _settings.OdTotal || odEnergy.Bonus == 0
                ? odEnergy.Total.ToString("#,0", german)
                : $"{odEnergy.Current.ToString("#,0", german)} (+{odEnergy.Bonus.ToString("#,0", german)})";
            _od.ToolTip = $"Od-Energie {odEnergy.Total.ToString("#,0", german)} = {odEnergy.Current.ToString("#,0", german)} (lädt alle 3 h auf) + {odEnergy.Bonus.ToString("#,0", german)} Zusatz";
        }
        else
        {
            // Noch kein Wert: dezent ausgegraut statt einer erfundenen Zahl.
            _od.Opacity = 0.45;
            _odText.Text = "–";
            _od.ToolTip = "Od-Energie: wird beim Einloggen oder beim Einlösen von Zusatz-Energie erkannt (AionMeta vor dem Spiel starten).";
        }
    }

    /// <summary>Selbst gezeichnetes Od-Symbol: blauer Kristall mit hellem Kern.</summary>
    private static FrameworkElement OdIcon(double size)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(new Path
        {
            Data = Geometry.Parse("M12,1 L19,9 L12,23 L5,9 Z"),
            Fill = new LinearGradientBrush(Color.FromRgb(0x9A, 0xE4, 0xFF), Color.FromRgb(0x2E, 0x7C, 0xE6), 90),
            Stroke = Frozen(Color.FromRgb(0xD8, 0xF4, 0xFF)),
            StrokeThickness = 1.2,
        });
        canvas.Children.Add(new Path { Data = Geometry.Parse("M12,4 L16,9 L12,17 L8,9 Z"), Fill = Frozen(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)) });
        return new Viewbox { Width = size, Height = size + 2, Child = canvas, VerticalAlignment = VerticalAlignment.Center };
    }

    private DateTimeOffset _lobbyChangedAt;

    /// <summary>
    /// Gruppen-Lobby: Mitglieder mit Klasse (falls bekannt), Stufe, GS und KS. Nur ohne laufenden Kampf – und wenn schon
    /// Kampfzeilen da sind, nur in der ersten Minute nach einer Änderung der Gruppe (Beitritt/Austritt).
    /// </summary>
    private void UpdateLobby(OverlayContent content, LiveCombatSnapshot snapshot)
    {
        var members = _settings.ShowLobby && !_collapsed ? _meter.LobbyMembers() : [];
        var key = string.Join("|", members.Select(m => $"{m.Name}:{m.Level}:{m.GearScore}:{m.CombatPower}"));
        var now = DateTimeOffset.UtcNow;
        if (key != _lobbyKey)
        {
            _lobbyKey = key;
            _lobbyChangedAt = now;
            _lobby.Children.Clear();
            if (members.Count >= 2) BuildLobby(members, snapshot);
        }

        var show = members.Count >= 2 && !content.Active && (content.Rows.Count == 0 || now - _lobbyChangedAt < TimeSpan.FromMinutes(1));
        _lobby.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildLobby(IReadOnlyList<Aion2DpsMeter.Core.Analysis.RosterMember> members, LiveCombatSnapshot snapshot)
    {
        var german = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
        var size = _meter.PartySize is { } s && s >= members.Count ? s : members.Count;
        _lobby.Children.Add(new TextBlock
        {
            Text = $"Gruppe · {members.Count}/{size}",
            FontWeight = FontWeights.SemiBold,
            Foreground = AccentBrush,
            FontSize = SecondaryFontSize,
            Margin = new Thickness(2, 0, 0, 3),
        });

        foreach (var m in members)
        {
            var className = snapshot.Actors.FirstOrDefault(a => a.Name == m.Name)?.ClassName;
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1), Height = RowHeight * 0.85 };
            var card = new Border { CornerRadius = new CornerRadius(4), Background = Frozen(Color.FromArgb(0x30, 0, 0, 0)), Padding = new Thickness(4, 0, 6, 0), Child = row };
            var icon = ClassIcons.Create(className ?? string.Empty, RowHeight * 0.62);
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            var numbers = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            numbers.Children.Add(Number($"St. {m.Level}", 40));
            numbers.Children.Add(Number(m.GearScore > 0 ? $"{m.GearScore.ToString("#,0", german)} GS" : "– GS", 62));
            numbers.Children.Add(Number(m.CombatPower > 0 ? $"{OverlayFormat.Compact(m.CombatPower)} KS" : "– KS", 62));
            DockPanel.SetDock(numbers, Dock.Right);
            row.Children.Add(numbers);
            row.Children.Add(new TextBlock { Text = m.Name, FontWeight = FontWeights.SemiBold, Foreground = NameBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            _lobby.Children.Add(card);
        }

        TextBlock Number(string text, double width) => new()
        {
            Text = text,
            Foreground = DimBrush,
            FontSize = SecondaryFontSize,
            TextAlignment = TextAlignment.Right,
            MinWidth = width * FontSize / 13,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>Ping-Anzeige: Balken und Zahl in Grün (gut), Gelb (mittel) oder Rot (schlecht); ohne Messung ausgeblendet.</summary>
    private void UpdatePing()
    {
        if (!_settings.ShowPing || _pingValue?.Invoke() is not { } ping)
        {
            _ping.Visibility = Visibility.Collapsed;
            return;
        }

        var level = Aion2DpsMeter.Core.Analysis.PingEstimator.Level(ping);
        _ping.Visibility = Visibility.Visible;
        _pingText.Text = $"{Math.Max(1, Math.Round(ping.TotalMilliseconds)):0} ms";
        _pingText.Foreground = PingBrushes[level];
        _pingText.FontSize = Math.Max(9, FontSize - 2);
        for (var i = 0; i < _pingBars.Length; i++) _pingBars[i].Fill = i < 3 - level ? PingBrushes[level] : PingOff;
    }
    private void UpdateFooter(LiveCombatSnapshot snapshot)
    {
        UpdatePing();
        UpdateOd();
        _lockIcon.Text = _settings.ClickThrough ? "🔒" : "🔓";
        _lockIcon.ToolTip = _settings.ClickThrough
            ? "Klicks gehen ans Spiel – ausschalten mit Strg+Umschalt+K oder Doppelklick aufs Tray-Symbol"
            : "Klicks durchlassen einschalten";
        _lockIcon.Foreground = DimBrush;

        // Nur neu aufbauen, wenn sich etwas geändert hat (nicht 10× pro Sekunde).
        var own = _meter.OwnActor;
        // Klasse auch ohne Treffer im aktuellen Kampf: aus früheren Treffern bzw. gemerkt (kein „?“ im Leerlauf).
        var className = own is { } id ? snapshot.Actors.FirstOrDefault(a => a.Actor == id)?.ClassName ?? _meter.ClassNameOf(id) : null;
        var key = $"{own}|{className}|{(own is { } o ? _meter.NameOf(o) : null)}|{FontSize}";
        if (key == _footerKey) return;
        _footerKey = key;

        _ownInfo.Children.Clear();
        if (own is { } ownId)
        {
            _ownInfo.Children.Add(ClassIcons.Create(className ?? string.Empty, FontSize * 1.3));
            _ownInfo.Children.Add(new TextBlock { Text = _meter.NameOf(ownId) ?? $"#{ownId}", Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        }
        else
        {
            _ownInfo.Children.Add(new TextBlock
            {
                Text = "Teleportiere dich irgendwohin, um erkannt zu werden",
                Foreground = DimBrush,
                FontSize = Math.Max(9, FontSize - 2),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
    }

    private static TextBlock IconButton(string symbol, string tooltip, Action action)
    {
        // Hover: weich eingeblendeter Hintergrund in der Akzentfarbe, Symbol wird hell und etwas größer.
        var hoverBg = new SolidColorBrush(Colors.Transparent);
        var scale = new ScaleTransform(1, 1);
        var button = new TextBlock
        {
            Text = symbol,
            FontFamily = SymbolFont,
            Foreground = DimBrush,
            Background = hoverBg,
            Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(2, 0, 0, 0),
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = scale,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };

        void Animate(bool over)
        {
            var time = TimeSpan.FromMilliseconds(over ? 120 : 200);
            var ease = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            var accent = ThemeColors.Accent;
            hoverBg.BeginAnimation(SolidColorBrush.ColorProperty, new System.Windows.Media.Animation.ColorAnimation(
                over ? Color.FromArgb(0x55, accent.R, accent.G, accent.B) : Colors.Transparent, time) { EasingFunction = ease });
            var size = new System.Windows.Media.Animation.DoubleAnimation(over ? 1.15 : 1, time) { EasingFunction = ease };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, size);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, size);
            button.Foreground = over ? TextBrush : DimBrush;
        }

        button.MouseEnter += (_, _) => Animate(true);
        button.MouseLeave += (_, _) => Animate(false);
        WireClick(button, action);
        return button;
    }

    private static void WireClick(UIElement element, Action action)
    {
        // Mausdruck abfangen, damit das Fenster beim Klick auf einen Knopf nicht verschoben wird.
        element.MouseLeftButtonDown += (_, e) => e.Handled = true;
        element.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            action();
        };
    }

    private static void TrySetClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (COMException)
        {
            // Zwischenablage gerade belegt – dann eben nicht.
        }
    }

    private static Brush ClassBrush(string className, byte alpha)
    {
        var color = ClassIcons.ColorOf(className);
        return Frozen(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Wert, der in <see cref="Duration"/> mit Abbremsen (ease-out) zum Ziel gleitet.</summary>
    private struct Tween
    {
        public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(250);

        private double _from;
        private double _to;
        private DateTime _start;

        public Tween(double value)
        {
            _from = _to = value;
            _start = DateTime.MinValue;
        }

        public readonly double Target => _to;

        public readonly double ValueAt(DateTime now)
        {
            var t = (now - _start).TotalMilliseconds / Duration.TotalMilliseconds;
            if (t >= 1) return _to;
            if (t <= 0) return _from;
            var eased = 1 - Math.Pow(1 - t, 3);
            return _from + (_to - _from) * eased;
        }

        public readonly bool IsMoving(DateTime now) => (now - _start) < Duration && _from != _to;

        public void Set(double target, DateTime now)
        {
            if (target == _to) return;
            _from = ValueAt(now);
            _to = target;
            _start = now;
        }
    }

    /// <summary>Eine Spielerzeile: Balken in Klassenfarbe, Icon, Name, „x,xxK/s“, Gesamtschaden, Anteil.</summary>
    private sealed class RowView
    {
        private readonly OverlayWindow _owner;
        private readonly Border _fill = new() { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(4) };
        private readonly ContentControl _icon = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _name = new() { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        private readonly Border _badge;
        private readonly StackPanel _deaths = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Tode in diesem Kampf" };
        private readonly TextBlock _deathsText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0), FontWeight = FontWeights.SemiBold, Foreground = Frozen(Color.FromRgb(0xF0, 0x9A, 0x9A)) };
        private readonly StackPanel _power = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Kampfkraft (Community-Hypothese, noch nicht mit dem Spiel abgeglichen)" };
        private readonly TextBlock _powerText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0), Foreground = Frozen(Color.FromRgb(0xB8, 0xE6, 0xDC)) };
        private readonly TextBlock _dps = new() { TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontFamily = NumberFont };
        private readonly SolidColorBrush _dpsBrush = new(Color.FromRgb(0xF2, 0xF4, 0xF8));
        private readonly Border _glow = new() { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), IsHitTestVisible = false };
        private long _lastDamage;
        private readonly TextBlock _damage = new() { Foreground = DimBrush, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _share = new() { Foreground = DimBrush, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        private readonly TranslateTransform _move = new();
        private Tween _y;
        private Tween _fraction = new(0);
        private Tween _dpsValue = new(0);
        private Tween _damageValue = new(0);
        private string? _iconKey;
        private string? _brushKey;

        public RowView(OverlayWindow owner, uint actor, double height, double y)
        {
            _owner = owner;
            _y = new Tween(y);
            var size = owner.FontSize;

            Root = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Top, RenderTransform = _move, Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = "Klicken: Details zu diesem Spieler" };
            Root.Children.Add(new Border { Background = Frozen(Color.FromArgb(0x30, 0, 0, 0)), CornerRadius = new CornerRadius(4) });
            Root.Children.Add(_fill);
            // Leichter Glanz auf der oberen Hälfte des Balkens.
            _fill.Child = new Border
            {
                VerticalAlignment = VerticalAlignment.Top,
                Height = height / 2,
                CornerRadius = new CornerRadius(4, 4, 0, 0),
                Background = new LinearGradientBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 90),
            };
            Root.Children.Add(_glow);
            _dps.Foreground = _dpsBrush;
            _dps.FontSize = size + 2;

            _badge = new Border
            {
                Background = AccentBrush,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 0, 4, 0),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = "ICH", FontSize = Math.Max(8, size - 4), FontWeight = FontWeights.Bold },
            };
            _damage.FontSize = Math.Max(9, size - 1);
            _share.FontSize = Math.Max(9, size - 1);
            _dps.MinWidth = size * 4.6;
            _damage.MinWidth = size * 3.4;
            _share.MinWidth = size * 3.6;

            var line = new DockPanel { Margin = new Thickness(4, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, LastChildFill = true };
            DockPanel.SetDock(_icon, Dock.Left);
            line.Children.Add(_icon);
            var numbers = new StackPanel { Orientation = Orientation.Horizontal };
            numbers.Children.Add(_dps);
            numbers.Children.Add(_damage);
            numbers.Children.Add(_share);
            DockPanel.SetDock(numbers, Dock.Right);
            line.Children.Add(numbers);
            var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(_name);
            name.Children.Add(_badge);
            _power.Children.Add(ClassIcons.CreateCombatPowerIcon(Math.Max(10, size)));
            _powerText.FontSize = Math.Max(8, size - 2);
            _power.Children.Add(_powerText);
            name.Children.Add(_power);
            _deaths.Children.Add(ClassIcons.SkullVector(Math.Max(10, size)));
            _deathsText.FontSize = Math.Max(8, size - 2);
            _deaths.Children.Add(_deathsText);
            name.Children.Add(_deaths);
            line.Children.Add(name);
            Root.Children.Add(line);

            WireClick(Root, () => owner._openDetail(actor));
        }

        public Grid Root { get; }

        public void Update(OverlayRow row, double fraction, double y, DateTime now)
        {
            _name.Text = (row.MaybeOwn ? "? " : "") + row.Name;
            _name.Foreground = row.MaybeOwn ? Brushes.Khaki : NameBrush;
            _badge.Visibility = row.IsOwn ? Visibility.Visible : Visibility.Collapsed;
            _deaths.Visibility = row.Deaths > 0 ? Visibility.Visible : Visibility.Collapsed;
            _deathsText.Text = row.Deaths.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _power.Visibility = row.CombatPower is > 0 ? Visibility.Visible : Visibility.Collapsed;
            _powerText.Text = row.CombatPower is { } cp ? OverlayFormat.Compact(cp) : string.Empty;
            _share.Text = row.Share;

            var iconKey = $"{row.Class}|{row.IsLikelyPet}";
            if (iconKey != _iconKey)
            {
                _iconKey = iconKey;
                _icon.Content = ClassIcons.Create(row.Class, _owner.RowHeight * 0.72, row.IsLikelyPet);
            }

            var brushKey = $"{row.Class}|{row.IsOwn}";
            if (brushKey != _brushKey)
            {
                _brushKey = brushKey;
                // Farbverlauf in Klassenfarbe (eigene Zeile violett): oben heller, unten dunkler.
                var color = ClassIcons.ColorOf(row.Class); // auch die eigene Zeile in Klassenfarbe
                var brush = new LinearGradientBrush(
                    Color.FromArgb(0xC8, Lighten(color.R), Lighten(color.G), Lighten(color.B)),
                    Color.FromArgb(0x90, (byte)(color.R * 0.7), (byte)(color.G * 0.7), (byte)(color.B * 0.7)),
                    90);
                brush.Freeze();
                _fill.Background = brush;
                // Eigene Zeile: dezente Leuchtkante.
                // Eigene Zeile: Leuchtkante in heller Klassenfarbe (plus „ICH“-Schild).
                _glow.BorderBrush = row.IsOwn ? Frozen(Color.FromArgb(0xE0, Lighten(Lighten(color.R)), Lighten(Lighten(color.G)), Lighten(Lighten(color.B)))) : Brushes.Transparent;
                _glow.BorderThickness = new Thickness(row.IsOwn ? 1.5 : 0);
            }

            // Großer Treffer: Zuwachs seit der letzten Aktualisierung deutlich über dem Durchschnittstreffer → kurz gelb.
            var average = row.Hits > 0 ? (double)row.DamageValue / row.Hits : 0;
            if (_owner._settings.Effects && _lastDamage > 0 && average > 0 && row.DamageValue - _lastDamage > 2.5 * average)
            {
                _dpsBrush.BeginAnimation(SolidColorBrush.ColorProperty,
                    new System.Windows.Media.Animation.ColorAnimation(Color.FromRgb(0xFF, 0xD5, 0x4A), Color.FromRgb(0xF2, 0xF4, 0xF8), TimeSpan.FromMilliseconds(350)));
            }

            _lastDamage = row.DamageValue;

            _y.Set(y, now);
            _fraction.Set(Math.Clamp(fraction, 0, 1), now);
            _dpsValue.Set(row.DpsValue, now);
            _damageValue.Set(row.DamageValue, now);
        }

        private static byte Lighten(byte value) => (byte)Math.Min(255, value + (255 - value) * 0.25);

        /// <summary>Zeichnet den aktuellen Zwischenstand; true, solange noch etwas gleitet.</summary>
        public bool Render(DateTime now, double width)
        {
            _move.Y = _y.ValueAt(now);
            _fill.Width = Math.Max(0, width * _fraction.ValueAt(now));
            _dps.Text = OverlayFormat.PerSecond(_dpsValue.ValueAt(now));
            _damage.Text = OverlayFormat.Compact((long)Math.Round(_damageValue.ValueAt(now)));
            return _y.IsMoving(now) || _fraction.IsMoving(now) || _dpsValue.IsMoving(now) || _damageValue.IsMoving(now);
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
