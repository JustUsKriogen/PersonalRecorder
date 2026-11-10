using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using ScreenRecorderLib;

namespace PersonalRecorder;

public partial class MainWindow : Window
{
    public static RoutedCommand OpenFolderCommand { get; } = new();
    public static RoutedCommand OpenSettingsCommand { get; } = new();
    private void OpenFolder_Executed(object sender, ExecutedRoutedEventArgs e) => OpenFolder_Click(sender, e);
    private void OpenSettings_Executed(object sender, ExecutedRoutedEventArgs e) => Settings_Click(sender, e);
    private RecorderSettings settings = new();
    private readonly SettingsPanel preferences = new();
    private Window? settingsWindow;
    private bool settingsSaved;
    private ComboBox ApplicationPicker => preferences.ApplicationPicker;
    private ComboBox DisplayPicker => preferences.DisplayPicker;
    private ComboBox FpsPicker => preferences.FpsPicker;
    private TextBox FolderBox => preferences.FolderBox;
    private TextBox StartHotkeyBox => preferences.StartHotkeyBox;
    private TextBox StopHotkeyBox => preferences.StopHotkeyBox;
    private TextBox PauseHotkeyBox => preferences.PauseHotkeyBox;
    private TextBlock HotkeyHint => preferences.HotkeyHint;
    private CheckBox SystemAudioBox => preferences.SystemAudioBox;
    private CheckBox MicrophoneBox => preferences.MicrophoneBox;
    private CheckBox CursorBox => preferences.CursorBox;
    private CheckBox HardwareBox => preferences.HardwareBox;
    private GlobalHotkeys? hotkeys;
    private Recorder? recorder;
    private AppWindow? target;
    private readonly Stopwatch elapsed = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private enum CaptureState { Idle, Starting, Recording, Paused, Stopping }
    private CaptureState state;
    private bool closeAfterSave;
    private string? outputPath;
    private string? stopReason;
    private DateTime startRequestedAt;
    private readonly bool smokeTest = Environment.GetCommandLineArgs().Contains("--smoke-test");

