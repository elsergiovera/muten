using System.Text.Json;
using System.Text.Json.Nodes;

namespace muten.Core;

public record ManagedApp
{
    public string ExePath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public record MutenSettings
{
    public List<ManagedApp> ManagedApps { get; set; } = [];
    public bool AutoMuteEnabled { get; set; } = true;
}

public static class SettingsManager
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "muten");

    private static readonly string SettingsPath =
        Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
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

            // Detect old format: managedApps is a list of strings
            var managedApps = doc["managedApps"];
            if (managedApps is JsonArray arr && arr.Count > 0 && arr[0] is JsonValue)
            {
                var migrated = new MutenSettings
                {
                    AutoMuteEnabled = doc["autoMuteEnabled"]?.GetValue<bool>() ?? true,
                    ManagedApps = arr
                        .Select(item => item?.GetValue<string>() ?? "")
                        .Where(s => !string.IsNullOrEmpty(s))
                        .Select(name => new ManagedApp { ExePath = name, DisplayName = name })
                        .ToList(),
                };

                // Re-save in new format
                Save(migrated);
                return migrated;
            }

            return JsonSerializer.Deserialize<MutenSettings>(json, JsonOptions) ?? new MutenSettings();
        }
        catch
        {
            return new MutenSettings();
        }
    }

    public static void Save(MutenSettings settings)
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
