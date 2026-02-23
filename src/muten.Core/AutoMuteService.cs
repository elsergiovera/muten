namespace muten.Core;

public class AutoMuteService
{
    private readonly AudioSessionManager _manager;
    private readonly Dictionary<string, ManagedApp> _managedApps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> _savedVolumes = new(StringComparer.OrdinalIgnoreCase);

    public bool Enabled { get; set; } = true;

    public AutoMuteService(AudioSessionManager manager)
    {
        _manager = manager;
    }

    public IReadOnlyDictionary<string, ManagedApp> ManagedApps => _managedApps;

    public bool IsManaged(string? exePath) =>
        !string.IsNullOrEmpty(exePath) && _managedApps.ContainsKey(exePath);

    public void AddManagedApp(ManagedApp app)
    {
        if (!string.IsNullOrEmpty(app.ExePath))
            _managedApps[app.ExePath] = app;
    }

    public void RemoveManagedApp(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return;

        if (_managedApps.Remove(exePath, out var app))
        {
            // Find process name for this app to unmute by name
            var processName = GetProcessNameFromPath(app.ExePath);
            if (processName != null)
            {
                if (_savedVolumes.Remove(exePath, out var volume))
                {
                    _manager.SetVolumeByName(processName, volume);
                }
                _manager.UnmuteByName(processName);
            }
        }
    }

    public void OnForegroundChanged(string foregroundProcess, string? foregroundExePath)
    {
        if (!Enabled) return;

        foreach (var (exePath, app) in _managedApps)
        {
            var processName = GetProcessNameFromPath(exePath);
            if (processName is null) continue;

            bool isForeground = foregroundExePath != null
                ? exePath.Equals(foregroundExePath, StringComparison.OrdinalIgnoreCase)
                : processName.Equals(foregroundProcess, StringComparison.OrdinalIgnoreCase);

            if (isForeground)
            {
                // Foreground app — unmute and restore volume
                _manager.UnmuteByName(processName);

                if (_savedVolumes.Remove(exePath, out var savedVolume))
                {
                    _manager.SetVolumeByName(processName, savedVolume);
                }
            }
            else
            {
                // Background app — save volume and mute
                if (!_savedVolumes.ContainsKey(exePath))
                {
                    var sessions = _manager.GetSessions();
                    var session = sessions.FirstOrDefault(s =>
                        s.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));

                    if (session != null)
                    {
                        _savedVolumes[exePath] = session.Volume;
                    }
                }

                _manager.MuteByName(processName);
            }
        }
    }

    public void RestoreAll()
    {
        foreach (var (exePath, app) in _managedApps)
        {
            var processName = GetProcessNameFromPath(exePath);
            if (processName is null) continue;

            _manager.UnmuteByName(processName);

            if (_savedVolumes.TryGetValue(exePath, out var volume))
            {
                _manager.SetVolumeByName(processName, volume);
            }
        }

        _savedVolumes.Clear();
    }

    private static string? GetProcessNameFromPath(string exePath) =>
        Path.GetFileNameWithoutExtension(exePath) is { Length: > 0 } name ? name : null;
}
