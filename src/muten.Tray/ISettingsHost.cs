using muten.Core;

namespace muten.Tray;

public enum UpdateState
{
    Disabled,     // dev build: not a published single-file exe
    Checking,
    UpToDate,
    Available,
    Installing,
    CheckFailed,
}

// What the Settings window reads and changes; implemented by the tray (and by fakes in tests/screenshots)
public interface ISettingsHost
{
    IReadOnlyList<ManagedApp> ManagedApps { get; }
    void AddApp(ManagedApp app);
    void RemoveApp(string processName);
    void SetBackgroundVolume(string processName, int percent);

    int FadeMs { get; set; }
    int GraceMs { get; set; }
    bool Notifications { get; set; }
    bool StartWithWindows { get; set; }

    Hotkey? PauseHotkey { get; }

    // Registers and saves the new pause hotkey (null disables it); returns an error message when it can't be
    // registered, in which case the previous hotkey stays active
    string? SetPauseHotkey(Hotkey? hotkey);

    // Unregisters the hotkey while the hotkey box is capturing keys, so pressing the current combo is recorded
    void SuspendHotkey(bool suspended);

    IReadOnlyList<RunningApp> GetRunningApps();

    string Version { get; }
    UpdateState UpdateState { get; }
    string? AvailableVersion { get; }
    void InstallUpdate();

    // Checks for an update now (no-op while a check or install is running, or in a dev build)
    void CheckForUpdates();

    // Update state or managed apps changed outside the Settings window (e.g. from the tray menu)
    event Action? StatusChanged;
}
