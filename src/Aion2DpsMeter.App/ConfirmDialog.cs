using System.Windows;
using System.Windows.Controls;

namespace Aion2DpsMeter.App;

/// <summary>Rückfrage im Stil der übrigen Fenster (eigene Kopfzeile statt Windows-Meldungsfenster).</summary>
public sealed class ConfirmDialog : Window
{
    private ConfirmDialog(string title, string text, string yes, string? no)
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        DarkTheme.ApplyDialog(this);

        var page = new StackPanel { Margin = new Thickness(18, 14, 18, 16) };
        page.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 20 });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = yes, Padding = new Thickness(16, 6, 16, 6), IsDefault = true };
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(ok);
        if (no is not null)
        {
            var cancel = new Button { Content = no, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            buttons.Children.Add(cancel);
        }

        page.Children.Add(buttons);
        Content = page;
    }

    /// <summary>Ja/Nein-Frage; true bei „Ja“.</summary>
    public static bool Ask(Window? owner, string title, string text, string yes = "Ja", string no = "Nein")
    {
        var dialog = new ConfirmDialog(title, text, yes, no);
        if (owner is { IsVisible: true }) dialog.Owner = owner;
        return dialog.ShowDialog() == true;
    }

    /// <summary>Nur ein Hinweis mit „OK“.</summary>
    public static void Inform(Window? owner, string title, string text)
    {
        var dialog = new ConfirmDialog(title, text, "OK", null);
        if (owner is { IsVisible: true }) dialog.Owner = owner;
        dialog.ShowDialog();
    }
}