    public MainWindow()
    {
        InitializeComponent();
        try { settings = RecorderSettings.Load(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { DetailText.Text = "Settings could not be loaded; defaults restored. " + ex.Message; }
        LoadPreferences();
        preferences.RefreshButton.Click += Refresh_Click;
        preferences.BrowseButton.Click += Browse_Click;
        preferences.ApplyButton.Click += Apply_Click;
        foreach (var box in new[] { StartHotkeyBox, StopHotkeyBox, PauseHotkeyBox })
            box.PreviewKeyDown += Hotkey_PreviewKeyDown;
        Loaded += (_, _) =>
        {
            if (hotkeys is not null) return;
            hotkeys = new(new WindowInteropHelper(this).Handle, StartRecording, StopRecording, TogglePause);
            if (!smokeTest)
            {
                try { hotkeys.Apply(settings.StartHotkey, settings.StopHotkey, settings.PauseHotkey); }
                catch (Exception ex) { DetailText.Text = "Open Settings: " + ex.Message; }
            }
        };
        timer.Tick += (_, _) => Tick();
        StateChanged += Window_StateChanged;
        Closed += (_, _) => { timer.Stop(); hotkeys?.Dispose(); recorder?.Dispose(); DisposeTray(); };
        if (smokeTest) Loaded += async (_, _) => await RunSmokeTest();
    }

    private void LoadPreferences()
    {
        FolderBox.Text = settings.Folder;
        StartHotkeyBox.Text = settings.StartHotkey;
        StopHotkeyBox.Text = settings.StopHotkey;
        PauseHotkeyBox.Text = settings.PauseHotkey;
        FpsPicker.SelectedIndex = settings.Fps == 30 ? 0 : 1;
        SystemAudioBox.IsChecked = settings.SystemAudio;
        MicrophoneBox.IsChecked = settings.Microphone;
        CursorBox.IsChecked = settings.Cursor;
        HardwareBox.IsChecked = settings.HardwareEncoding;
        RefreshSources();
        StartButton.ToolTip = settings.StartHotkey;
        StopButton.ToolTip = settings.StopHotkey;
        PauseButton.ToolTip = settings.PauseHotkey;
        StartShortcutText.Text = settings.StartHotkey;
        PauseShortcutText.Text = settings.PauseHotkey;
        StopShortcutText.Text = settings.StopHotkey;
        if (state == CaptureState.Idle)
            DetailText.Text = (ApplicationPicker.SelectedItem as AppWindow)?.Label ?? "Choose an application in Settings.";
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (settingsWindow is not null) { settingsWindow.Activate(); return; }
        var originalApplication = (ApplicationPicker.SelectedItem as AppWindow)?.Handle;
        var originalDisplay = (DisplayPicker.SelectedItem as DisplayInfo)?.Device;
        settingsSaved = false;
        LoadPreferences();
        hotkeys?.Suspend();
        HotkeyHint.Text = "Click a shortcut field and press the desired keys. All three shortcuts must differ.";
        settingsWindow = new Window
        {
            Title = "Personal Recorder — Settings", Owner = this, Content = preferences,
            Icon = Icon,
            Width = 760, Height = Math.Min(820, SystemParameters.WorkArea.Height - 40),
            MinWidth = 640, MinHeight = 400, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Background, Foreground = Foreground
        };
        try { settingsWindow.ShowDialog(); }
        finally
        {
            settingsWindow.Content = null;
            settingsWindow = null;
            if (!settingsSaved)
            {
                ApplicationPicker.SelectedItem = ApplicationPicker.Items.Cast<AppWindow>().FirstOrDefault(w => w.Handle == originalApplication);
                DisplayPicker.SelectedItem = DisplayPicker.Items.Cast<DisplayInfo>().FirstOrDefault(d => d.Device == originalDisplay);
            }
            LoadPreferences();
            try { hotkeys?.Resume(); }
            catch (Exception ex) { DetailText.Text = ex.Message; }
        }
    }

    private void RefreshSources()
    {
        var selected = ApplicationPicker.SelectedItem as AppWindow;
        var display = (DisplayPicker.SelectedItem as DisplayInfo)?.Device ?? settings.DisplayDevice;
        var windows = NativeDesktop.Windows();
        ApplicationPicker.ItemsSource = windows;
        ApplicationPicker.SelectedItem = windows.FirstOrDefault(w => w.Handle == selected?.Handle && w.ProcessId == selected.ProcessId);
        if (ApplicationPicker.SelectedItem is null)
        {
            try { ApplicationPicker.SelectedItem = WindowSelection.Resolve(selected, settings.ProcessName, windows); }
            catch (InvalidOperationException) { /* Leave ambiguous or unavailable targets for explicit selection. */ }
        }
        var displays = NativeDesktop.Displays();
        DisplayPicker.ItemsSource = displays;
        DisplayPicker.SelectedItem = displays.FirstOrDefault(d => d.Device == display) ?? displays.FirstOrDefault();
    }
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshSources();
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a recording folder" };
        if (Directory.Exists(FolderBox.Text)) dialog.InitialDirectory = FolderBox.Text;
        if (dialog.ShowDialog(settingsWindow ?? this) == true) FolderBox.Text = dialog.FolderName;
    }
    private void Apply_Click(object sender, RoutedEventArgs e) => ApplyHotkeys();
    private void Hotkey_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab) return;
        e.Handled = true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        try
        {
            ((TextBox)sender).Text = GlobalHotkeys.Format(key, Keyboard.Modifiers);
            HotkeyHint.Text = "Shortcut selected. Click Save settings to activate all three shortcuts.";
        }
        catch (Exception ex) { HotkeyHint.Text = ex.Message; }
    }
    private void ApplyHotkeys()
    {
        try
        {
            if (!Path.IsPathFullyQualified(FolderBox.Text.Trim())) throw new ArgumentException("Choose an absolute recording folder path.");
            hotkeys!.Apply(StartHotkeyBox.Text, StopHotkeyBox.Text, PauseHotkeyBox.Text);
            var candidate = ReadPreferences();
            candidate.StartHotkey = StartHotkeyBox.Text.Trim();
            candidate.StopHotkey = StopHotkeyBox.Text.Trim();
            candidate.PauseHotkey = PauseHotkeyBox.Text.Trim();
            try { if (!smokeTest) candidate.Save(); }
            catch
            {
                hotkeys.Apply(settings.StartHotkey, settings.StopHotkey, settings.PauseHotkey);
                throw;
            }
            settings = candidate;
            settingsSaved = true;
            settingsWindow?.Close();
        }
        catch (Exception ex)
        {
            hotkeys?.Suspend();
            HotkeyHint.Text = "Settings could not be saved: " + ex.Message;
            if (smokeTest) File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-results", "settings-error.txt"), ex.ToString());
        }
    }
    private void SaveSettings()
    {
        var candidate = ReadPreferences();
        if (!smokeTest) candidate.Save();
        settings = candidate;
    }
    private RecorderSettings ReadPreferences() => new()
    {
        Folder = FolderBox.Text.Trim(), Fps = FpsPicker.SelectedIndex == 0 ? 30 : 60,
        SystemAudio = SystemAudioBox.IsChecked == true, Microphone = MicrophoneBox.IsChecked == true,
        Cursor = CursorBox.IsChecked == true, HardwareEncoding = HardwareBox.IsChecked == true,
        DisplayDevice = (DisplayPicker.SelectedItem as DisplayInfo)?.Device ?? "",
        ProcessName = (ApplicationPicker.SelectedItem as AppWindow)?.ProcessName ?? "",
        StartHotkey = settings.StartHotkey, StopHotkey = settings.StopHotkey, PauseHotkey = settings.PauseHotkey
    };
    private void Start_Click(object sender, RoutedEventArgs e) => StartRecording();
    private void Stop_Click(object sender, RoutedEventArgs e) => StopRecording();

    private AppWindow ResolveRecordingTarget()
    {
        var previous = ApplicationPicker.SelectedItem as AppWindow;
        if (previous is not null && NativeDesktop.StillExists(previous)) return previous;
        var windows = NativeDesktop.Windows();
        var resolved = WindowSelection.Resolve(previous, settings.ProcessName, windows);
        ApplicationPicker.ItemsSource = windows;
        ApplicationPicker.SelectedItem = resolved;
        return resolved;
    }

    private void StartRecording()
    {
        if (state != CaptureState.Idle) return;
        outputPath = null;
        stopReason = null;
        RecoveryPanel.Visibility = Visibility.Collapsed;
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
                throw new NotSupportedException("Window capture requires Windows 10 version 1903 or newer.");
            target = ResolveRecordingTarget();
            if (NativeDesktop.IsIconic(target.Handle)) throw new InvalidOperationException("Restore the selected application before recording.");
            var selectedDisplay = DisplayPicker.SelectedItem as DisplayInfo ?? throw new InvalidOperationException("Select an output display.");
            var display = NativeDesktop.Displays().FirstOrDefault(d => d.Device == selectedDisplay.Device)
                ?? throw new InvalidOperationException("The selected display was disconnected. Refresh the list.");
            if (display.Width % 2 != 0 || display.Height % 2 != 0)
                throw new NotSupportedException("H.264 needs even pixel dimensions. Choose a display with an even width and height.");
            var folder = FolderBox.Text.Trim();
            if (!Path.IsPathFullyQualified(folder)) throw new ArgumentException("Choose an absolute recording folder path.");
            Directory.CreateDirectory(folder);
            CheckDiskSpace(folder);
            SaveSettings();
            var safeName = string.Concat(target.ProcessName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            outputPath = Path.Combine(folder, $"{safeName}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Guid.NewGuid().ToString("N")[..6]}.mp4");
            // Probe permissions without pre-creating the encoder's destination file.
            using (new FileStream(outputPath + ".probe", FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.DeleteOnClose)) { }
            var options = RecorderOptions.Default;
            options.SourceOptions = new SourceOptions
            {
                RecordingSources = new List<RecordingSourceBase> { new WindowRecordingSource(target.Handle) }
            };
            options.OutputOptions = new OutputOptions
            {
                RecorderMode = RecorderMode.Video,
                OutputFrameSize = new ScreenSize(display.Width, display.Height),
                Stretch = StretchMode.Uniform
            };
            options.VideoEncoderOptions = new VideoEncoderOptions
            {
                Framerate = settings.Fps,
                Bitrate = (int)Math.Clamp((long)display.Width * display.Height * settings.Fps / 6, 8_000_000L, 100_000_000L),
                IsHardwareEncodingEnabled = settings.HardwareEncoding,
                // Avoid duplicating unchanged frames and let the sink apply backpressure.
                IsFixedFramerate = false,
                IsThrottlingDisabled = false,
                IsFragmentedMp4Enabled = true,
                IsMp4FastStartEnabled = false
            };
            options.AudioOptions = new AudioOptions
            {
                IsAudioEnabled = settings.SystemAudio || settings.Microphone
            };
            if (settings.SystemAudio) options.AudioOptions.AudioSources.Add(LoopbackAudioSource.Default);
            if (settings.Microphone) options.AudioOptions.AudioSources.Add(CaptureAudioSource.Default);
            options.MouseOptions = new MouseOptions { IsMousePointerEnabled = settings.Cursor };
            options.LogOptions = new LogOptions
            {
                IsLogEnabled = true,
                LogFilePath = outputPath + ".log",
                LogSeverityLevel = smokeTest ? LogLevel.Debug : LogLevel.Error
            };
            AppendDiagnostic($"Started {DateTimeOffset.Now:O}\nOS: {Environment.OSVersion}\nScreenRecorderLib: {typeof(Recorder).Assembly.GetName().Version}\nOutput: {display.Width}x{display.Height}; FPS target: {settings.Fps}; hardware requested: {settings.HardwareEncoding}\nAudio: system={settings.SystemAudio}, microphone={settings.Microphone}\nFragmented MP4; variable cadence; sink throttling enabled.\n");
            recorder = Recorder.CreateRecorder(options);
            if (smokeTest) File.WriteAllText(Path.Combine(folder, "environment.txt"), $"Output: {display.Width}x{display.Height}; hardware: {settings.HardwareEncoding}; OS: {Environment.OSVersion}");
            var activeRecorder = recorder;
            recorder.OnStatusChanged += (_, e) => Dispatcher.BeginInvoke(() => { if (ReferenceEquals(recorder, activeRecorder)) OnRecorderStatus(e.Status); });
            recorder.OnRecordingComplete += (_, e) => Dispatcher.BeginInvoke(() => { if (ReferenceEquals(recorder, activeRecorder)) Finished(e.FilePath, null); });
            recorder.OnRecordingFailed += (_, e) => Dispatcher.BeginInvoke(() => { if (ReferenceEquals(recorder, activeRecorder)) Finished(e.FilePath, e.Error); });
            elapsed.Reset();
            TimerText.Text = "00:00:00";
            SetState(CaptureState.Starting, "Starting capture…");
            startRequestedAt = DateTime.UtcNow;
            DetailText.Text = $"{display.Width} × {display.Height} · {settings.Fps} FPS · {Path.GetFileName(outputPath)}";
            recorder.Record(outputPath);
            timer.Start();
        }
        catch (Exception ex)
        {
            if (recorder is null && (outputPath is null || !File.Exists(outputPath))) ShowStartError(ex.Message);
            else Finished(null, ex.Message);
        }
    }

    private void OnRecorderStatus(RecorderStatus status)
    {
        if (state is CaptureState.Idle or CaptureState.Stopping) return;
        if (status == RecorderStatus.Recording)
        {
            elapsed.Start();
            SetState(CaptureState.Recording, "Recording");
        }
        else if (status == RecorderStatus.Paused)
        {
            elapsed.Stop();
            SetState(CaptureState.Paused, "Paused");
        }
    }
    private void Pause_Click(object sender, RoutedEventArgs e) => TogglePause();
    private void TogglePause()
    {
        try
        {
            if (state == CaptureState.Recording) recorder?.Pause();
            else if (state == CaptureState.Paused) recorder?.Resume();
        }
        catch (Exception ex) { DetailText.Text = "Could not change pause state: " + ex.Message; }
    }
    private void StopRecording()
    {
        if (state is CaptureState.Idle or CaptureState.Stopping) return;
        elapsed.Stop();
        SetState(CaptureState.Stopping, "Saving MP4…");
        try { recorder?.Stop(); }
        catch (Exception ex) { Finished(null, ex.Message); }
    }
    private void Finished(string? savedPath, string? error)
    {
        timer.Stop();
        elapsed.Stop();
        recorder?.Dispose();
        recorder = null;
        var hasFinalizedFile = !string.IsNullOrWhiteSpace(savedPath);
        SetState(CaptureState.Idle, error is null ? "Recording saved" : hasFinalizedFile ? "Recording stopped — partial saved" : "Recording could not complete");
        if (error is null)
        {
            DetailText.Text = $"{stopReason}{savedPath}";
            AppendDiagnostic($"Completed {DateTimeOffset.Now:O}; captured time {elapsed.Elapsed}.\n");
        }
        else
        {
            lastErrorDetails = BuildErrorDetails(error, savedPath, outputPath);
            DetailText.Text = hasFinalizedFile ? $"Captured portion finalized: {savedPath}" : "Open Error details for the cause and file location.";
            AppendDiagnostic($"Failed {DateTimeOffset.Now:O}; captured time {elapsed.Elapsed}.\n{lastErrorDetails}\n");
            RecoveryPanel.Visibility = Visibility.Visible;
            RecoveryHintText.Text = "Recording stopped. Review the details before restarting.";
            RetrySoftwareButton.Visibility = IsEncoderFailure(error) ? Visibility.Visible : Visibility.Collapsed;
        }
        if (error is not null && !IsVisible) RestoreFromTray();
        if (smokeTest)
        {
            Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "smoke-results"));
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-results", "result.txt"), error is null ? $"PASS {savedPath}" : $"FAIL {error}");
        }
        if (closeAfterSave) { closeAfterSave = false; if (error is null) Close(); }
    }
    private void SetState(CaptureState value, string label)
    {
        state = value;
        StatusText.Text = label;
        preferences.ConfigurationPanel.IsEnabled = value == CaptureState.Idle;
        preferences.ApplyButton.IsEnabled = value == CaptureState.Idle;
        preferences.EditingHint.Text = value == CaptureState.Idle
            ? "Shortcuts are suspended while settings are open. Close to discard unsaved changes."
            : "Stop the recording before changing settings. Close this window to restore recording shortcuts.";
        StartButton.IsEnabled = value == CaptureState.Idle;
        StopButton.IsEnabled = value is CaptureState.Starting or CaptureState.Recording or CaptureState.Paused;
        PauseButton.IsEnabled = value is CaptureState.Recording or CaptureState.Paused;
        PauseButton.Content = value == CaptureState.Paused ? "▶ Resume" : "Ⅱ Pause";
        UpdateTrayStatus();
    }
    private static void CheckDiskSpace(string path)
    {
        var root = Path.GetPathRoot(path);
        if (root is null || root.StartsWith(@"\\")) return;
        var drive = new DriveInfo(root);
        if (drive.IsReady && drive.AvailableFreeSpace < 512L * 1024 * 1024)
            throw new IOException("Less than 512 MB is free on the recording drive.");
    }
    private void Tick()
    {
        TimerText.Text = elapsed.Elapsed.ToString(@"hh\:mm\:ss");
        UpdateCornerPanel();
        if (state == CaptureState.Starting && DateTime.UtcNow - startRequestedAt > TimeSpan.FromSeconds(20))
        {
            stopReason = "No frames arrived within 20 seconds; capture stopped. ";
            StopRecording();
        }
        if (state is not (CaptureState.Recording or CaptureState.Paused)) return;
        if (target is not null && !NativeDesktop.StillExists(target))
        {
            stopReason = "The target window closed; recording saved. ";
            StopRecording();
            return;
        }
        if (target is not null && NativeDesktop.IsIconic(target.Handle))
            DetailText.Text = "The target is minimized. Restore it; capture may freeze while minimized.";
        try { if (outputPath is not null) CheckDiskSpace(outputPath); }
        catch (IOException) { stopReason = "Low disk space; recording stopped. "; StopRecording(); }
        catch (UnauthorizedAccessException) { stopReason = "Storage access lost; recording stopped. "; StopRecording(); }
    }
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = FolderBox.Text.Trim();
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Choose an existing folder or make a recording first.");
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder }, UseShellExecute = true });
        }
        catch (Exception ex) { DetailText.Text = ex.Message; if (!IsVisible) RestoreFromTray(); }
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (state != CaptureState.Idle)
        {
            e.Cancel = true;
            closeAfterSave = true;
            StopRecording();
            return;
        }
        try { SaveSettings(); }
        catch (Exception ex)
        {
            if (!IsVisible) RestoreFromTray();
            MessageBox.Show(this, "Preferences could not be saved: " + ex.Message, "Personal Recorder");
        }
    }

}
