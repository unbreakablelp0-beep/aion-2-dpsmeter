using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.App;

/// <summary>
/// Details zu einem Spieler im aktuellen Kampf: Kopf mit Gesamtschaden, Ziel-Auswahl und Tabelle pro Fähigkeit
/// (Schaden, Anteil, Treffer, Krit-Quote, Max, Durchschnitt). Fähigkeiten tragen nur Nummern; eigene Namen
/// per Doppelklick (lokal gespeichert). Aktualisiert sich jede Sekunde.
/// </summary>
public sealed class PlayerDetailWindow : Window
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    // Farben wie im Overlay (dunkles Panel, Rahmen, gedämpfter Text).
    private static readonly Brush Bg = Frozen(Color.FromRgb(0x10, 0x12, 0x18));
    private static readonly Brush CardBg = Frozen(Color.FromRgb(0x16, 0x18, 0x1F));
    private static readonly Brush CardBorder = Frozen(Color.FromRgb(0x2C, 0x30, 0x3B));
    private static readonly Brush TextBrush = Frozen(Color.FromRgb(0xE6, 0xE8, 0xEE));
    private static readonly Brush DimBrush = Frozen(Color.FromRgb(0x9A, 0xA0, 0xAE));
    private static readonly Brush DefaultBarBrush = Frozen(Color.FromArgb(0x70, 0x7B, 0x5C, 0xD6));
    private Brush BarBrush = DefaultBarBrush;
    private static readonly Brush DotBarBrush = Frozen(Color.FromArgb(0x70, 0xD0, 0x8A, 0x3A));
    private static readonly Brush PetBarBrush = Frozen(Color.FromArgb(0x70, 0x3E, 0xA6, 0xA0));

    private readonly Func<uint, uint?, ActorDetail?> _detail;
    private readonly Func<uint, string?> _nameOf;
    private readonly uint _actor;
    private readonly SkillNameStore _skillNames;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TextBlock _name = new() { FontSize = 18, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _className = new() { Foreground = DimBrush, FontSize = 12 };
    private readonly TextBlock _total = new() { FontSize = 26, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
    private readonly WrapPanel _tiles = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly ComboBox _targetBox = new() { MinWidth = 220, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _rows = new();
    private readonly ContentControl _icon = new() { VerticalAlignment = VerticalAlignment.Center };
    private string? _iconClass = "\0";
    private readonly ContentControl _headerRow = new();
    private readonly TextBlock _communityLine = new() { Foreground = DimBrush, FontSize = 12, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _community = new()
    {
        Content = "Community-Spalten (Parade, Perfekt, Doppelt, Front/Rücken, Multi) – noch nicht abgeglichen",
        IsChecked = true,
        Foreground = DimBrush,
        Margin = new Thickness(0, 0, 0, 6),
    };
    private bool _updatingTargets;

    /// <summary>Live: Details aus dem laufenden Kampf (aktualisiert sich).</summary>
    public PlayerDetailWindow(LiveCombatMeter meter, uint actor, SkillNameStore skillNames)
        : this((a, t) => meter.Detail(a, t, DateTimeOffset.UtcNow), meter.NameOf, actor, skillNames, live: true, meter.DamagePerSecond)
    {
    }

    /// <summary>Gespeicherter Kampf aus dem Verlauf.</summary>
    public PlayerDetailWindow(FightRecord fight, uint actor, SkillNameStore skillNames)
        : this(fight.Detail, a => fight.Players.FirstOrDefault(p => p.Actor == a)?.Name, actor, skillNames, live: false,
            a => fight.Players.FirstOrDefault(p => p.Actor == a)?.DamagePerSecond)
    {
        Title = $"Spieler-Details – {fight.Name}";
    }

    private readonly Func<uint, IReadOnlyList<long>?> _series;
    private readonly DpsChart _chart = new();
    private readonly Border _chartCard;

    private PlayerDetailWindow(Func<uint, uint?, ActorDetail?> detail, Func<uint, string?> nameOf, uint actor, SkillNameStore skillNames, bool live,
        Func<uint, IReadOnlyList<long>?> series)
    {
        _series = series;
        _detail = detail;
        _nameOf = nameOf;
        _actor = actor;
        _skillNames = skillNames;

        Title = "Spieler-Details";
        Width = 900;
        Height = 600;
        Background = Bg;
        DarkTheme.Apply(this);
        Foreground = TextBrush;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;

        _targetBox.SelectionChanged += (_, _) =>
        {
            if (!_updatingTargets) Refresh();
        };

        // Kopf-Karte: Klassen-Icon, Name, Klasse, Gesamtschaden; darunter Kennzahlen-Kacheln.
        var nameBlock = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        nameBlock.Children.Add(_name);
        nameBlock.Children.Add(_className);
        var headTop = new DockPanel();
        DockPanel.SetDock(_icon, Dock.Left);
        DockPanel.SetDock(_total, Dock.Right);
        headTop.Children.Add(_icon);
        headTop.Children.Add(_total);
        headTop.Children.Add(nameBlock);
        var headContent = new StackPanel();
        headContent.Children.Add(headTop);
        headContent.Children.Add(_tiles);
        headContent.Children.Add(_communityLine);
        _communityLine.Margin = new Thickness(0, 8, 0, 0);

        var controls = new DockPanel { Margin = new Thickness(0, 10, 0, 6) };
        DockPanel.SetDock(_targetBox, Dock.Left);
        _targetBox.Margin = new Thickness(0, 0, 14, 0);
        _community.VerticalAlignment = VerticalAlignment.Center;
        _community.Margin = new Thickness(0);
        controls.Children.Add(_targetBox);
        controls.Children.Add(_community);

        var header = new StackPanel();
        header.Children.Add(Card(headContent));
        // DPS-Verlauf (geglättet über 5 s), darunter die Fähigkeiten.
        _chartCard = Card(_chart);
        _chartCard.Margin = new Thickness(0, 8, 0, 0);
        header.Children.Add(_chartCard);
        header.Children.Add(controls);
        _community.Click += (_, _) => Refresh();

        var hint = new TextBlock
        {
            Text = "Fähigkeiten tragen nur Nummern aus den Daten – Doppelklick auf eine Zeile vergibt einen eigenen Namen. " +
                   $"Die Namen stehen in {_skillNames.Path} (von Hand änderbar). " +
                   (_skillNames.Community.Count > 0 ? "Namen mit * stammen aus der lokalen Community-Liste skills-community.json (nicht abgeglichen). " : "") +
                   "„über Zeit“ = Schaden über Zeit, „Pet“ = Schaden von Beschwörungen. * = laut Community, nicht abgeglichen.",
            Foreground = DimBrush,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 8, 2, 0),
        };

        var table = new DockPanel();
        DockPanel.SetDock(_headerRow, Dock.Top);
        table.Children.Add(_headerRow);
        table.Children.Add(new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(hint, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(hint);
        root.Children.Add(Card(table));
        Content = root;

        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            Refresh();
            if (live) _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    private uint? SelectedTarget => _targetBox.SelectedItem is ComboBoxItem { Tag: uint t } ? t : null;

    private void Refresh()
    {
        var detail = _detail(_actor, SelectedTarget);
        if (detail is null)
        {
            _name.Text = _nameOf(_actor) ?? $"#{_actor}";
            _className.Text = string.Empty;
            _total.Text = "–";
            _tiles.Children.Clear();
            _tiles.Children.Add(new TextBlock { Text = "In diesem Kampf (noch) kein Schaden.", Foreground = DimBrush });
            _rows.Children.Clear();
            return;
        }

        _name.Text = detail.Name ?? $"(Name unbekannt) #{detail.Actor}";
        _className.Text = detail.ClassName ?? "Klasse unbekannt";
        if (_iconClass != detail.ClassName)
        {
            _iconClass = detail.ClassName;
            _icon.Content = ClassIcons.Create(detail.ClassName, 40);
            BarBrush = detail.ClassName is not null ? Frozen(WithAlpha(ClassIcons.ColorOf(detail.ClassName), 0x70)) : DefaultBarBrush;
        }

        _total.Text = OverlayFormat.Compact(detail.Damage);
        _total.ToolTip = detail.Damage.ToString("#,0", German) + " Schaden";

        var activeDps = detail.ActiveDpsIfMeaningful;
        _tiles.Children.Clear();
        _tiles.Children.Add(Tile("DPS", OverlayFormat.Compact((long)detail.Dps), "Schaden ÷ Kampfzeit"));
        _tiles.Children.Add(Tile("DPS (aktiv)", OverlayFormat.ActiveDps(activeDps),
            activeDps is null
                ? $"Erst ab {ActorDetail.MinActiveTime.TotalSeconds:0} s aktiver Zeit – vorher nicht aussagekräftig"
                : "Schaden ÷ aktive Zeit (Pausen ab 5 s zählen nicht, eigene Annahme)"));
        _tiles.Children.Add(Tile("Kampfzeit", OverlayFormat.Elapsed(detail.Duration), null));
        _tiles.Children.Add(Tile("aktiv", OverlayFormat.ActiveTime(detail.ActiveTime), "Zeit zwischen den eigenen Treffern; Pausen ab 5 s zählen nicht"));
        _tiles.Children.Add(Tile("Zauber", detail.Hits.ToString("#,0", German), null));
        _tiles.Children.Add(Tile("Treffer*", detail.HitsWithExtra.ToString("#,0", German), "Zauber + Zusatztreffer laut Community, nicht abgeglichen"));
        _tiles.Children.Add(Tile("Krit", (detail.CriticalRate * 100).ToString("0.0", German) + " %", null));
        UpdateTargets(detail);

        // DPS-Kurve: nur wenn Daten vorhanden (ältere gespeicherte Kämpfe haben keine).
        if (_series(_actor) is { Count: > 1 } perSecond)
        {
            _chartCard.Visibility = Visibility.Visible;
            var color = detail.ClassName is not null ? ClassIcons.ColorOf(detail.ClassName) : Color.FromRgb(0x7B, 0x5C, 0xD6);
            _chart.Update(DpsTimeline.Smooth(perSecond), Color.FromRgb(Lift(color.R), Lift(color.G), Lift(color.B)));
        }
        else
        {
            _chartCard.Visibility = Visibility.Collapsed;
        }

        var community = _community.IsChecked == true;
        _headerRow.Content = HeaderRow(community);
        _communityLine.Visibility = community && detail.Flags is not null ? Visibility.Visible : Visibility.Collapsed;
        if (detail.Flags is { } f)
        {
            _communityLine.Text = string.Create(German,
                $"Parade {f.ParryRate * 100:0.0} % · Rücken {f.BackRate * 100:0.0} % · Front {f.FrontRate * 100:0.0} % · Perfekt {f.PerfectRate * 100:0.0} % · Doppelt {f.DoubleRate * 100:0.0} % · Multi {f.MultiRate * 100:0.0} %   (laut Community, nicht abgeglichen)");
        }

        _rows.Children.Clear();
        var max = detail.Skills.Count > 0 ? Math.Max(1, detail.Skills.Max(s => s.Damage)) : 1;
        foreach (var skill in detail.Skills) _rows.Children.Add(SkillRow(skill, (double)skill.Damage / max));
    }

    private void UpdateTargets(ActorDetail detail)
    {
        var selected = SelectedTarget;
        var labels = detail.Targets.Select(t => $"{t.Name ?? $"Ziel #{t.Target}"} – {OverlayFormat.Compact(t.Damage)}").ToList();
        var shown = _targetBox.Items.Cast<ComboBoxItem>().Skip(1).Select(i => i.Tag).OfType<uint>().ToList();
        if (shown.SequenceEqual(detail.Targets.Select(t => t.Target)) && _targetBox.Items.Count > 0)
        {
            // Nur Beschriftungen auffrischen, Auswahl bleibt.
            for (var i = 0; i < labels.Count; i++) ((ComboBoxItem)_targetBox.Items[i + 1]).Content = labels[i];
            return;
        }

        _updatingTargets = true;
        _targetBox.Items.Clear();
        _targetBox.Items.Add(new ComboBoxItem { Content = "Alle Ziele", Tag = null });
        for (var i = 0; i < detail.Targets.Count; i++)
        {
            _targetBox.Items.Add(new ComboBoxItem { Content = labels[i], Tag = detail.Targets[i].Target });
        }

        _targetBox.SelectedItem = _targetBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Tag is uint t && t == selected) ?? _targetBox.Items[0];
        _updatingTargets = false;
    }

    // Fähigkeit (Rest), Schaden, %, Treffer, Krit, Max, Ø – dazu optional Multi, Parade, Perfekt, Doppelt.
    private static readonly double[] Columns = [90, 55, 55, 55, 75, 75];
    private static readonly double[] CommunityColumns = [55, 60, 60, 60];

    private static Grid ColumnGrid(bool community)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var w in community ? Columns.Concat(CommunityColumns) : Columns)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
        }

        return grid;
    }

    private static FrameworkElement HeaderRow(bool community)
    {
        var grid = ColumnGrid(community);
        string[] titles = community
            ? ["Fähigkeit", "Schaden", "%", "Treffer", "Krit", "Max", "Ø", "Multi*", "Parade*", "Perfekt*", "Doppelt*"]
            : ["Fähigkeit", "Schaden", "%", "Treffer", "Krit", "Max", "Ø"];
        for (var i = 0; i < titles.Length; i++) Add(grid, i, titles[i], DimBrush, i == 0 ? TextAlignment.Left : TextAlignment.Right, FontWeights.SemiBold);
        grid.Margin = new Thickness(6, 4, 6, 4);
        if (community) grid.ToolTip = "* laut Community-Beschreibung, noch nicht mit dem Spiel abgeglichen";
        return grid;
    }

    private FrameworkElement SkillRow(SkillStatistics s, double fraction)
    {
        var cell = new Grid { Margin = new Thickness(0, 0, 0, 3), Cursor = Cursors.Hand, ToolTip = "Doppelklick: eigenen Namen vergeben" };
        var bar = new Grid();
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Clamp(fraction, 0, 1), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - Math.Clamp(fraction, 0, 1), GridUnitType.Star) });
        var fill = s.Kind switch { HitKind.OverTime => DotBarBrush, HitKind.Pet => PetBarBrush, _ => BarBrush };
        bar.Children.Add(new Border { Background = fill, CornerRadius = new CornerRadius(3) });
        cell.Children.Add(new Border { Background = Frozen(Color.FromArgb(0x30, 0, 0, 0)), CornerRadius = new CornerRadius(3) });
        cell.Children.Add(bar);

        var name = _skillNames.DisplayName(s.Skill, out var fromCommunity);
        var suffix = s.Kind switch { HitKind.OverTime => "  (über Zeit)", HitKind.Pet => "  (Pet)", _ => "" };
        var community = _community.IsChecked == true;
        var grid = ColumnGrid(community);
        grid.Margin = new Thickness(6, 3, 6, 3);
        Add(grid, 0, name + suffix, TextBrush, TextAlignment.Left, FontWeights.SemiBold);
        Add(grid, 1, OverlayFormat.Compact(s.Damage), TextBrush, TextAlignment.Right, FontWeights.Normal);
        Add(grid, 2, (s.Share * 100).ToString("0.0", German), DimBrush, TextAlignment.Right, FontWeights.Normal);
        Add(grid, 3, s.Hits.ToString("#,0", German), DimBrush, TextAlignment.Right, FontWeights.Normal);
        Add(grid, 4, s.Kind == HitKind.OverTime ? "–" : (s.CriticalRate * 100).ToString("0", German) + " %", DimBrush, TextAlignment.Right, FontWeights.Normal);
        Add(grid, 5, OverlayFormat.Compact(s.Max), DimBrush, TextAlignment.Right, FontWeights.Normal);
        Add(grid, 6, OverlayFormat.Compact((long)Math.Round((double)s.Average)), DimBrush, TextAlignment.Right, FontWeights.Normal);
        cell.ToolTip = $"#{s.Skill} – Doppelklick: eigenen Namen vergeben\nSchaden {s.Damage.ToString("#,0", German)} · Max {s.Max.ToString("#,0", German)}" +
                       (fromCommunity ? "\n* Name aus der lokalen Community-Liste, nicht abgeglichen" : "");
        if (community)
        {
            var f = s.Flags;
            string P(double? rate) => rate is { } r ? (r * 100).ToString("0", German) + " %" : "–";
            var direct = s.Kind != HitKind.OverTime;
            Add(grid, 7, direct ? P(f?.MultiRate) : "–", DimBrush, TextAlignment.Right, FontWeights.Normal);
            Add(grid, 8, f is { WithModifier: > 0 } ? P(f.ParryRate) : "–", DimBrush, TextAlignment.Right, FontWeights.Normal);
            Add(grid, 9, f is { WithModifier: > 0 } ? P(f.PerfectRate) : "–", DimBrush, TextAlignment.Right, FontWeights.Normal);
            Add(grid, 10, f is { WithModifier: > 0 } ? P(f.DoubleRate) : "–", DimBrush, TextAlignment.Right, FontWeights.Normal);
        }

        cell.Children.Add(grid);

        cell.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) Rename(s.Skill);
        };
        return cell;
    }

    private void Rename(uint skill)
    {
        var box = new TextBox { Text = _skillNames.NameOf(skill) ?? string.Empty, Margin = new Thickness(0, 6, 0, 10), MinWidth = 260 };
        var ok = new Button { Content = "Speichern", IsDefault = true, Padding = new Thickness(12, 3, 12, 3) };
        var dialog = new Window
        {
            Title = $"Name für Fähigkeit #{skill}",
            Owner = this,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(14),
                Children =
                {
                    new TextBlock { Text = "Eigener Name (leer = Nummer anzeigen):" },
                    box,
                    ok,
                },
            },
        };
        DarkTheme.ApplyDialog(dialog);
        ok.Click += (_, _) => dialog.DialogResult = true;
        box.Focus();
        if (dialog.ShowDialog() != true) return;

        try
        {
            if (!_skillNames.SetName(skill, box.Text))
            {
                MessageBox.Show(this, $"{_skillNames.Path} stammt nicht von diesem Programm und wird nicht überschrieben.", "Nicht gespeichert");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Nicht gespeichert");
        }

        Refresh();
    }

    private static void Add(Grid grid, int column, string text, Brush brush, TextAlignment alignment, FontWeight weight)
    {
        var block = new TextBlock { Text = text, Foreground = brush, TextAlignment = alignment, FontWeight = weight, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    /// <summary>Abgerundete dunkle Karte wie das Overlay-Panel.</summary>
    private static Border Card(UIElement child) => new()
    {
        Background = CardBg,
        BorderBrush = CardBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(12, 10, 12, 10),
        Child = child,
    };

    /// <summary>Kleine Kennzahl-Kachel: Wert groß, Beschriftung klein darunter.</summary>
    private static Border Tile(string label, string value, string? tooltip)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = value, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = TextBrush });
        panel.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = DimBrush });
        return new Border
        {
            Background = Frozen(Color.FromArgb(0x30, 0, 0, 0)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5, 12, 5),
            Margin = new Thickness(0, 0, 8, 6),
            MinWidth = 78,
            Child = panel,
            ToolTip = tooltip,
        };
    }

    private static byte Lift(byte value) => (byte)Math.Min(255, value + (255 - value) * 0.3);

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
