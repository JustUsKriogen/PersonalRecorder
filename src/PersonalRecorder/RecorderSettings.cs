using System.IO;
using System.Text.Json;

namespace PersonalRecorder;

public sealed class RecorderSettings
{
    public string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "PersonalRecorder");
    public string StartHotkey { get; set; } = "Ctrl+Shift+F9";
    public string StopHotkey { get; set; } = "Ctrl+Shift+F10";
    public string PauseHotkey { get; set; } = "Ctrl+Shift+F11";
    public int Fps { get; set; } = 60;
    public bool SystemAudio { get; set; } = true;
    public bool Microphone { get; set; }
    public bool Cursor { get; set; } = true;
    public bool HardwareEncoding { get; set; } = true;
    public string DisplayDevice { get; set; } = "";
    public string ProcessName { get; set; } = "";
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PersonalRecorder", "settings.json");
    public static RecorderSettings Load()
    {
        if (!File.Exists(SettingsPath)) return new();
        return JsonSerializer.Deserialize<RecorderSettings>(File.ReadAllText(SettingsPath)) ?? new();
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, SettingsPath, true);
    }
}
