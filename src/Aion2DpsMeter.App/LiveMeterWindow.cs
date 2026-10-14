using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.App;

/// <summary>
/// Normales Fenster (kein Overlay) mit dem Live-Schaden je Verursacher. Liest alle 0,5 s eine Momentaufnahme
/// aus <see cref="LiveCombatMeter"/>; die Treffer kommen vom Aufnahme-Thread.
/// </summary>
public sealed class LiveMeterWindow : Window
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private readonly LiveCombatMeter _meter;
    private readonly Func<string> _status;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly TextBlock _header = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBlock _table = new() { FontFamily = new FontFamily("Consolas"), FontSize = 13 };
    private readonly TextBlock _footer = new() { FontSize = 11, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly ComboBox _ownBox = new() { MinWidth = 160, Margin = new Thickness(6, 0, 0, 0) };
    private bool _updatingOwnBox;

    public LiveMeterWindow(LiveCombatMeter meter, Func<string> status, Action<uint> openDetail)
    {
        _meter = meter;
        _status = status;
        Title = "Live-Schaden (Hypothese)";
        Topmost = true; // über dem Spiel bleiben (das Overlay nimmt nie den Fokus)
        Width = 880;
        Height = 420;
        MinWidth = 480;

        var hint = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF6, 0xE5)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xB0, 0x50)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8),
            Child = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Text = "Werte nach Community-Hypothese (Typ 04 38 und 05 38), an eigenen Aufnahmen abgeglichen: " +
                       "Einzeltreffer exakt, Summe 99,4 % bzw. mit Schaden über Zeit genau. Nicht offiziell, keine Gewähr. " +
                       "Selbsttreffer (Verursacher = Ziel) werden nicht gezählt. DPS = Schaden ÷ Zeit ab dem eigenen ersten Treffer " +
                       "bis zum letzten Treffer im Kampf – die Kampfanalyse des Spiels zählt länger und zeigt daher meist weniger DPS. " +
                       "Klasse aus den Fertigkeitsnummern; nur „Bogenschütze“ ist bisher belegt, sonst „Klasse NN“.",
            },
        };

        var reset = new Button { Content = "Zurücksetzen", Padding = new Thickness(12, 2, 12, 2) };
        reset.Click += (_, _) =>
        {
            _meter.Reset();
            Refresh();
        };
        _ownBox.SelectionChanged += (_, _) =>
        {
            if (_updatingOwnBox) return;
            _meter.OwnActor = _ownBox.SelectedItem is ComboBoxItem { Tag: uint id } ? id : null;
            Refresh();
        };

        var details = new Button { Content = "Details…", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Aufschlüsselung pro Fähigkeit (eigener Charakter, sonst der stärkste Spieler)" };
        details.Click += (_, _) =>
        {
            var snapshot = _meter.Snapshot(DateTimeOffset.UtcNow);
            var actor = _meter.OwnActor is { } own && snapshot.Actors.Any(a => a.Actor == own)
                ? own
                : snapshot.Actors.FirstOrDefault()?.Actor;
            if (actor is { } id) openDetail(id);
        };

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        controls.Children.Add(reset);
        controls.Children.Add(details);
        controls.Children.Add(new TextBlock { Text = "Eigener Charakter:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) });
        controls.Children.Add(_ownBox);

        var panel = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(hint, Dock.Top);
        DockPanel.SetDock(controls, Dock.Top);
        DockPanel.SetDock(_header, Dock.Top);
        DockPanel.SetDock(_footer, Dock.Bottom);
        panel.Children.Add(hint);
        panel.Children.Add(controls);
        panel.Children.Add(_header);
        panel.Children.Add(_footer);
        panel.Children.Add(new ScrollViewer { Content = _table, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = panel;

        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            Refresh();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    private void Refresh()
    {
        var snapshot = _meter.Snapshot(DateTimeOffset.UtcNow);
        UpdateOwnBox(snapshot);

        if (snapshot.Start is null)
        {
            _header.Text = "Noch keine Treffer";
            _table.Text = string.Empty;
        }
        else
        {
            var state = snapshot.Active ? "läuft" : "beendet (Leerlauf)";
            _header.Text = string.Create(German,
                $"Kampf {snapshot.FightNumber} – {state} – {snapshot.Duration.TotalSeconds:0.0} s – gesamt {Format(snapshot.TotalDamage)}, {snapshot.Dps ?? 0:#,0} DPS");

            var sb = new StringBuilder();
            sb.AppendLine("   Name                 Klasse        Kennung    Schaden       DPS  Anteil  Treffer  krit.  über Zeit  davon Pet");
            foreach (var a in snapshot.Actors)
            {
                var mark = a.IsOwn ? "► " : a.IsRememberedOwn ? "? " : "  ";
                var name = a.Name ?? (a.IsLikelyPet ? "Pet?" : "(Name unbekannt)");
                if (name.Length > 20) name = name[..19] + "…";
                sb.AppendLine(string.Create(German,
                    $"{mark} {name,-20} {a.ClassName ?? "–",-12} {a.Actor,7} {Format(a.Damage),10} {a.Dps,9:#,0} {a.Share * 100,6:0.0} % {a.Hits,7} {a.CriticalHits,6} {Format(a.OverTimeDamage),10} {Format(a.PetDamage),10}"));
            }

            _table.Text = sb.ToString();
        }

        var detected = _meter.DetectedOwnActor;
        var manual = _meter.OwnActorIsManual ? " – von Hand überschrieben" : string.Empty;
        var ownText = detected is { } d
            ? $"Eigener Charakter automatisch erkannt: {_meter.NameOf(d) ?? "?"} ({d}){manual}"
            : _meter.RememberedOwn is { } r
                ? $"Eigener Charakter nicht erkannt; gemerkt vom {r.DetectedAt.ToLocalTime():dd.MM. HH:mm}: {r.Name} ({r.Id}) – unsicher, mit „?“ markiert (falsch nach Charakterwechsel/neuem Login); oben bestätigen oder Aufnahme vor dem Einloggen starten{manual}"
                : "Eigener Charakter nicht automatisch erkannt – einmal teleportieren (oder Aufnahme vor dem Einloggen bzw. Charakterwechsel starten); sonst oben wählen";
        _footer.Text = $"{ownText} · {_status()} · verworfene Selbsttreffer: {snapshot.IgnoredSelfHits.ToString("#,0", German)} · " +
                       $"neuer Kampf nach {_meter.IdleTimeout.TotalSeconds:0} s ohne Treffer · Kennungen gelten vermutlich nur für die laufende Spielsitzung.";
    }

    private void UpdateOwnBox(LiveCombatSnapshot snapshot)
    {
        var manual = _meter.OwnActorIsManual ? _meter.OwnActor : null;
        var ids = snapshot.Actors.Select(a => a.Actor).ToList();
        if (manual is { } m && !ids.Contains(m)) ids.Add(m);
        ids.Sort(); // feste Reihenfolge, damit die Liste nicht bei jeder Aktualisierung neu aufgebaut wird
        var labels = ids.Select(id => _meter.NameOf(id) is { } n ? $"{n} ({id})" : id.ToString(German)).ToList();
        var shown = _ownBox.Items.Cast<ComboBoxItem>().Skip(1).Select(i => i.Content as string).ToList();
        if (shown.SequenceEqual(labels)) return;

        _updatingOwnBox = true;
        _ownBox.Items.Clear();
        _ownBox.Items.Add(new ComboBoxItem { Content = "(automatisch)", Tag = null });
        for (var i = 0; i < ids.Count; i++) _ownBox.Items.Add(new ComboBoxItem { Content = labels[i], Tag = ids[i] });
        _ownBox.SelectedItem = _ownBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Tag is uint t && t == manual) ?? _ownBox.Items[0];
        _updatingOwnBox = false;
    }

    private static string Format(long value) => value.ToString("#,0", German);
}
