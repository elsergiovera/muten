using Microsoft.Win32;

namespace muten.Tray;

// "Start with Windows": the HKCU Run entry pointing at the running exe
internal static class StartupRegistry
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "muten";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) is not null;
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled)
            key.SetValue(ValueName, $"\"{Application.ExecutablePath}\"");
        else if (key.GetValue(ValueName) is not null)
            key.DeleteValue(ValueName);
    }
}
