using System.Windows.Input;
using System.Windows.Interop;

namespace PersonalRecorder;

internal sealed class GlobalHotkeys : IDisposable
{
    private readonly HwndSource source;
    private readonly Action[] actions;
    private (uint Modifiers, uint Key)[] current = [];
    private int[] ids = [];
    private int nextId = 100;
    private bool suspended;
    public GlobalHotkeys(nint hwnd, Action start, Action stop, Action pause)
    {
        source = HwndSource.FromHwnd(hwnd);
        actions = [start, stop, pause];
        source.AddHook(Hook);
    }
    public void Apply(string startText, string stopText, string pauseText)
    {
        string[] labels = [startText, stopText, pauseText];
        var desired = labels.Select(Parse).ToArray();
        if (desired.Any(key => key == (2u, 0x4Fu) || key == (2u, 0xBCu)))
            throw new ArgumentException("Ctrl+O opens the recording folder and Ctrl+, opens Settings. Choose another recording shortcut.");
        if (desired.Distinct().Count() != desired.Length)
            throw new ArgumentException("Start, pause/resume, and stop shortcuts must all be different.");
        if (!suspended && desired.SequenceEqual(current)) return;
        Unregister();
        var registered = new List<int>();
        for (int i = 0; i < desired.Length; i++)
        {
            if (nextId >= 0xBFFF) nextId = 100;
            int id = nextId++;
            if (!NativeDesktop.RegisterHotKey(source.Handle, id, desired[i].Modifiers | 0x4000, desired[i].Key))
            {
                foreach (int newId in registered) NativeDesktop.UnregisterHotKey(source.Handle, newId);
                RestoreBindings();
                throw new InvalidOperationException($"The shortcut {labels[i]} is already in use or unavailable.");
            }
            registered.Add(id);
        }
        current = desired;
        ids = registered.ToArray();
        suspended = false;
    }
    private void Unregister()
    {
        foreach (int id in ids) NativeDesktop.UnregisterHotKey(source.Handle, id);
    }
    public void Suspend() { Unregister(); suspended = true; }
    public void Resume() { if (suspended) RestoreBindings(); }
    private void RestoreBindings()
    {
        for (int i = 0; i < current.Length; i++)
        {
            if (!NativeDesktop.RegisterHotKey(source.Handle, ids[i], current[i].Modifiers | 0x4000, current[i].Key))
            {
                Unregister();
                suspended = true;
                throw new InvalidOperationException("A shortcut was claimed by another app. Choose available shortcuts in Settings.");
            }
        }
        suspended = false;
    }
    internal static (uint Modifiers, uint Key) Parse(string text)
    {
        var parts = text.Trim().Split('+', StringSplitOptions.TrimEntries);
        var key = (Key)(new KeyConverter().ConvertFromInvariantString(parts[^1])
            ?? throw new ArgumentException("Press a key or a shortcut such as Ctrl+Shift+F9."));
        var modifierKeys = parts.Length == 1 ? ModifierKeys.None :
            (ModifierKeys)(new ModifierKeysConverter().ConvertFromInvariantString(string.Join('+', parts[..^1]))
                ?? throw new ArgumentException("Invalid shortcut modifiers."));
        if (key is Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed
            or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            throw new ArgumentException("Choose a key in addition to the modifier keys.");
        if (modifierKeys == ModifierKeys.None && (key < Key.F1 || key > Key.F24))
            throw new ArgumentException("Use F1–F24 alone, or include Ctrl, Alt, Shift, or Windows with another key.");
        uint modifiers = 0;
        if (modifierKeys.HasFlag(ModifierKeys.Alt)) modifiers |= 1;
        if (modifierKeys.HasFlag(ModifierKeys.Control)) modifiers |= 2;
        if (modifierKeys.HasFlag(ModifierKeys.Shift)) modifiers |= 4;
        if (modifierKeys.HasFlag(ModifierKeys.Windows)) modifiers |= 8;
        return (modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
    }
    internal static string Format(Key key, ModifierKeys modifiers)
    {
        var prefix = new ModifierKeysConverter().ConvertToInvariantString(modifiers);
        var keyName = new KeyConverter().ConvertToInvariantString(key);
        var text = string.IsNullOrEmpty(prefix) ? keyName! : $"{prefix}+{keyName}";
        Parse(text);
        return text;
    }
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != 0x0312 || suspended) return 0;
        int index = Array.IndexOf(ids, (int)wParam);
        if (index >= 0) { actions[index](); handled = true; }
        return 0;
    }
    public void Dispose() { Unregister(); source.RemoveHook(Hook); }
}
