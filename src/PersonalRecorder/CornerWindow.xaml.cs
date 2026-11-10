using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace PersonalRecorder;

public partial class CornerWindow : Window
{
    public event Action? StartRequested, PauseRequested, StopRequested, RestoreRequested;
    private bool allowClose;
    public CornerWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }
    public void Update(string status, string time, bool canStart, bool canPause, bool canStop, bool paused,
        string startKey, string pauseKey, string stopKey)
    {
        StateText.Text = status;
        StateText.ToolTip = status;
        ClockText.Text = time;
        StartControl.IsEnabled = canStart;
        PauseControl.IsEnabled = canPause;
        StopControl.IsEnabled = canStop;
        PauseControl.Content = paused ? "▶" : "Ⅱ";
        StartControl.ToolTip = $"Start — {startKey}";
        PauseControl.ToolTip = $"{(paused ? "Resume" : "Pause")} — {pauseKey}";
        StopControl.ToolTip = $"Stop and save — {stopKey}";
    }
    private void Start_Click(object sender, RoutedEventArgs e) => StartRequested?.Invoke();
    private void Pause_Click(object sender, RoutedEventArgs e) => PauseRequested?.Invoke();
    private void Stop_Click(object sender, RoutedEventArgs e) => StopRequested?.Invoke();
    private void Restore_Click(object sender, RoutedEventArgs e) => RestoreRequested?.Invoke();
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) RestoreRequested?.Invoke();
        else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        RestoreRequested?.Invoke();
    }
    public void Shutdown() { allowClose = true; Close(); }
}
