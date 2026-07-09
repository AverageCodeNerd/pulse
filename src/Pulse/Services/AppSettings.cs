using System.IO;
using System.Text.Json;

namespace Pulse.Services;

/// <summary>User preferences, persisted as JSON in %LOCALAPPDATA%\Pulse\settings.json.</summary>
public sealed class AppSettings
{
    public int UpdateMs { get; set; } = 1000;
    public string Theme { get; set; } = "System"; // System | Light | Dark

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
