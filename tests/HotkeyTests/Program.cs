using System.Windows.Input;
using System.Windows.Interop;
using PersonalRecorder;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var old = new AppWindow(1, 10, "Game", "Match");
        var recreated = new AppWindow(2, 10, "Game", "Match");
        var restarted = new AppWindow(3, 20, "Game", "New match");
        var otherApp = new AppWindow(4, 30, "Other", "Match");
        Assert(WindowSelection.Resolve(old, "Game", [recreated, restarted, otherApp]) == recreated, "Recreated same-process window");
        Assert(WindowSelection.Resolve(old, "Game", [restarted, otherApp]) == restarted, "Restarted application");
        Assert(WindowSelection.Resolve(null, "Game", [restarted]) == restarted, "Remembered app relaunched");
        Reject(() => WindowSelection.Resolve(old, "Game", [otherApp]), "Unrelated application fallback");
        Reject(() => WindowSelection.Resolve(old, "Game", [restarted, restarted with { Handle = 5 }]), "Ambiguous application windows");
        Reject(() => WindowSelection.Resolve(null, "", [otherApp]), "No configured target");
        Assert(GlobalHotkeys.Parse("F9") == (0u, 0x78u), "Standalone F9");
        Assert(GlobalHotkeys.Parse("Ctrl+Shift+F9") == (6u, 0x78u), "Modifier parsing");
        Assert(GlobalHotkeys.Parse(GlobalHotkeys.Format(Key.F10, ModifierKeys.Alt)) == (1u, 0x79u), "Alt key capture");
        Assert(GlobalHotkeys.Parse(GlobalHotkeys.Format(Key.R, ModifierKeys.Control | ModifierKeys.Shift)) == (6u, 0x52u), "Letter chord capture");
        Reject(() => GlobalHotkeys.Parse("R"), "Unmodified letter");
        Reject(() => GlobalHotkeys.Parse("Ctrl+LeftShift"), "Modifier-only chord");
        using var first = new HwndSource(new HwndSourceParameters("Hotkey regression test") { Width = 1, Height = 1 });
        using var second = new HwndSource(new HwndSourceParameters("Hotkey conflict test") { Width = 1, Height = 1 });
        using var hotkeys = new GlobalHotkeys(first.Handle, () => { }, () => { }, () => { });
        const string start = "Ctrl+Alt+Shift+F22", stop = "Ctrl+Alt+Shift+F23", pause = "Ctrl+Alt+Shift+F21";
        hotkeys.Apply(start, stop, pause);
        hotkeys.Apply(start, stop, pause);
        hotkeys.Apply(stop, start, pause);
        hotkeys.Apply(start, stop, pause);
        Reject(() => hotkeys.Apply(start, start, pause), "Duplicate keys");
        Reject(() => hotkeys.Apply(start, stop, start), "Pause duplicates start");
        Reject(() => hotkeys.Apply(start, stop, stop), "Pause duplicates stop");
        Reject(() => hotkeys.Apply("Ctrl+O", stop, pause), "Reserved folder shortcut");
        Reject(() => hotkeys.Apply(start, stop, "Ctrl+OemComma"), "Reserved settings shortcut");
        Assert(!NativeDesktop.RegisterHotKey(second.Handle, 900, 7, 0x85), "Start key reserved");
        hotkeys.Suspend();
        Assert(NativeDesktop.RegisterHotKey(second.Handle, 903, 7, 0x84), "Editing releases pause key");
        NativeDesktop.UnregisterHotKey(second.Handle, 903);
        Assert(NativeDesktop.RegisterHotKey(second.Handle, 900, 7, 0x85), "Editing releases start key");
        Assert(NativeDesktop.RegisterHotKey(second.Handle, 901, 7, 0x86), "Editing releases stop key");
        NativeDesktop.UnregisterHotKey(second.Handle, 900);
        NativeDesktop.UnregisterHotKey(second.Handle, 901);
        hotkeys.Resume();
        Assert(!NativeDesktop.RegisterHotKey(second.Handle, 903, 7, 0x84), "Leaving editor restores pause");
        Assert(!NativeDesktop.RegisterHotKey(second.Handle, 900, 7, 0x85), "Leaving editor restores start");
        Assert(NativeDesktop.RegisterHotKey(second.Handle, 902, 7, 0x87), "Reserve conflicting key");
        try
        {
            Reject(() => hotkeys.Apply(start, "Ctrl+Alt+Shift+F24", pause), "Conflicting stop key");
            Assert(!NativeDesktop.RegisterHotKey(second.Handle, 901, 7, 0x86), "Conflict restores old stop key");
            Reject(() => hotkeys.Apply(start, stop, "Ctrl+Alt+Shift+F24"), "Conflicting pause key");
            Assert(!NativeDesktop.RegisterHotKey(second.Handle, 903, 7, 0x84), "Conflict restores old pause key");
        }
        finally { NativeDesktop.UnregisterHotKey(second.Handle, 902); }
        hotkeys.Suspend();
        hotkeys.Apply(start, stop, pause);
        Assert(!NativeDesktop.RegisterHotKey(second.Handle, 900, 7, 0x85), "Apply from editor restores registration");
        Console.WriteLine("PASS: shortcut capture formatting, validation, registration, swaps, edit suspension, resume, and conflict rollback.");
    }
    private static void Assert(bool result, string name)
    {
        if (!result) throw new Exception("FAIL: " + name);
    }
    private static void Reject(Action action, string name)
    {
        try { action(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return; }
        throw new Exception("FAIL: accepted " + name);
    }
}
