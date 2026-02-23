namespace muten.Core;

public class AutoMuteService
{
    private readonly AudioSessionManager _manager;
    private readonly HashSet<string> _managedApps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> _savedVolumes = new(StringComparer.OrdinalIgnoreCase);

    public bool Enabled { get; set; } = true;

    public AutoMuteService(AudioSessionManager manager)
    {
        _manager = manager;
    }

    public IReadOnlySet<string> ManagedApps => _managedApps;

    public bool IsManaged(string processName) =>
        _managedApps.Contains(processName);

    public void AddManagedApp(string processName) =>
        _managedApps.Add(processName);

    public void RemoveManagedApp(string processName)
    {
        _managedApps.Remove(processName);

        // Restore the app when unmanaging it
        if (_savedVolumes.Remove(processName, out var volume))
        {
            _manager.SetVolumeByName(processName, volume);
        }
        _manager.UnmuteByName(processName);
    }

    public void OnForegroundChanged(string foregroundProcess)
    {
        if (!Enabled) return;

        foreach (var app in _managedApps)
        {
            if (app.Equals(foregroundProcess, StringComparison.OrdinalIgnoreCase))
            {
                // Foreground app — unmute and restore volume
                _manager.UnmuteByName(app);

                if (_savedVolumes.Remove(app, out var savedVolume))
                {
                    _manager.SetVolumeByName(app, savedVolume);
                }
            }
            else
            {
                // Background app — save volume and mute
                if (!_savedVolumes.ContainsKey(app))
                {
                    var sessions = _manager.GetSessions();
                    var session = sessions.FirstOrDefault(s =>
                        s.ProcessName.Equals(app, StringComparison.OrdinalIgnoreCase));

                    if (session != null)
                    {
                        _savedVolumes[app] = session.Volume;
                    }
                }

                _manager.MuteByName(app);
            }
        }
    }

    public void RestoreAll()
    {
        foreach (var app in _managedApps)
        {
            _manager.UnmuteByName(app);

            if (_savedVolumes.TryGetValue(app, out var volume))
            {
                _manager.SetVolumeByName(app, volume);
            }
        }

        _savedVolumes.Clear();
    }
}
