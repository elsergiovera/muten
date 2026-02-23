using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace muten.Core;

public class AudioSessionManager : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator;

    public AudioSessionManager()
    {
        _enumerator = new MMDeviceEnumerator();
    }

    public IReadOnlyList<AudioSession> GetSessions()
    {
        var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var sessionManager = device.AudioSessionManager;
        var sessions = sessionManager.Sessions;
        var result = new List<AudioSession>();

        for (int i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];
            var processId = (int)session.GetProcessID;

            string processName;
            string? exePath = null;
            try
            {
                var process = Process.GetProcessById(processId);
                processName = process.ProcessName;
                try { exePath = process.MainModule?.FileName; } catch { }
            }
            catch
            {
                processName = processId == 0 ? "System Sounds" : "Unknown";
            }

            var displayName = !string.IsNullOrWhiteSpace(session.DisplayName)
                ? session.DisplayName
                : processName;

            result.Add(new AudioSession
            {
                ProcessId = processId,
                ProcessName = processName,
                DisplayName = displayName,
                Volume = session.SimpleAudioVolume.Volume,
                IsMuted = session.SimpleAudioVolume.Mute,
                IsActive = session.State == AudioSessionState.AudioSessionStateActive,
                ExecutablePath = exePath,
            });
        }

        return result;
    }

    public bool ToggleMute(int processId)
    {
        var session = FindSession(processId);
        if (session is null) return false;

        session.SimpleAudioVolume.Mute = !session.SimpleAudioVolume.Mute;
        return true;
    }

    public bool Mute(int processId)
    {
        var session = FindSession(processId);
        if (session is null) return false;

        session.SimpleAudioVolume.Mute = true;
        return true;
    }

    public bool Unmute(int processId)
    {
        var session = FindSession(processId);
        if (session is null) return false;

        session.SimpleAudioVolume.Mute = false;
        return true;
    }

    public bool SetVolume(int processId, float volume)
    {
        if (volume is < 0f or > 1f) return false;

        var session = FindSession(processId);
        if (session is null) return false;

        session.SimpleAudioVolume.Volume = volume;
        return true;
    }

    public bool MuteByName(string processName)
    {
        return ActOnSessionsByName(processName, s => s.SimpleAudioVolume.Mute = true);
    }

    public bool UnmuteByName(string processName)
    {
        return ActOnSessionsByName(processName, s => s.SimpleAudioVolume.Mute = false);
    }

    public bool ToggleMuteByName(string processName)
    {
        return ActOnSessionsByName(processName, s => s.SimpleAudioVolume.Mute = !s.SimpleAudioVolume.Mute);
    }

    public bool SetVolumeByName(string processName, float volume)
    {
        if (volume is < 0f or > 1f) return false;
        return ActOnSessionsByName(processName, s => s.SimpleAudioVolume.Volume = volume);
    }

    private bool ActOnSessionsByName(string processName, Action<AudioSessionControl> action)
    {
        var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var sessions = device.AudioSessionManager.Sessions;
        bool found = false;

        for (int i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];
            var pid = (int)session.GetProcessID;

            string name;
            try
            {
                name = Process.GetProcessById(pid).ProcessName;
            }
            catch
            {
                continue;
            }

            if (name.Equals(processName, StringComparison.OrdinalIgnoreCase))
            {
                action(session);
                found = true;
            }
        }

        return found;
    }

    private AudioSessionControl? FindSession(int processId)
    {
        var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var sessions = device.AudioSessionManager.Sessions;

        for (int i = 0; i < sessions.Count; i++)
        {
            if ((int)sessions[i].GetProcessID == processId)
                return sessions[i];
        }

        return null;
    }

    public void Dispose()
    {
        _enumerator.Dispose();
    }
}
