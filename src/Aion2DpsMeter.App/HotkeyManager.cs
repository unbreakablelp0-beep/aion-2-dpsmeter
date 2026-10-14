using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Aion2DpsMeter.App;

/// <summary>
/// Tastenkürzel über die Windows-Funktion RegisterHotKey. Windows meldet nur unserem eigenen (unsichtbaren)
/// Nachrichtenfenster, dass die Kombination gedrückt wurde – es wird nichts ans Spiel gesendet, kein Tastatur-Hook,
/// keine Automatisierung. Ist eine Kombination schon von einem anderen Programm belegt, bleibt sie einfach ohne Wirkung.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const int WmHotkey = 0x0312;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 0xB000;

    public HotkeyManager()
    {
        // Nur-Nachrichten-Fenster (HWND_MESSAGE): unsichtbar, ohne Taskleisteneintrag.
        _source = new HwndSource(new HwndSourceParameters("Aion2DpsMeterHotkeys") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
        _source.AddHook(Hook);
    }

    /// <summary>Unsichtbares Nachrichtenfenster, an das Windows die Kürzel meldet.</summary>
    public IntPtr Handle => _source.Handle;

    /// <summary>Kürzel, die tatsächlich angemeldet werden konnten.</summary>
    public List<string> Registered { get; } = new();

    /// <summary>Kürzel, die schon belegt waren.</summary>
    public List<string> Failed { get; } = new();

    public void Register(uint modifiers, uint virtualKey, string label, Action action)
    {
        var id = _nextId++;
        if (RegisterHotKey(_source.Handle, id, modifiers | ModNoRepeat, virtualKey))
        {
            _actions[id] = action;
            Registered.Add(label);
        }
        else
        {
            Failed.Add(label);
        }
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            action();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
        _source.RemoveHook(Hook);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
