using System.Windows;
using Aion2DpsMeter.App;
using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.App.Tests;

/// <summary>Die eigenen Klassensymbole lassen sich für alle Klassen erzeugen (WPF braucht dafür einen STA-Thread).</summary>
public class ClassIconsTests
{
    [Fact]
    public void AllClassIconsCanBeCreated()
    {
        Exception? failure = null;
        var created = 0;
        var thread = new Thread(() =>
        {
            try
            {
                foreach (var prefix in Enumerable.Range(11, 8))
                {
                    var name = SkillClassMap.Describe(prefix);
                    Assert.True(ClassIcons.Colors.ContainsKey(name), $"Keine Farbe für {name}");
                    var icon = ClassIcons.Create(name, 20);
                    icon.Measure(new Size(20, 20));
                    created++;
                }

                ClassIcons.Create(null, 20).Measure(new Size(20, 20));
                ClassIcons.Create("Klasse 19", 20).Measure(new Size(20, 20));
                ClassIcons.Create("Beschwörer", 20, isPet: true).Measure(new Size(20, 20));
                created += 3;
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.Equal(11, created);
    }

    /// <summary>Die dunklen Stile (Knopf, Schiebeschalter) lassen sich laden und auf Steuerelemente anwenden.</summary>
    [Fact]
    public void DarkThemeStylesLoadAndApply()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var styles = DarkTheme.StyleResources;
                Assert.NotNull(styles[typeof(System.Windows.Controls.Button)]);
                Assert.NotNull(styles[typeof(System.Windows.Controls.CheckBox)]);

                var panel = new System.Windows.Controls.StackPanel();
                panel.Resources.MergedDictionaries.Add(styles);
                var button = new System.Windows.Controls.Button { Content = "Öffnen" };
                var toggle = new System.Windows.Controls.CheckBox { Content = "an", IsChecked = true };
                panel.Children.Add(button);
                panel.Children.Add(toggle);
                panel.Measure(new Size(400, 200));
                panel.Arrange(new Rect(0, 0, 400, 200));
                Assert.True(button.ApplyTemplate() || button.Template is not null);
                Assert.True(toggle.DesiredSize.Width >= 36); // Schalterbahn 36 px + Text
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(failure);
    }

    /// <summary>Fenster mit dunklem Design liegen immer oben (sonst hinter dem Spiel, weil das Overlay nie den Fokus nimmt).</summary>
    [Fact]
    public void DarkThemedWindowsStayOnTop()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new Window(); // wird nicht angezeigt
                DarkTheme.Apply(window);
                Assert.True(window.Topmost);
                Assert.True(window.ShowActivated);
                window.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(failure);
    }

    /// <summary>Das (neu gestaltete) Einstellungsfenster lässt sich aufbauen und jede Seite erzeugen – ohne es anzuzeigen.</summary>
    [Fact]
    public void SettingsWindowBuildsEveryPage()
    {
        Exception? failure = null;
        var pages = 0;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new OverlaySettings();
                IReadOnlyList<Aion2DpsMeter.Core.Events.WorldBoss> bosses = Aion2DpsMeter.Core.Events.WorldBossTimes.Default;
                var window = new OverlaySettingsWindow(
                    () => settings, change => settings = change(settings), () => { }, () => { }, () => { },
                    () => Aion2DpsMeter.Core.Events.GameEventSchedule.Default, () => "Test",
                    () => bosses, b => { bosses = b; return null; },
                    () => { }, () => { }, ["Strg+Umschalt+K: Test"], []);
                var dictionary = (System.Collections.IDictionary)typeof(OverlaySettingsWindow)
                    .GetField("_pages", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
                var show = typeof(OverlaySettingsWindow).GetMethod("ShowPage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                foreach (string name in dictionary.Keys)
                {
                    show.Invoke(window, [name]);
                    pages++;
                }

                window.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(failure);
        Assert.True(pages >= 5);
    }

    /// <summary>Das eigene Kampfkraft-Symbol lässt sich zeichnen; mit AION2DPS_ICON_PREVIEW wird eine Vorschau als PNG gespeichert.</summary>
    [Fact]
    public void CombatPowerVectorRenders()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var icon = ClassIcons.CombatPowerVector(32);
                icon.Measure(new Size(32, 32));
                icon.Arrange(new Rect(0, 0, 32, 32));
                Assert.Equal(32, icon.DesiredSize.Width, 1);
                if (Environment.GetEnvironmentVariable("AION2DPS_ICON_PREVIEW") is { Length: > 0 } path)
                {
                    var host = new System.Windows.Controls.Border { Width = 64, Height = 64, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x12, 0x14, 0x1A)), Child = ClassIcons.CombatPowerVector(48) };
                    host.Measure(new Size(64, 64));
                    host.Arrange(new Rect(0, 0, 64, 64));
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(64, 64, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(host);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var file = File.Create(path);
                    encoder.Save(file);
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(failure);
    }

    /// <summary>Wochenstatistik (leerer Speicher) und DPS-Kurve lassen sich aufbauen, ohne angezeigt zu werden.</summary>
    [Fact]
    public void WeeklyStatsAndChartBuild()
    {
        Exception? failure = null;
        var directory = Path.Combine(Path.GetTempPath(), $"aion2dps-stats-{Guid.NewGuid():N}");
        var thread = new Thread(() =>
        {
            try
            {
                var window = new WeeklyStatsWindow(new FightHistoryStore(directory));
                window.Close();
                var chart = new DpsChart();
                chart.Update([1000, 2000, 1500, 3000], System.Windows.Media.Colors.Teal);
                chart.Measure(new Size(300, 120));
                chart.Arrange(new Rect(0, 0, 300, 120));
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        Assert.Null(failure);
    }

    /// <summary>Die normale Version enthält keine eingebetteten (fremden) Klassenbilder.</summary>
    [Fact]
    public void StandardBuildHasNoEmbeddedIcons()
    {
        Assert.False(ClassIcons.HasEmbeddedIcons);
        Assert.DoesNotContain(typeof(ClassIcons).Assembly.GetManifestResourceNames(), n => n.StartsWith(ClassIcons.EmbeddedPrefix, StringComparison.Ordinal));
    }
}
