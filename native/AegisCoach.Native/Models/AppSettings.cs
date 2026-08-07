using System.Text.Json;

namespace AegisCoach.Native.Models;

/// <summary>应用本地设置，持久化到 %LOCALAPPDATA%\AegisCoach\settings.json。</summary>
public sealed class AppSettings
{
    public int GsiPort { get; set; } = 4444;
    public string GsiToken { get; set; } = string.Empty;
    public bool AutoStartGsi { get; set; } = true;
    public string? DotaCfgManualPath { get; set; }
    public bool SmartCoachEnabled { get; set; }
    public string LocalModelEndpoint { get; set; } = "http://127.0.0.1:11434";
    public string LocalModelName { get; set; } = "qwen2.5:1.5b";

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AegisCoach",
        "settings.json");

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Settings persistence is best effort; losing it only resets preferences.
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);
                if (loaded is not null) return loaded;
            }
        }
        catch
        {
            // Fall back to defaults when settings are corrupt.
        }

        return new AppSettings();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}
