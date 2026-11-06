using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace muten.Core;

public record ManagedApp
{
    public string ProcessName { get; set; } = string.Empty;
    public string? ExePath { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    // Background volume in percent of the app's own volume; 0 mutes it
    public int BackgroundVolume { get; set; }
}

public record MutenSettings
{
    public List<ManagedApp> ManagedApps { get; set; } = [];
    public bool AutoMuteEnabled { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    public int FadeMs { get; set; } = 150;
    public int GraceMs { get; set; } = 300;

    // Global hotkey that toggles pause, e.g. "Ctrl+Alt+M"; empty disables it
    public string PauseHotkey { get; set; } = "Ctrl+Alt+M";
}

public static class SettingsManager
{
    // %APPDATA%\muten; settable so tests can point it at a scratch folder
    public static string DataDir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "muten");

    private static string SettingsPath => Path.Combine(DataDir, "settings.json");

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Keeps "Ctrl+Alt+M" readable instead of escaping the "+"; these files are never embedded in HTML
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static MutenSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new MutenSettings();

            var json = File.ReadAllText(SettingsPath);
            var doc = JsonNode.Parse(json);
            if (doc is null) return new MutenSettings();

            // Migration: managedApps is a list of plain strings (oldest format)
            var managedApps = doc["managedApps"];
            if (managedApps is JsonArray arr && arr.Count > 0 && arr[0] is JsonValue)
            {
                var migrated = new MutenSettings
                {
                    AutoMuteEnabled = doc["autoMuteEnabled"]?.GetValue<bool>() ?? true,
                    ManagedApps = arr
                        .Select(item => item?.GetValue<string>() ?? "")
                        .Where(s => !string.IsNullOrEmpty(s))
                        .Select(path => new ManagedApp
                        {
                            ProcessName = Path.GetFileNameWithoutExtension(path),
                            ExePath = path,
                            DisplayName = Path.GetFileNameWithoutExtension(path),
                        })
                        .ToList(),
                };

                Save(migrated);
                return migrated;
            }

            var settings = JsonSerializer.Deserialize<MutenSettings>(json, JsonOptions) ?? new MutenSettings();

            // Migration: exePath-keyed entries that lack processName
            bool needsSave = false;
            foreach (var app in settings.ManagedApps)
            {
                if (string.IsNullOrEmpty(app.ProcessName) && !string.IsNullOrEmpty(app.ExePath))
                {
                    app.ProcessName = Path.GetFileNameWithoutExtension(app.ExePath);
                    needsSave = true;
                }
            }

            if (needsSave) Save(settings);
            return settings;
        }
        catch
        {
            return new MutenSettings();
        }
    }

    public static void Save(MutenSettings settings)
    {
        Directory.CreateDirectory(DataDir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
