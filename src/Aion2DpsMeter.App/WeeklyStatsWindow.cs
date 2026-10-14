using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.App;

/// <summary>
/// Wochenstatistik aus den lokal gespeicherten Kämpfen: Kämpfe und Boss-Kämpfe je Tag, eigene Durchschnitts-DPS als
/// Kurve über die letzten 7 Tage, beste Kämpfe je Boss. Nur lokal, nichts wird gesendet.
/// </summary>
public sealed class WeeklyStatsWindow : Window
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private static readonly Brush Bg = Frozen(Color.FromRgb(0x10, 0x12, 0x18));
    private static readonly Brush CardBg = Frozen(Color.FromRgb(0x18, 0x1B, 0x23));
    private static readonly Brush CardBorder = Frozen(Color.FromRgb(0x2A, 0x2E, 0x3A));
    private static readonly Brush TextBrush = Frozen(Color.FromRgb(0xE6, 0xE8, 0xEE));
    private static readonly Brush DimBrush = Frozen(Color.FromRgb(0x9A, 0xA0, 0xAE));
    private static Brush AccentBrush => ThemeColors.AccentBrush;

    public WeeklyStatsWindow(FightHistoryStore store)
    {
        Title = "Wochenstatistik";
        Width = 720;
        Height = 640;
        Background = Bg;
        DarkTheme.Apply(this);
        Foreground = TextBrush;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;

        var fights = store.List().Select(e => e.Fight).ToList();
        var days = FightStatistics.LastSevenDays(fights, DateTimeOffset.Now);
        var week = fights.Where(f => f.Start >= DateTimeOffset.Now.AddDays(-7)).ToList();
        var weekDps = week.Where(f => f.Duration >= FightStatistics.MinDurationForBest).Select(FightStatistics.OwnDps).OfType<double>().ToList();

        var page = new StackPanel { Margin = new Thickness(20) };
        page.Children.Add(new TextBlock { Text = "Wochenstatistik", FontSize = 22, FontWeight = FontWeights.SemiBold });
        page.Children.Add(new TextBlock { Text = "Letzte 7 Tage aus deinen gespeicherten Kämpfen (nur auf diesem PC).", Foreground = DimBrush, Margin = new Thickness(0, 2, 0, 12) });

        var tiles = new WrapPanel();
        tiles.Children.Add(Tile("Kämpfe", week.Count.ToString(German)));
        tiles.Children.Add(Tile("Boss-Kämpfe", week.Count(f => f.BossName is not null).ToString(German)));
        tiles.Children.Add(Tile("Ø eigene DPS", weekDps.Count == 0 ? "–" : OverlayFormat.Compact((long)weekDps.Average())));
        tiles.Children.Add(Tile("Beste DPS", weekDps.Count == 0 ? "–" : OverlayFormat.Compact((long)weekDps.Max())));
        page.Children.Add(tiles);

        // Kurve: eigene Ø-DPS je Tag (Tage ohne Wert als 0).
        var chart = new DpsChart
        {
            PeakCaption = "Bester Tag",
            LabelOf = i => days[i].Day.ToString("ddd dd.MM.", German),
        };
        chart.Update(days.Select(d => d.AverageOwnDps ?? 0).ToList(), Color.FromRgb(0xA8, 0x8C, 0xFF));
        page.Children.Add(Section("Eigene Ø-DPS je Tag"));
        page.Children.Add(Card(chart));

        page.Children.Add(Section("Je Tag"));
        var dayList = new StackPanel();
        foreach (var d in days.Reverse())
        {
            dayList.Children.Add(Row(
                d.Day.ToString("dddd, dd.MM.", German),
                $"{d.Fights} Kämpfe · {d.BossFights} Boss",
                d.AverageOwnDps is { } avg ? "Ø " + OverlayFormat.PerSecond(avg) : "–"));
        }

        page.Children.Add(Card(dayList));

        page.Children.Add(Section("Beste Kämpfe je Boss"));
        var best = FightStatistics.BestPerBoss(fights);
        var bestList = new StackPanel();
        if (best.Count == 0)
        {
            bestList.Children.Add(new TextBlock
            {
                Text = "Noch keine Boss-Kämpfe gespeichert. Ab v36 merkt sich der Meter bei jedem Kampf den Boss aus der Boss-Leiste.",
                Foreground = DimBrush,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        foreach (var (boss, fight, dps) in best.Take(10))
        {
            bestList.Children.Add(Row(boss, fight.Start.ToLocalTime().ToString("dd.MM. HH:mm", German) + " · " + OverlayFormat.Elapsed(fight.Duration), OverlayFormat.PerSecond(dps)));
        }

        page.Children.Add(Card(bestList));
        Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static FrameworkElement Section(string title) => new TextBlock
    {
        Text = title.ToUpper(German),
        Foreground = AccentBrush,
        FontWeight = FontWeights.Bold,
        FontSize = 11.5,
        Margin = new Thickness(0, 16, 0, 6),
    };

    private static Border Card(UIElement child) => new()
    {
        Child = child,
        Background = CardBg,
        BorderBrush = CardBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(14, 10, 14, 10),
    };

    private static Border Tile(string label, string value)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = label, Foreground = DimBrush, FontSize = 11 });
        var tile = Card(panel);
        tile.Margin = new Thickness(0, 0, 8, 8);
        tile.MinWidth = 140;
        return tile;
    }

    private static FrameworkElement Row(string left, string middle, string right)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.Children.Add(new TextBlock { Text = left, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var mid = new TextBlock { Text = middle, Foreground = DimBrush, Margin = new Thickness(12, 0, 12, 0) };
        Grid.SetColumn(mid, 1);
        grid.Children.Add(mid);
        var r = new TextBlock { Text = right, TextAlignment = TextAlignment.Right };
        Grid.SetColumn(r, 2);
        grid.Children.Add(r);
        return grid;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
