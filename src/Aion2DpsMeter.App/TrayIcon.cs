using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace Aion2DpsMeter.App;

/// <summary>
/// Kleines Symbol im Infobereich der Taskleiste: Notausgang, falls das Overlay keine Klicks mehr annimmt
/// („Klicks durchlassen“) oder ein Tastenkürzel nicht greift. Doppelklick schaltet „Klicks durchlassen“ aus.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _clickThrough;
    private readonly Icon _image;

    public TrayIcon(Func<bool> isClickThrough, Action disableClickThrough, Action toggleClickThrough, Action toggleOverlay, Action openSettings, Action exit)
    {
        _clickThrough = new Forms.ToolStripMenuItem("Klicks durchlassen", null, (_, _) => toggleClickThrough());
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_clickThrough);
        menu.Items.Add(new Forms.ToolStripMenuItem("Overlay ein/aus", null, (_, _) => toggleOverlay()));
        menu.Items.Add(new Forms.ToolStripMenuItem("Einstellungen …", null, (_, _) => openSettings()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Beenden", null, (_, _) => exit()));
        menu.Opening += (_, _) => _clickThrough.Checked = isClickThrough();

        _image = CreateImage();
        _icon = new Forms.NotifyIcon
        {
            Icon = _image,
            Text = "AionMeta – Doppelklick: Klicks durchlassen aus",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => disableClickThrough();
    }

    /// <summary>Kurze Sprechblase am Symbol (z. B. beim Einschalten von „Klicks durchlassen“).</summary>
    public void Notify(string title, string text) => _icon.ShowBalloonTip(4000, title, text, Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        DestroyIcon(_image.Handle);
        _image.Dispose();
    }

    /// <summary>Selbst gezeichnet: drei steigende Balken auf dunklem Grund.</summary>
    private static Icon CreateImage()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var background = new SolidBrush(Color.FromArgb(255, 30, 34, 44));
            g.FillEllipse(background, 0, 0, 31, 31);
            using var bar = new SolidBrush(Color.FromArgb(255, 230, 170, 60));
            g.FillRectangle(bar, 8, 18, 4, 7);
            g.FillRectangle(bar, 14, 12, 4, 13);
            g.FillRectangle(bar, 20, 7, 4, 18);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
