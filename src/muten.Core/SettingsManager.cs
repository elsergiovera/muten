using System.Text.Json;

namespace muten.Core;

public record MutenSettings
{
    public List<string> ManagedApps { get; set; } = [];
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
