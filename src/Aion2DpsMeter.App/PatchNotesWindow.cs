using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Aion2DpsMeter.Core.Updates;

namespace Aion2DpsMeter.App;

/// <summary>„Was ist neu“: Patchnotes aus der eingebetteten CHANGELOG.md, im Stil der übrigen Fenster.</summary>
public sealed class PatchNotesWindow : Window
{
    private static readonly Brush Bg = Frozen(Color.FromRgb(0x10, 0x12, 0x18));
    private static readonly Brush CardBg = Frozen(Color.FromRgb(0x18, 0x1B, 0x23));
    private static readonly Brush CardBorder = Frozen(Color.FromRgb(0x2A, 0x2E, 0x3A));
    private static readonly Brush TextBrush = Frozen(Color.FromRgb(0xE6, 0xE8, 0xEE));
    private static readonly Brush DimBrush = Frozen(Color.FromRgb(0xB4, 0xBA, 0xC6));

    /// <summary>Alle Patchnotes aus der eingebetteten CHANGELOG.md (leer, wenn sie fehlt).</summary>
    public static IReadOnlyList<PatchNote> All { get; } = Load();

    private static IReadOnlyList<PatchNote> Load()
    {
        using var stream = typeof(PatchNotesWindow).Assembly.GetManifestResourceStream("CHANGELOG.md");
        if (stream is null) return [];
        using var reader = new StreamReader(stream);
        return PatchNotes.Parse(reader.ReadToEnd());
    }

    public PatchNotesWindow(IReadOnlyList<PatchNote> notes, bool afterUpdate)
    {
        Title = "AionMeta – Was ist neu";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 640;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Bg;
        DarkTheme.Apply(this);
        Foreground = TextBrush;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;

        var page = new StackPanel { Margin = new Thickness(20) };
        page.Children.Add(new TextBlock
        {
            Text = afterUpdate ? $"Neu in Version {AppRelease.Current}" : "Was ist neu",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
        });
        page.Children.Add(new TextBlock
        {
            Text = afterUpdate ? "AionMeta wurde aktualisiert. Das hat sich geändert:" : $"Du hast Version {AppRelease.Current}.",
            Foreground = DimBrush,
            Margin = new Thickness(0, 2, 0, 12),
        });

        foreach (var note in notes)
        {
            var card = new StackPanel();
            card.Children.Add(new TextBlock { Text = note.Title, FontWeight = FontWeights.SemiBold, Foreground = ThemeColors.AccentBrush, Margin = new Thickness(0, 0, 0, 6) });
            foreach (var item in note.Items)
            {
                var line = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                var bullet = new TextBlock { Text = "•", Foreground = ThemeColors.AccentBrush, Margin = new Thickness(0, 0, 8, 0) };
                DockPanel.SetDock(bullet, Dock.Left);
                line.Children.Add(bullet);
                line.Children.Add(new TextBlock { Text = item, TextWrapping = TextWrapping.Wrap });
                card.Children.Add(line);
            }

            page.Children.Add(new Border
            {
                Background = CardBg,
                BorderBrush = CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 10),
                Margin = new Thickness(0, 0, 0, 10),
                Child = card,
            });
        }

        if (notes.Count == 0) page.Children.Add(new TextBlock { Text = "Keine Patchnotes vorhanden.", Foreground = DimBrush });

        var ok = new Button { Content = "Alles klar", Padding = new Thickness(18, 6, 18, 6), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0), IsDefault = true };
        ok.Click += (_, _) => Close();
        page.Children.Add(ok);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
