using System.Text.Json;

namespace muten.Core;

// An app whose volume muten changed, with what to put back
public record AlteredAppState
{
    public string ProcessName { get; set; } = string.Empty;
    public float OriginalVolume { get; set; }
    public bool Muted { get; set; }
}

// Persists the altered apps to state.json so a crash never leaves apps muted or lowered
public static class StateManager
{
    private static string StatePath => Path.Combine(SettingsManager.DataDir, "state.json");

    public static List<AlteredAppState> Load()
    {
        try
        {
            if (!File.Exists(StatePath)) return [];
            var json = File.ReadAllText(StatePath);
            return JsonSerializer.Deserialize<List<AlteredAppState>>(json, SettingsManager.JsonOptions)?
                .Where(s => !string.IsNullOrEmpty(s.ProcessName))
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(IReadOnlyCollection<AlteredAppState> apps)
    {
        try
        {
            if (apps.Count == 0)
            {
                File.Delete(StatePath);
                return;
            }

            Directory.CreateDirectory(SettingsManager.DataDir);

            // Write then swap, so a crash mid-write cannot leave a truncated file behind
            var tempPath = StatePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(apps, SettingsManager.JsonOptions));
            File.Move(tempPath, StatePath, overwrite: true);
        }
        catch
        {
            // Losing crash recovery is better than crashing over it
        }
    }
}
