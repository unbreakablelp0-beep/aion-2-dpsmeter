using System.Windows;
using System.Windows.Controls;
using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.App;

/// <summary>
/// Einmalige Frage zum Teilen für die Bestenliste. Zeigt genau, was gesendet wird. Esc oder Schließen = „Nein“.
/// </summary>
public sealed class ShareConsentDialog : Window
{
    private string _choice = ShareModes.Off;

    private ShareConsentDialog()
    {
        Title = "AionMeta – Bestenliste";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        DarkTheme.ApplyDialog(this);

        var page = new StackPanel { Margin = new Thickness(18, 14, 18, 16) };
        page.Children.Add(new TextBlock { Text = "Möchtest du deine Bosskämpfe für die Bestenliste teilen?", FontWeight = FontWeights.SemiBold, FontSize = 15, TextWrapping = TextWrapping.Wrap });
        page.Children.Add(new TextBlock
        {
            Text = "Nach jedem Bosskampf sendet AionMeta dann eine kurze Zusammenfassung an die Bestenliste auf der AionMeta-Webseite:\n" +
                   "• Boss, Kampfdauer, dein Schaden und deine DPS\n" +
                   "• deine Klasse und die AionMeta-Version\n" +
                   "• dein Charaktername – nur, wenn du „mit meinem Namen“ wählst\n\n" +
                   "Nicht gesendet werden: Namen oder Werte anderer Spieler, Aufnahmen, Chat oder sonst etwas von deinem PC.\n" +
                   "Du kannst das jederzeit unter Einstellungen → Allgemein ändern.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
            LineHeight = 20,
        });

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(Choice("Ja, mit meinem Namen", ShareModes.WithName));
        buttons.Children.Add(Choice("Ja, anonym", ShareModes.Anonymous));
        var no = Choice("Nein", ShareModes.Off);
        no.IsCancel = true;
        no.IsDefault = true;
        buttons.Children.Add(no);
        page.Children.Add(buttons);
        Content = page;
    }

    private Button Choice(string text, string mode)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0) };
        button.Click += (_, _) =>
        {
            _choice = mode;
            DialogResult = true;
        };
        return button;
    }

    /// <summary>Fragt einmal; Ergebnis „Aus“, „Anonym“ oder „Mit Name“ (Schließen/Esc = „Aus“).</summary>
    public static string Ask()
    {
        var dialog = new ShareConsentDialog();
        return dialog.ShowDialog() == true ? dialog._choice : ShareModes.Off;
    }
}
