using Microsoft.Win32;

namespace muten.Tray;

// "Start with Windows": the HKCU Run entry pointing at the running exe, plus its StartupApproved record.
// Recent Windows 11 builds skip Run entries that have no StartupApproved record, and Task Manager doesn't list them.
internal static class StartupRegistry
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "muten";

    // What Task Manager writes: first byte even = enabled, odd = disabled (then a timestamp)
    private static readonly byte[] Enabled = [2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    public static bool IsEnabled()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey, false);
            if (run?.GetValue(ValueName) is null) return false;

            // Disabled in Task Manager counts as off
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, false);
            return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } state || (state[0] & 1) == 0;
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKey, true);
        using var approved = Registry.CurrentUser.CreateSubKey(ApprovedKey, true);
        if (enabled)
        {
            run.SetValue(ValueName, $"\"{Application.ExecutablePath}\"");
            approved.SetValue(ValueName, Enabled, RegistryValueKind.Binary);
        }
        else
        {
            if (run.GetValue(ValueName) is not null) run.DeleteValue(ValueName);
            if (approved.GetValue(ValueName) is not null) approved.DeleteValue(ValueName);
        }
    }
}
