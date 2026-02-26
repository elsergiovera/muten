using System.Diagnostics;
using System.Runtime.InteropServices;

namespace muten.Core;

public class ForegroundWatcher : IDisposable
{
    public event Action<string, int, string?>? ForegroundChanged;

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    private IntPtr _hook;
    private WinEventDelegate? _callback;

    public void Start()
    {
        _callback = OnWinEvent;
        _hook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    private void OnWinEvent(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == (uint)Environment.ProcessId) return;

            var process = Process.GetProcessById((int)pid);
            string? exePath = null;
            try { exePath = process.MainModule?.FileName; } catch { }
            ForegroundChanged?.Invoke(process.ProcessName, (int)pid, exePath);
        }
        catch
        {
            // Process may have exited between detection and lookup
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
