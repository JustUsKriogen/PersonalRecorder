using System.Windows;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace PersonalRecorder;

public partial class MainWindow
{
    private Forms.NotifyIcon? trayIcon;
    private Forms.ContextMenuStrip? trayMenu;
    private Drawing.Icon? trayArtwork;
    private Forms.ToolStripMenuItem? trayRestoreItem;
    private Forms.ToolStripMenuItem? trayExitItem;
    private CornerWindow? cornerWindow;

    private void ShowCornerPanel()
    {
        if (cornerWindow is null)
        {
            cornerWindow = new CornerWindow();
            cornerWindow.StartRequested += StartRecording;
            cornerWindow.PauseRequested += TogglePause;
            cornerWindow.StopRequested += StopRecording;
            cornerWindow.RestoreRequested += RestoreFromTray;
        }
        UpdateCornerPanel();
        cornerWindow.Show();
        var workArea = Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle).WorkingArea;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(cornerWindow).Handle;
        // Use physical screen coordinates; reposition twice after any per-monitor DPI change.
        NativeDesktop.PlaceInCorner(hwnd, workArea.Right, workArea.Bottom);
        NativeDesktop.PlaceInCorner(hwnd, workArea.Right, workArea.Bottom);
    }

    private void UpdateCornerPanel() => cornerWindow?.Update(StatusText.Text, TimerText.Text,
        state == CaptureState.Idle, state is CaptureState.Recording or CaptureState.Paused,
        state is CaptureState.Starting or CaptureState.Recording or CaptureState.Paused,
        state == CaptureState.Paused, settings.StartHotkey, settings.PauseHotkey, settings.StopHotkey);

    private void InitializeTray()
    {
        if (trayIcon is not null) return;
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/PersonalRecorder.ico"))!.Stream;
        using var artwork = new Drawing.Icon(stream);
        trayArtwork = (Drawing.Icon)artwork.Clone();
        trayMenu = new Forms.ContextMenuStrip();
        trayRestoreItem = new Forms.ToolStripMenuItem("Show Personal Recorder", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add(trayRestoreItem);
        trayMenu.Items.Add("Open recording folder", null, (_, _) => OpenFolder_Click(this, new RoutedEventArgs()));
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayExitItem = new Forms.ToolStripMenuItem("Exit", null, (_, _) =>
        {
            // Use normal close handling so an active recording finalizes before exit.
            settingsWindow?.Close();
            Close();
        });
        trayMenu.Items.Add(trayExitItem);
        trayIcon = new Forms.NotifyIcon
        {
            Icon = trayArtwork,
            Text = "Personal Recorder",
            ContextMenuStrip = trayMenu,
            Visible = false
        };
        trayIcon.DoubleClick += (_, _) => RestoreFromTray();
        UpdateTrayStatus();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized) return;
        try
        {
            InitializeTray();
            // Publish the restore route before hiding the window. Hide keeps its HWND
            // (and therefore the registered global hotkeys) alive.
            trayIcon!.Visible = true;
            ShowCornerPanel();
            Hide();
        }
        catch (Exception ex)
        {
            Show();
            WindowState = WindowState.Normal;
            DetailText.Text = "Could not minimize to the tray: " + ex.Message;
        }
    }

    private void RestoreFromTray()
    {
        cornerWindow?.Hide();
        Show();
        WindowState = WindowState.Normal;
        Activate();
        if (settingsWindow is not null)
        {
            settingsWindow.WindowState = WindowState.Normal;
            settingsWindow.Activate();
        }
        if (trayIcon is not null) trayIcon.Visible = false;
    }

    private void UpdateTrayStatus()
    {
        UpdateCornerPanel();
        if (trayIcon is null) return;
        var status = state switch
        {
            CaptureState.Starting => "Starting capture",
            CaptureState.Recording => "Recording",
            CaptureState.Paused => "Paused",
            CaptureState.Stopping => "Saving MP4",
            _ => "Ready"
        };
        trayIcon.Text = $"Personal Recorder — {status}";
        if (trayExitItem is not null)
            trayExitItem.Text = state == CaptureState.Idle ? "Exit" : "Stop, save and exit";
    }

    private void DisposeTray()
    {
        cornerWindow?.Shutdown();
        cornerWindow = null;
        if (trayIcon is not null)
        {
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayIcon = null;
        }
        trayMenu?.Dispose();
        trayArtwork?.Dispose();
        trayMenu = null;
        trayArtwork = null;
    }
}
