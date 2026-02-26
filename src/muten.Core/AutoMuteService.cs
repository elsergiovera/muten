namespace muten.Core;

public class AutoMuteService
{
    private readonly AudioSessionManager _manager;
    private readonly Dictionary<string, ManagedApp> _managedApps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> _savedVolumes = new(StringComparer.OrdinalIgnoreCase);

    public bool Enabled { get; set; } = true;

    public event Action<List<string>>? AppsMuted;

    public AutoMuteService(AudioSessionManager manager)
    {
        _manager = manager;
    }

    public IReadOnlyDictionary<string, ManagedApp> ManagedApps => _managedApps;

    public bool IsManaged(string? processName) =>
        !string.IsNullOrEmpty(processName) && _managedApps.ContainsKey(processName);

    public void AddManagedApp(ManagedApp app)
    {
        if (!string.IsNullOrEmpty(app.ProcessName))
            _managedApps[app.ProcessName] = app;
    }

    public void RemoveManagedApp(string? processName)
    {
        if (string.IsNullOrEmpty(processName)) return;

        if (_managedApps.Remove(processName))
        {
            if (_savedVolumes.Remove(processName, out var volume))
            {
                _manager.SetVolumeByName(processName, volume);
            }
            _manager.UnmuteByName(processName);
        }
    }

    public void OnForegroundChanged(string foregroundProcess, string? foregroundExePath)
    {
        if (!Enabled) return;

        List<string>? newlyMuted = null;

        foreach (var (processName, app) in _managedApps)
        {
            bool isForeground = processName.Equals(foregroundProcess, StringComparison.OrdinalIgnoreCase);

            if (isForeground)
            {
                // Foreground app — unmute and restore volume
                _manager.UnmuteByName(processName);

                if (_savedVolumes.Remove(processName, out var savedVolume))
                {
                    _manager.SetVolumeByName(processName, savedVolume);
                }
            }
            else
            {
                // Background app — save volume and mute
                bool wasAlreadyMuted = _savedVolumes.ContainsKey(processName);

                if (!wasAlreadyMuted)
                {
                    var sessions = _manager.GetSessions();
                    var session = sessions.FirstOrDefault(s =>
                        s.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));

                    if (session != null)
                    {
                        _savedVolumes[processName] = session.Volume;
                        newlyMuted ??= [];
                        newlyMuted.Add(app.DisplayName);
                    }
                }

                _manager.MuteByName(processName);
            }
        }

        if (newlyMuted is { Count: > 0 })
            AppsMuted?.Invoke(newlyMuted);
    }

    public void RestoreAll()
    {
        foreach (var (processName, app) in _managedApps)
        {
            _manager.UnmuteByName(processName);

            if (_savedVolumes.TryGetValue(processName, out var volume))
            {
                _manager.SetVolumeByName(processName, volume);
            }
        }

        _savedVolumes.Clear();
    }
}
