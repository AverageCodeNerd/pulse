using System.IO;
using System.Text.Json;

namespace Pulse.Services;

/// <summary>User preferences, persisted as JSON in %LOCALAPPDATA%\Pulse\settings.json.</summary>
public sealed class AppSettings
{
    public int UpdateMs { get; set; } = 1000;
    public string Theme { get; set; } = "System"; // System | Light | Dark
    public bool AlwaysOnTop { get; set; } = false;
    public int WinW { get; set; } = 1220;
    public int WinH { get; set; } = 800;
    public string LastPage { get; set; } = "processes";
    public bool Compact { get; set; } = false;
    public bool MinimizeToTray { get; set; } = false;

    // Process-list columns hidden by the user ("cpu","mem","disk","threads","pid","status").
    public List<string> HiddenColumns { get; set; } = new();

    // Per-resource graph colors (hex "#RRGGBB"). CpuColor "" means follow the system accent.
    public string CpuColor { get; set; } = "";
    public string MemColor { get; set; } = "#B18CFF";
    public string GpuColor { get; set; } = "#E06CB0";
    public string DiskColor { get; set; } = "#3AC07A";
    public string NetColor { get; set; } = "#E09B2E";

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pulse");
    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
