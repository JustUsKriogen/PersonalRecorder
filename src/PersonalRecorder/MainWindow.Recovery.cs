using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace PersonalRecorder;

public partial class MainWindow
{
    private string lastErrorDetails = "";

    internal static string BuildErrorDetails(string error, string? finalizedPath, string? attemptedPath)
    {
        if (string.IsNullOrWhiteSpace(attemptedPath) && string.IsNullOrWhiteSpace(finalizedPath))
            return $"{error}\n\nRecording did not start. No recording file was created.";
        var fileStatus = !string.IsNullOrWhiteSpace(finalizedPath)
            ? $"The recording engine finalized the captured portion at:\n{finalizedPath}\nPlayback has not been verified. Footage after the failure was not recorded."
            : $"The engine did not confirm a finalized video. Any partial file remains at:\n{attemptedPath ?? "(no file created)"}\nIt may be incomplete; the app has not deleted or overwritten it.";
        var recovery = IsEncoderFailure(error) ? "\n\nRetry uses software encoding at 30 FPS, keeps the display resolution, and writes a NEW file. It cannot recover missed footage. Software encoding uses more CPU." : "";
        var diagnostics = string.IsNullOrWhiteSpace(attemptedPath) ? "" : $"\n\nDiagnostics (if writable):\n{attemptedPath}.diagnostics.txt\n{attemptedPath}.log";
        return $"{error}\n\n{fileStatus}{recovery}{diagnostics}";
    }
    private static bool IsEncoderFailure(string error) => error.Contains("video encoder", StringComparison.OrdinalIgnoreCase)
        || error.Contains("0xc00d36b5", StringComparison.OrdinalIgnoreCase);

    private void ShowStartError(string error)
    {
        timer.Stop();
        elapsed.Reset();
        TimerText.Text = "00:00:00";
        SetState(CaptureState.Idle, "Unable to start");
        DetailText.Text = error;
        lastErrorDetails = BuildErrorDetails(error, null, null);
        RecoveryHintText.Text = "Recording did not start. Open details for the next step.";
        RecoveryPanel.Visibility = Visibility.Visible;
        RetrySoftwareButton.Visibility = Visibility.Collapsed;
        if (!IsVisible) RestoreFromTray();
        if (smokeTest)
        {
            Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "smoke-results"));
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-results", "result.txt"), "FAIL " + error);
        }
    }

    private void AppendDiagnostic(string message)
    {
        if (outputPath is null) return;
        try { File.AppendAllText(outputPath + ".diagnostics.txt", message); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void ErrorDetails_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "Personal Recorder — Recording error", Owner = this, Icon = Icon,
            Width = 660, Height = 460, MinWidth = 400, MinHeight = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBox
            {
                Text = lastErrorDetails, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(18),
                FontSize = 14, Background = Background, Foreground = Foreground
            }
        };
        dialog.ShowDialog();
    }

    private void RetrySoftware_Click(object sender, RoutedEventArgs e)
    {
        if (state != CaptureState.Idle) return;
        HardwareBox.IsChecked = false;
        FpsPicker.SelectedIndex = 0;
        StartRecording();
    }
}
