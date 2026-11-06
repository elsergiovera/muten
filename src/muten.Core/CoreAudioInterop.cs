using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace muten.Core;

// Minimal Core Audio COM interop, used instead of NAudio's wrappers so every reference can be
// released deterministically. While this process still holds a reference to an exited app's
// session, Windows does not report that app's next session as created.
// Unused vtable slots are declared with placeholder signatures and must never be called.

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(DataFlow dataFlow, DeviceState stateMask, out IMMDeviceCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(DataFlow dataFlow, Role role, out IMMDevice endpoint);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int Item(int index, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
}

[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint streamFlags, out IntPtr sessionControl);
    [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint streamFlags, out IntPtr audioVolume);
    [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
    [PreserveSig] int RegisterSessionNotification(IAudioSessionNotification notification);
    [PreserveSig] int UnregisterSessionNotification(IAudioSessionNotification notification);
}

[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
}

[ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    [PreserveSig] int GetState(out AudioSessionState state);
    [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
    [PreserveSig] int SetDisplayName(IntPtr displayName, IntPtr eventContext);
    [PreserveSig] int GetIconPath(out IntPtr iconPath);
    [PreserveSig] int SetIconPath(IntPtr iconPath, IntPtr eventContext);
    [PreserveSig] int GetGroupingParam(out Guid groupingId);
    [PreserveSig] int SetGroupingParam(IntPtr groupingId, IntPtr eventContext);
    [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
    [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
    [PreserveSig] int GetSessionIdentifier(out IntPtr id);
    [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
    [PreserveSig] int GetProcessId(out uint processId);
}

[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    [PreserveSig] int SetMasterVolume(float level, ref Guid eventContext);
    [PreserveSig] int GetMasterVolume(out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool isMuted, ref Guid eventContext);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool isMuted);
}

[ComImport, Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionNotification
{
    [PreserveSig] int OnSessionCreated([MarshalAs(UnmanagedType.IUnknown)] object newSession);
}

// Volume control of one session; valid until the snapshot it came from is disposed
internal sealed class SessionVolume(ISimpleAudioVolume volume)
{
    public float Volume
    {
        get
        {
            Marshal.ThrowExceptionForHR(volume.GetMasterVolume(out var level));
            return level;
        }
        set
        {
            var context = Guid.Empty;
            Marshal.ThrowExceptionForHR(volume.SetMasterVolume(Math.Clamp(value, 0f, 1f), ref context));
        }
    }

    public bool Mute
    {
        get
        {
            Marshal.ThrowExceptionForHR(volume.GetMute(out var muted));
            return muted;
        }
        set
        {
            var context = Guid.Empty;
            Marshal.ThrowExceptionForHR(volume.SetMute(value, ref context));
        }
    }
}

internal sealed record SessionEntry(int ProcessId, string? ProcessName, IAudioSessionControl2 Control, SessionVolume Volume);

// Sessions on all active render devices (default device first); Dispose releases every COM reference taken
internal sealed class SessionSnapshot : IDisposable
{
    private readonly List<object> _comObjects = [];

    public List<SessionEntry> Sessions { get; } = [];

    public IEnumerable<SessionVolume> Volumes => Sessions.Select(s => s.Volume);

    public T Track<T>(T comObject) where T : class
    {
        _comObjects.Add(comObject);
        return comObject;
    }

    public void Dispose()
    {
        // ReleaseComObject (not Final) keeps references shared with concurrent snapshots balanced
        foreach (var comObject in _comObjects)
        {
            try { Marshal.ReleaseComObject(comObject); } catch { }
        }
        _comObjects.Clear();
        Sessions.Clear();
    }
}
