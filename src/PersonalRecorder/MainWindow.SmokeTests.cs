using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PersonalRecorder;

public partial class MainWindow
{
    // Integration test captures only this app's own window, with all audio disabled.
    private async Task RunSmokeTest()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        var results = Path.Combine(AppContext.BaseDirectory, "smoke-results");
        Directory.CreateDirectory(results);
        try
        {
            // Exercise the same settings dialog and save action used by the toolbar.
            var originalFolder = settings.Folder;
            Exception? dialogError = null;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                try
                {
                    if (settingsWindow?.IsVisible != true) throw new Exception("Settings did not open.");
                    FolderBox.Text = "not-an-absolute-path";
                    Apply_Click(this, new RoutedEventArgs());
                    if (settingsSaved || settingsWindow?.IsVisible != true) throw new Exception("Invalid settings were accepted.");
                    FolderBox.Text = originalFolder;
                    PauseHotkeyBox.Text = StartHotkeyBox.Text;
                    Apply_Click(this, new RoutedEventArgs());
                    if (settingsSaved) throw new Exception("Duplicate pause shortcut was accepted.");
                    FolderBox.Text = results;
                }
                catch (Exception ex) { dialogError = ex; }
                finally { settingsWindow?.Close(); }
            }));
            Settings_Click(this, new RoutedEventArgs());
            if (dialogError is not null) throw dialogError;
            if (FolderBox.Text != originalFolder || settings.Folder != originalFolder)
                throw new Exception("Closing Settings did not discard changes.");
            _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                try
                {
                    FolderBox.Text = results;
                    StartHotkeyBox.Text = "Ctrl+Alt+Shift+F22";
                    StopHotkeyBox.Text = "Ctrl+Alt+Shift+F23";
                    PauseHotkeyBox.Text = "Ctrl+Alt+Shift+F21";
                    // Avoid including external application titles in the test image.
                    ApplicationPicker.SelectedItem = null;
                    SaveTestImage(settingsWindow!, Path.Combine(results, "settings.png"));
                    Apply_Click(this, new RoutedEventArgs());
                    if (!settingsSaved) throw new Exception("Settings save failed: " + HotkeyHint.Text);
                }
                catch (Exception ex) { dialogError = ex; settingsWindow?.Close(); }
            }));
            Settings_Click(this, new RoutedEventArgs());
            if (dialogError is not null) throw dialogError;
            if (settings.PauseHotkey != "Ctrl+Alt+Shift+F21" || settings.Folder != results)
                throw new Exception("Saved settings were lost.");
            if (StartShortcutText.Text != settings.StartHotkey || StopShortcutText.Text != settings.StopHotkey
                || PauseShortcutText.Text != settings.PauseHotkey)
                throw new Exception("Toolbar shortcut labels were not refreshed after saving settings.");
            if (Icon is null) throw new Exception("Window icon was not loaded.");
            if (!InputBindings.OfType<System.Windows.Input.KeyBinding>().Any(binding => binding.Command == OpenFolderCommand)
                || !InputBindings.OfType<System.Windows.Input.KeyBinding>().Any(binding => binding.Command == OpenSettingsCommand))
                throw new Exception("Toolbar navigation shortcuts were not bound.");
            if (System.Text.Json.JsonSerializer.Deserialize<RecorderSettings>("{}")!.PauseHotkey != "Ctrl+Shift+F11")
                throw new Exception("Existing settings do not receive a default pause shortcut.");
            DetailText.Text = "Recorder test window";
            SaveTestImage(this, Path.Combine(results, "toolbar.png"));
            File.WriteAllText(Path.Combine(results, "settings-test.txt"), "PASS: dialog visibility, invalid paths, duplicate pause shortcut, cancel, save, legacy settings default, compact layout.");
        }
        catch (Exception ex)
        {
            Finished(null, "Settings test: " + ex.Message);
            Close();
            return;
        }
        FolderBox.Text = Path.Combine(AppContext.BaseDirectory, "smoke-results");
        SystemAudioBox.IsChecked = MicrophoneBox.IsChecked = false;
        if (Environment.GetCommandLineArgs().Contains("--software")) HardwareBox.IsChecked = false;
        try
        {
            hotkeys!.Apply("Ctrl+Alt+Shift+F22", "Ctrl+Alt+Shift+F23", "Ctrl+Alt+Shift+F21");
            hotkeys.Apply("Ctrl+Alt+Shift+F22", "Ctrl+Alt+Shift+F23", "Ctrl+Alt+Shift+F21");
            hotkeys.Apply("Ctrl+Alt+Shift+F22", "Ctrl+Alt+Shift+F24", "Ctrl+Alt+Shift+F21");
            hotkeys.Apply("Ctrl+Alt+Shift+F24", "Ctrl+Alt+Shift+F22", "Ctrl+Alt+Shift+F21");
            bool rejected = false;
            try { hotkeys.Apply("Ctrl+Alt+Shift+F22", "Ctrl+Alt+Shift+F22", "Ctrl+Alt+Shift+F21"); }
            catch (ArgumentException) { rejected = true; }
            if (!rejected) throw new InvalidOperationException("Duplicate shortcuts were accepted.");
            var originalHandle = new WindowInteropHelper(this).Handle;
            for (int cycle = 0; cycle < 2; cycle++)
            {
                WindowState = WindowState.Minimized;
                await Task.Delay(150);
                if (IsVisible || trayIcon?.Visible != true || new WindowInteropHelper(this).Handle != originalHandle
                    || cornerWindow?.IsVisible != true || !cornerWindow.Topmost || cornerWindow.ShowInTaskbar)
                    throw new Exception("Minimize did not hide the toolbar while preserving its hotkey window.");
                SaveTestImage(cornerWindow, Path.Combine(results, "corner-panel.png"));
                using var probe = new HwndSource(new HwndSourceParameters("Tray hotkey probe") { Width = 1, Height = 1 });
                // These are the actual bindings applied immediately above.
                foreach (uint key in new uint[] { 0x87, 0x85, 0x84 })
                {
                    if (NativeDesktop.RegisterHotKey(probe.Handle, 950, 7, key))
                    {
                        NativeDesktop.UnregisterHotKey(probe.Handle, 950);
                        throw new Exception("A recording shortcut was released while in the tray.");
                    }
                }
                trayRestoreItem!.PerformClick();
                if (!IsVisible || WindowState != WindowState.Normal || trayIcon.Visible || cornerWindow.IsVisible)
                    throw new Exception("The tray Show command did not restore the toolbar.");
            }
            File.WriteAllText(Path.Combine(results, "tray-restore-test.txt"), "PASS: repeated minimize/restore, tray visibility, HWND preservation, global shortcut registrations retained.");
        }
        catch (Exception ex)
        {
            Finished(null, "Hotkey test: " + ex.Message);
            Close();
            return;
        }
        var self = new AppWindow(new WindowInteropHelper(this).Handle, (uint)Environment.ProcessId, "SmokeTest", "Recorder test window");
        ApplicationPicker.ItemsSource = new[] { self };
        ApplicationPicker.SelectedItem = self;
        await Task.Delay(750);
        StartRecording();
        await Task.Delay(4000);
        if (Environment.GetCommandLineArgs().Contains("--restart-test") && state == CaptureState.Recording)
        {
            StopRecording();
            for (int i = 0; i < 100 && state != CaptureState.Idle; i++) await Task.Delay(100);
            if (state != CaptureState.Idle || outputPath is null) throw new Exception("First recording did not finalize.");
            // Delete only the MP4 this smoke test just created in its own output directory.
            var deleted = outputPath;
            if (Path.GetDirectoryName(deleted) != results) throw new Exception("Unexpected test output location.");
            File.Delete(deleted);
            ApplicationPicker.ItemsSource = new[] { new AppWindow(0, 0, "NonexistentRecorderSmokeTarget", "Closed target") };
            ApplicationPicker.SelectedIndex = 0;
            StartRecording();
            if (StatusText.Text != "Unable to start" || !lastErrorDetails.Contains("No recording file was created")
                || lastErrorDetails.Contains(".log") || lastErrorDetails.Contains("software encoding")
                || RetrySoftwareButton.Visibility != Visibility.Collapsed)
                throw new Exception("Preflight target errors were incorrectly reported as encoder failures.");
            SaveTestImage(this, Path.Combine(results, "start-error.png"));
            ApplicationPicker.ItemsSource = new[] { self };
            ApplicationPicker.SelectedItem = self;
            StartRecording();
            await Task.Delay(4000);
            if (state != CaptureState.Recording || outputPath == deleted || File.Exists(deleted))
                throw new Exception("Deleting the prior recording prevented a new recording.");
            File.WriteAllText(Path.Combine(results, "restart-test.txt"), "PASS: deleted test recording, clear unavailable-target preflight error, successful new recording with unchanged application selection.");
        }
        string? originalRecording = null;
        long originalRecordingLength = 0;
        if (Environment.GetCommandLineArgs().Contains("--recovery-test") && state == CaptureState.Recording)
        {
            StopRecording();
            for (int i = 0; i < 100 && state != CaptureState.Idle; i++) await Task.Delay(100);
            if (state != CaptureState.Idle || outputPath is null)
            {
                Finished(null, "Recovery test could not finalize its first recording."); Close(); return;
            }
            originalRecording = outputPath;
            originalRecordingLength = new FileInfo(originalRecording).Length;
            if (!BuildErrorDetails("Test", "", originalRecording).Contains("did not confirm"))
                throw new Exception("An unfinalized file was reported as saved.");
            // Inject the native callback outcome, not a real GPU fault, to test recovery UI.
            Finished(originalRecording, "Simulated write error (0xc00d36b5) in video encoder: not accepting further input.");
            if (StatusText.Text != "Recording stopped — partial saved" || RecoveryPanel.Visibility != Visibility.Visible)
                throw new Exception("Finalized partial recordings were not reported correctly.");
            SaveTestImage(this, Path.Combine(results, "recovery-toolbar.png"));
            RetrySoftware_Click(this, new RoutedEventArgs());
            if (settings.HardwareEncoding || settings.Fps != 30 || outputPath == originalRecording)
                throw new Exception("Software retry did not select 30 FPS and a new output file.");
            await Task.Delay(4000);
        }
        if (state == CaptureState.Recording)
        {
            Pause_Click(this, new RoutedEventArgs());
            await Task.Delay(750);
            if (state != CaptureState.Paused || !PauseButton.Content.ToString()!.Contains("Resume"))
            {
                Finished(null, "Pause state or Resume button was not updated."); Close(); return;
            }
            SaveTestImage(this, Path.Combine(results, "paused-toolbar.png"));
            WindowState = WindowState.Minimized;
            await Task.Delay(150);
            if (state != CaptureState.Paused || trayIcon?.Visible != true || !trayIcon.Text.Contains("Paused")
                || cornerWindow?.IsVisible != true || cornerWindow.StateText.Text != "Paused")
            {
                Finished(null, "Minimizing changed the paused recording state or lost its tray status."); Close(); return;
            }
            SaveTestImage(cornerWindow!, Path.Combine(results, "paused-corner-panel.png"));
            trayRestoreItem!.PerformClick();
            Pause_Click(this, new RoutedEventArgs());
            await Task.Delay(1500);
            if (state != CaptureState.Recording)
            {
                Finished(null, "Resume failed."); Close(); return;
            }
        }
        if (state != CaptureState.Recording)
        {
            Finished(null, "Recording was not active before the tray exit test."); Close(); return;
        }
        Closed += (_, _) =>
        {
            File.WriteAllText(Path.Combine(results, "tray-exit-test.txt"),
                state == CaptureState.Idle && trayIcon is null && cornerWindow is null && outputPath is not null && new FileInfo(outputPath).Length > 0
                ? "PASS: tray Exit finalized the active recording and disposed the tray icon."
                : "FAIL: tray Exit did not finish saving or disposing resources.");
            if (originalRecording is not null)
                File.WriteAllText(Path.Combine(results, "recovery-test.txt"),
                    originalRecordingLength > 0 && new FileInfo(originalRecording).Length == originalRecordingLength && outputPath != originalRecording
                    ? "PASS: simulated finalized encoder failure reported; software 30 FPS retry created a new recording; original file untouched."
                    : "FAIL: recovery changed the original recording.");
        };
        WindowState = WindowState.Minimized;
        if (state != CaptureState.Recording || trayIcon?.Visible != true)
        {
            Finished(null, "Minimizing interrupted recording."); Close(); return;
        }
        trayExitItem!.PerformClick();
        for (var i = 0; i < 100 && state != CaptureState.Idle; i++) await Task.Delay(100);
        if (state != CaptureState.Idle)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-results", "result.txt"), "FAIL Recording did not finalize within 10 seconds.");
            Application.Current.Shutdown(1);
        }
        else if (IsLoaded) Close();
    }

    private static void SaveTestImage(Window window, string path)
    {
        window.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        image.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
