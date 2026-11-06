using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace muten.Core;

public class AudioSessionManager : IDisposable
{
    private static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IID_IAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
    private const int CLSCTX_ALL = 0x17;

    private readonly IMMDeviceEnumerator _enumerator;
    private readonly DeviceNotificationClient _notificationClient;
    private readonly object _watchLock = new();

    // One session-created registration per active render device
    private List<SessionWatcher> _watchers = [];
    private int _refreshQueued;
    private bool _disposed;

    // Raised on a COM worker thread with the process name of a newly created session
    public event Action<string>? SessionCreated;

    // Raised on a thread-pool thread after render devices were added/removed or the default changed
    public event Action? DevicesChanged;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    private static string? GetProcessExePath(int processId)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public AudioSessionManager()
    {
        // Created and registered on an MTA (thread-pool) thread even when called from the STA UI thread:
        // session notifications are only delivered to registrations made from the MTA
        _enumerator = Task.Run(() => (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator)!)!).Result;
        _notificationClient = new DeviceNotificationClient(this);

        Task.Run(() =>
        {
            try { RefreshWatchers(); } catch { }
            try { _enumerator.RegisterEndpointNotificationCallback(_notificationClient); } catch { }
        }).Wait();
    }

    public IReadOnlyList<AudioSession> GetSessions()
    {
        using var snapshot = TakeSnapshot(null);

        // One entry per process; the default device comes first, an active session beats an inactive one
        var byProcess = new Dictionary<int, SessionEntry>();
        var order = new List<int>();

        foreach (var entry in snapshot.Sessions)
        {
            if (!byProcess.TryGetValue(entry.ProcessId, out var existing))
            {
                byProcess[entry.ProcessId] = entry;
                order.Add(entry.ProcessId);
            }
            else if (!IsActive(existing) && IsActive(entry))
            {
                byProcess[entry.ProcessId] = entry;
            }
        }

        var result = new List<AudioSession>();

        foreach (var processId in order)
        {
            var session = byProcess[processId];

            string processName;
            string? exePath = null;
            string? windowTitle = null;
            try
            {
                var process = Process.GetProcessById(processId);
                processName = process.ProcessName;
                try { exePath = process.MainModule?.FileName; } catch { }
                if (string.IsNullOrEmpty(exePath))
                    exePath = GetProcessExePath(processId);
                var title = process.MainWindowTitle;
                if (!string.IsNullOrWhiteSpace(title)) windowTitle = title;
            }
            catch
            {
                processName = processId == 0 ? "System Sounds" : "Unknown";
            }

            // Friendly name fallback chain: WindowTitle → ProductName → FileDescription → session.DisplayName → ProcessName
            string? friendlyName = windowTitle;
            if (string.IsNullOrWhiteSpace(friendlyName) && !string.IsNullOrEmpty(exePath))
            {
                try
                {
                    var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
                    friendlyName = versionInfo.ProductName;
                    if (string.IsNullOrWhiteSpace(friendlyName) || friendlyName == processName)
                        friendlyName = versionInfo.FileDescription;
                }
                catch { }
            }

            try
            {
                string? sessionDisplayName = session.Control.GetDisplayName(out var name) >= 0 ? name : null;

                var displayName = !string.IsNullOrWhiteSpace(friendlyName) && friendlyName != processName
                    ? friendlyName
                    : !string.IsNullOrWhiteSpace(sessionDisplayName)
                        ? sessionDisplayName
                        : processName;

                result.Add(new AudioSession
                {
                    ProcessId = processId,
                    ProcessName = processName,
                    DisplayName = displayName,
                    Volume = session.Volume.Volume,
                    IsMuted = session.Volume.Mute,
                    IsActive = IsActive(session),
                    ExecutablePath = exePath,
                });
            }
            catch
            {
                // Session expired or its device went away while listing
            }
        }

        return result;
    }

    public bool ToggleMute(int processId)
    {
        using var snapshot = TakeSnapshot(null);
        var volumes = snapshot.Sessions.Where(s => s.ProcessId == processId).Select(s => s.Volume).ToList();
        if (volumes.Count == 0) return false;

        try
        {
            bool mute = !volumes[0].Mute;
            return ActOnSessions(volumes, v => v.Mute = mute);
        }
        catch
        {
            return false;
        }
    }

    public bool Mute(int processId)
    {
        using var snapshot = TakeSnapshot(null);
        return ActOnSessions(snapshot.Sessions.Where(s => s.ProcessId == processId).Select(s => s.Volume), v => v.Mute = true);
    }

    public bool Unmute(int processId)
    {
        using var snapshot = TakeSnapshot(null);
        return ActOnSessions(snapshot.Sessions.Where(s => s.ProcessId == processId).Select(s => s.Volume), v => v.Mute = false);
    }

    public bool SetVolume(int processId, float volume)
    {
        if (volume is < 0f or > 1f) return false;

        using var snapshot = TakeSnapshot(null);
        return ActOnSessions(snapshot.Sessions.Where(s => s.ProcessId == processId).Select(s => s.Volume), v => v.Volume = volume);
    }

    public bool MuteByName(string processName)
    {
        using var snapshot = TakeSnapshot(processName);
        return ActOnSessions(snapshot.Volumes, v => v.Mute = true);
    }

    public bool UnmuteByName(string processName)
    {
        using var snapshot = TakeSnapshot(processName);
        return ActOnSessions(snapshot.Volumes, v => v.Mute = false);
    }

    public bool ToggleMuteByName(string processName)
    {
        using var snapshot = TakeSnapshot(processName);
        return ActOnSessions(snapshot.Volumes, v => v.Mute = !v.Mute);
    }

    public bool SetVolumeByName(string processName, float volume)
    {
        if (volume is < 0f or > 1f) return false;

        using var snapshot = TakeSnapshot(processName);
        return ActOnSessions(snapshot.Volumes, v => v.Volume = volume);
    }

    // Volume of the process's first session (default device first), or null when it has none
    public float? GetVolumeByName(string processName)
    {
        using var snapshot = TakeSnapshot(processName);
        foreach (var volume in snapshot.Volumes)
        {
            try { return volume.Volume; } catch { }
        }
        return null;
    }

    // Every session of the process on every active device; dispose the snapshot when done
    internal SessionSnapshot GetSessionVolumes(string processName) => TakeSnapshot(processName);

    private static bool ActOnSessions(IEnumerable<SessionVolume> volumes, Action<SessionVolume> action)
    {
        bool found = false;
        foreach (var volume in volumes)
        {
            try
            {
                action(volume);
                found = true;
            }
            catch
            {
                // Session expired or its device went away
            }
        }
        return found;
    }

    private static bool IsActive(SessionEntry session) =>
        session.Control.GetState(out var state) >= 0 && state == AudioSessionState.AudioSessionStateActive;

    // Sessions on all active render devices, default device first, optionally only one process's
    private SessionSnapshot TakeSnapshot(string? processName)
    {
        var snapshot = new SessionSnapshot();
        var names = new Dictionary<int, string?>();

        try
        {
            foreach (var device in GetActiveDevices(snapshot))
            {
                var iid = IID_IAudioSessionManager2;
                if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var managerObject) < 0) continue;
                var manager = (IAudioSessionManager2)snapshot.Track(managerObject);

                if (manager.GetSessionEnumerator(out var sessionEnum) < 0) continue;
                snapshot.Track(sessionEnum);
                if (sessionEnum.GetCount(out int count) < 0) continue;

                for (int i = 0; i < count; i++)
                {
                    if (sessionEnum.GetSession(i, out var sessionObject) < 0) continue;
                    snapshot.Track(sessionObject);

                    try
                    {
                        var control = (IAudioSessionControl2)sessionObject;
                        if (control.GetProcessId(out uint pid) < 0) continue;

                        int processId = (int)pid;
                        if (!names.TryGetValue(processId, out var name))
                        {
                            name = TryGetProcessName(processId);
                            names[processId] = name;
                        }

                        if (processName is not null && (name is null || !name.Equals(processName, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        snapshot.Sessions.Add(new SessionEntry(processId, name, control, new SessionVolume((ISimpleAudioVolume)sessionObject)));
                    }
                    catch
                    {
                        // Session expired while enumerating
                    }
                }
            }
        }
        catch
        {
            // Device removed while enumerating
        }

        return snapshot;
    }

    private List<IMMDevice> GetActiveDevices(SessionSnapshot snapshot)
    {
        var devices = new List<(IMMDevice Device, string? Id)>();

        if (_enumerator.EnumAudioEndpoints(DataFlow.Render, DeviceState.Active, out var collection) < 0)
            return [];
        snapshot.Track(collection);

        if (collection.GetCount(out int count) >= 0)
        {
            for (int i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) < 0) continue;
                snapshot.Track(device);
                devices.Add((device, device.GetId(out var id) >= 0 ? id : null));
            }
        }

        string? defaultId = null;
        if (_enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out var defaultDevice) >= 0)
        {
            snapshot.Track(defaultDevice);
            if (defaultDevice.GetId(out var id) >= 0) defaultId = id;
        }

        return devices.OrderBy(d => d.Id == defaultId ? 0 : 1).Select(d => d.Device).ToList();
    }

    private static string? TryGetProcessName(int processId)
    {
        try
        {
            return Process.GetProcessById(processId).ProcessName;
        }
        catch
        {
            return null;
        }
    }

    // Re-registers session notifications on every active render device; runs on MTA threads only
    private void RefreshWatchers()
    {
        lock (_watchLock)
        {
            if (_disposed) return;

            var watchers = new List<SessionWatcher>();
            var devices = new SessionSnapshot();
            try
            {
                foreach (var device in GetActiveDevices(devices))
                {
                    var watcher = SessionWatcher.TryCreate(this, device);
                    if (watcher is not null) watchers.Add(watcher);
                }
            }
            finally
            {
                devices.Dispose();
            }

            // Register the new set before dropping the old one so no session slips through the gap
            var old = _watchers;
            _watchers = watchers;
            foreach (var watcher in old) watcher.Dispose();
        }
    }

    private void OnSessionCreated(object newSession)
    {
        if (((IAudioSessionControl2)newSession).GetProcessId(out uint pid) < 0) return;

        var name = TryGetProcessName((int)pid);
        if (name is not null)
            SessionCreated?.Invoke(name);
    }

    private void QueueDeviceRefresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1) return;

        // Device callbacks must return quickly and must not call back into the enumerator
        Task.Run(async () =>
        {
            // Let a burst of notifications (unplug fires several) settle first
            await Task.Delay(250);
            Interlocked.Exchange(ref _refreshQueued, 0);
            try { RefreshWatchers(); } catch { }
            try { DevicesChanged?.Invoke(); } catch { }
        });
    }

    public void Dispose()
    {
        lock (_watchLock)
        {
            if (_disposed) return;
            _disposed = true;
            try { _enumerator.UnregisterEndpointNotificationCallback(_notificationClient); } catch { }
            foreach (var watcher in _watchers) watcher.Dispose();
            _watchers = [];
        }

        try { Marshal.ReleaseComObject(_enumerator); } catch { }
    }

    private sealed class SessionWatcher : IAudioSessionNotification, IDisposable
    {
        private readonly AudioSessionManager _owner;
        private readonly IAudioSessionManager2 _manager;

        private SessionWatcher(AudioSessionManager owner, IAudioSessionManager2 manager)
        {
            _owner = owner;
            _manager = manager;
        }

        public static SessionWatcher? TryCreate(AudioSessionManager owner, IMMDevice device)
        {
            var iid = IID_IAudioSessionManager2;
            if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var managerObject) < 0) return null;
            var manager = (IAudioSessionManager2)managerObject;

            // Windows only sends session-created notifications once the session list has been requested;
            // the enumerator itself is released right away so it does not keep sessions alive
            if (manager.GetSessionEnumerator(out var sessionEnum) >= 0)
                Marshal.ReleaseComObject(sessionEnum);

            var watcher = new SessionWatcher(owner, manager);
            if (manager.RegisterSessionNotification(watcher) < 0)
            {
                Marshal.ReleaseComObject(managerObject);
                return null;
            }
            return watcher;
        }

        public int OnSessionCreated(object newSession)
        {
            try
            {
                _owner.OnSessionCreated(newSession);
            }
            catch
            {
                // Never let an exception escape into the audio service's callback
            }
            finally
            {
                Marshal.ReleaseComObject(newSession);
            }
            return 0;
        }

        public void Dispose()
        {
            try { _manager.UnregisterSessionNotification(this); } catch { }
            try { Marshal.ReleaseComObject(_manager); } catch { }
        }
    }

    private class DeviceNotificationClient(AudioSessionManager owner) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => owner.QueueDeviceRefresh();

        public void OnDeviceAdded(string pwstrDeviceId) => owner.QueueDeviceRefresh();

        public void OnDeviceRemoved(string deviceId) => owner.QueueDeviceRefresh();

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render) owner.QueueDeviceRefresh();
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
