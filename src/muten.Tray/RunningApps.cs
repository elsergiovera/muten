using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using muten.Core;

namespace muten.Tray;

public record RunningApp(string ProcessName, string DisplayName, string? ExePath, bool Playing);

// Apps the "＋ Add" picker offers: running processes with a visible window or an audio session
public static class RunningApps
{
    // Shell and system hosts that own visible windows but are never something to mute
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "ApplicationFrameHost", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost",
        "SearchApp", "TextInputHost", "LockApp", "SystemSettingsBroker", "dwm", "csrss", "Idle", "System",
    };

    public static List<RunningApp> Find(IEnumerable<AudioSession> sessions, Func<string, bool> isManaged)
    {
        var self = Process.GetCurrentProcess().ProcessName;
        var apps = new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);

        bool Skip(string name) =>
            string.IsNullOrEmpty(name) || name == "Unknown" || name == "System Sounds"
            || name.Equals(self, StringComparison.OrdinalIgnoreCase) || Ignored.Contains(name) || isManaged(name);

        foreach (var session in sessions)
        {
            if (session.ProcessId == 0 || Skip(session.ProcessName)) continue;

            apps.TryGetValue(session.ProcessName, out var existing);
            var exePath = existing?.ExePath ?? session.ExecutablePath;
            apps[session.ProcessName] = new RunningApp(
                session.ProcessName,
                FriendlyName(session.ProcessName, exePath) ?? session.DisplayName,
                exePath,
                (existing?.Playing ?? false) || session.IsActive);
        }

        foreach (var pid in WindowProcessIds())
        {
            string name;
            try
            {
                using var process = Process.GetProcessById(pid);
                name = process.ProcessName;
            }
            catch
            {
                continue;
            }

            if (Skip(name) || apps.ContainsKey(name)) continue;

            var exePath = GetExePath(pid);
            apps[name] = new RunningApp(name, FriendlyName(name, exePath) ?? name, exePath, Playing: false);
        }

        return apps.Values
            .OrderByDescending(a => a.Playing)
            .ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    // FileDescription, else ProductName of the exe; null when neither is useful
    public static string? FriendlyName(string processName, string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return null;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            foreach (var candidate in new[] { info.FileDescription, info.ProductName })
            {
                if (!string.IsNullOrWhiteSpace(candidate) && candidate.Trim().Length > 1)
                    return candidate.Trim();
            }
        }
        catch { }
        return null;
    }

    // Process ids owning a visible, unowned, uncloaked top-level window with a title
    private static HashSet<int> WindowProcessIds()
    {
        var ids = new HashSet<int>();
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd) || GetWindow(hWnd, GW_OWNER) != IntPtr.Zero) return true;
            if (GetWindowTextLength(hWnd) == 0) return true;
            if ((GetWindowLong(hWnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return true;
            if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;

            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid != 0) ids.Add((int)pid);
            return true;
        }, IntPtr.Zero);
        return ids;
    }

    public static string? GetExePath(int processId)
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

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    private const uint GW_OWNER = 4;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int DWMWA_CLOAKED = 14;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
}
