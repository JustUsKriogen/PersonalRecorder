namespace PersonalRecorder;

internal static class WindowSelection
{
    // Reconnect only to the same application, never to an arbitrary foreground window.
    internal static AppWindow Resolve(AppWindow? previous, string processName, IReadOnlyList<AppWindow> windows)
    {
        var name = previous?.ProcessName ?? processName;
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Choose an application in Settings before recording.");
        var matches = windows.Where(w => string.Equals(w.ProcessName, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (previous is not null)
        {
            var sameProcess = matches.Where(w => w.ProcessId == previous.ProcessId).ToList();
            if (sameProcess.Count > 0) matches = sameProcess;
            var sameTitle = matches.Where(w => w.Title == previous.Title).ToList();
            if (sameTitle.Count == 1) return sameTitle[0];
        }
        if (matches.Count == 1) return matches[0];
        if (matches.Count == 0)
            throw new InvalidOperationException($"{name} has no available window. Open the application or match, then press Start again. You can also choose a different window in Settings.");
        throw new InvalidOperationException($"{name} has several windows. Choose the one to record in Settings.");
    }
}
