using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.App;

/// <summary>
/// Verlauf der gespeicherten Kämpfe (nur lokal): Suche nach Bezeichnung oder Spieler, Karten mit Name, eigenem
/// Charakter, Datum, Dauer, Gesamtschaden und Spielerzahl. Klick öffnet die Spieler des Kampfs, ✎ benennt um,
/// 🗑 löscht nach Rückfrage.
/// </summary>
public sealed class FightHistoryWindow : Window
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private static readonly Brush Bg = Frozen(Color.FromRgb(0x16, 0x18, 0x1F));
    private static readonly Brush CardBg = Frozen(Color.FromRgb(0x1F, 0x22, 0x2B));
    private static readonly Brush CardHover = Frozen(Color.FromRgb(0x2A, 0x2E, 0x3A));
    private static readonly Brush TextBrush = Frozen(Color.FromRgb(0xE6, 0xE8, 0xEE));
    private static readonly Brush DimBrush = Frozen(Color.FromRgb(0x9A, 0xA0, 0xAE));

    private readonly FightHistoryStore _store;
    private readonly SkillNameStore _skillNames;
    private readonly TextBox _search = new() { Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(6, 4, 6, 4) };
    private readonly StackPanel _list = new();
    private readonly TextBlock _empty = new() { Foreground = DimBrush, Margin = new Thickness(0, 20, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };

    public FightHistoryWindow(FightHistoryStore store, SkillNameStore skillNames)
    {
        _store = store;
        _skillNames = skillNames;

        Title = "Kämpfe";
        Width = 640;
        Height = 560;
        Background = Bg;
        DarkTheme.Apply(this);
        Foreground = TextBrush;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;

        _search.ToolTip = "Bezeichnung oder Spieler";
        _search.TextChanged += (_, _) => Refresh();

        var header = new StackPanel();
        var titleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        var stats = new Button { Content = "📊 Wochenstatistik", Padding = new Thickness(12, 3, 12, 3), VerticalAlignment = VerticalAlignment.Center };
        stats.Click += (_, _) => new WeeklyStatsWindow(_store).Show();
        DockPanel.SetDock(stats, Dock.Right);
        titleRow.Children.Add(stats);
        titleRow.Children.Add(new TextBlock { Text = "Kämpfe", FontSize = 20, FontWeight = FontWeights.SemiBold });
        header.Children.Add(titleRow);
        header.Children.Add(new TextBlock
        {
            Text = "Nur auf diesem PC gespeichert (Dokumente\\Aion2DpsMeter\\Kaempfe). Suche nach Bezeichnung oder Spieler:",
            Foreground = DimBrush,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6),
        });
        header.Children.Add(_search);

        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var body = new StackPanel();
        body.Children.Add(_list);
        body.Children.Add(_empty);
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;

        Loaded += (_, _) => Refresh();
        Activated += (_, _) => Refresh(); // neue Kämpfe erscheinen beim Zurückkehren ins Fenster
    }

    private void Refresh()
    {
        var entries = _store.List().Where(e => e.Fight.Matches(_search.Text)).ToList();
        _list.Children.Clear();
        foreach (var entry in entries) _list.Children.Add(Card(entry));
        _empty.Text = entries.Count == 0
            ? string.IsNullOrWhiteSpace(_search.Text) ? "Noch keine Kämpfe gespeichert. Ein Kampf wird gespeichert, sobald er beendet ist." : "Keine Treffer für die Suche."
            : string.Empty;
    }

    private FrameworkElement Card(FightHistoryEntry entry)
    {
        var fight = entry.Fight;
        var card = new Border { Background = CardBg, CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 6), Cursor = Cursors.Hand };
        card.MouseEnter += (_, _) => card.Background = CardHover;
        card.MouseLeave += (_, _) => card.Background = CardBg;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = fight.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        left.Children.Add(new TextBlock
        {
            Text = $"{fight.OwnName ?? "eigener Charakter unbekannt"} · {fight.Start.ToLocalTime():dd.MM.yyyy HH:mm:ss}",
            Foreground = DimBrush,
            FontSize = 12,
        });
        grid.Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(Stat(OverlayFormat.Elapsed(fight.Duration), "Dauer"));
        right.Children.Add(Stat(OverlayFormat.Compact(fight.TotalDamage), "Schaden"));
        right.Children.Add(Stat(fight.Players.Count.ToString(German), "Spieler"));
        right.Children.Add(IconAction("✎", "Umbenennen", () => Rename(entry)));
        right.Children.Add(IconAction("🗑", "Löschen", () => Delete(entry)));
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        card.Child = grid;
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (e.Handled) return;
            new FightPlayersWindow(fight, _skillNames) { Owner = this }.Show();
        };
        return card;
    }

    private static FrameworkElement Stat(string value, string label)
    {
        var panel = new StackPanel { Margin = new Thickness(14, 0, 0, 0), MinWidth = 54 };
        panel.Children.Add(new TextBlock { Text = value, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Right });
        panel.Children.Add(new TextBlock { Text = label, Foreground = DimBrush, FontSize = 11, TextAlignment = TextAlignment.Right });
        return panel;
    }

    private static TextBlock IconAction(string symbol, string tooltip, Action action)
    {
        var icon = new TextBlock
        {
            Text = symbol,
            FontFamily = new FontFamily("Segoe UI Symbol"),
            Foreground = DimBrush,
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = tooltip,
            Cursor = Cursors.Hand,
        };
        icon.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true; // nicht zusätzlich die Karte öffnen
            action();
        };
        return icon;
    }

    private void Rename(FightHistoryEntry entry)
    {
        var box = new TextBox { Text = entry.Fight.Name, MinWidth = 300, Margin = new Thickness(0, 6, 0, 10) };
        var ok = new Button { Content = "Speichern", IsDefault = true, Padding = new Thickness(12, 3, 12, 3) };
        var dialog = new Window
        {
            Title = "Kampf umbenennen",
            Owner = this,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(14), Children = { new TextBlock { Text = "Bezeichnung:" }, box, ok } },
        };
        DarkTheme.ApplyDialog(dialog);
        ok.Click += (_, _) => dialog.DialogResult = true;
        box.Focus();
        if (dialog.ShowDialog() != true) return;

        try
        {
            if (!_store.Rename(entry.Path, box.Text)) MessageBox.Show(this, "Die Datei stammt nicht von diesem Programm oder fehlt – nichts geändert.", "Nicht umbenannt");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Nicht umbenannt");
        }

        Refresh();
    }

    private void Delete(FightHistoryEntry entry)
    {
        var answer = MessageBox.Show(this, $"„{entry.Fight.Name}“ wirklich löschen?\n\nDie Datei wird endgültig entfernt:\n{entry.Path}",
            "Kampf löschen", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            if (!_store.Delete(entry.Path)) MessageBox.Show(this, "Die Datei stammt nicht von diesem Programm oder fehlt – nichts gelöscht.", "Nicht gelöscht");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Nicht gelöscht");
        }

        Refresh();
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

/// <summary>Spieler eines gespeicherten Kampfs; Klick öffnet die Details (mit Zielauswahl).</summary>
public sealed class FightPlayersWindow : Window
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public FightPlayersWindow(FightRecord fight, SkillNameStore skillNames)
    {
        Title = fight.Name;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 700;
        Background = new SolidColorBrush(Color.FromRgb(0x16, 0x18, 0x1F));
        Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE8, 0xEE));
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        DarkTheme.Apply(this);

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = fight.Name, FontSize = 18, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = string.Create(German, $"{fight.Start.ToLocalTime():dd.MM.yyyy HH:mm:ss} · {OverlayFormat.Elapsed(fight.Duration)} · gesamt {fight.TotalDamage:#,0}"),
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 10),
        });

        var total = Math.Max(1, fight.TotalDamage);
        foreach (var p in fight.Players)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4), Cursor = Cursors.Hand, ToolTip = "Klicken: Details" };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            var name = (p.IsOwn ? "► " : "") + (p.Name ?? $"(Name unbekannt) #{p.Actor}") + (p.ClassName is { } c ? $"  ·  {c}" : "");
            row.Children.Add(ClassIcons.Create(p.ClassName, 20));
            Add(row, 1, name, TextAlignment.Left, p.IsOwn ? FontWeights.Bold : FontWeights.SemiBold);
            Add(row, 2, p.Damage.ToString("#,0", German), TextAlignment.Right, FontWeights.Normal);
            Add(row, 3, OverlayFormat.Compact((long)p.Dps) + " DPS", TextAlignment.Right, FontWeights.Normal);
            Add(row, 4, ((double)p.Damage / total * 100).ToString("0.0", German) + " %", TextAlignment.Right, FontWeights.Normal);
            row.Background = Brushes.Transparent;
            var actor = p.Actor;
            row.MouseLeftButtonUp += (_, _) => new PlayerDetailWindow(fight, actor, skillNames) { Owner = this }.Show();
            panel.Children.Add(row);
        }

        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static void Add(Grid grid, int column, string text, TextAlignment alignment, FontWeight weight)
    {
        var block = new TextBlock { Text = text, TextAlignment = alignment, FontWeight = weight, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }
}
