using System.Diagnostics;

namespace muten.Tray;

static class Program
{
    private const string MutexName = "muten-single-instance";

    [STAThread]
    static void Main(string[] args)
    {
        // Started by the updater: "--updated <pid of the previous muten>"
        int updatedFrom = ParseUpdatedFrom(args);
        if (updatedFrom > 0) WaitForExit(updatedFrom);

        using var mutex = new Mutex(true, MutexName, out bool isNew);
        if (!isNew && updatedFrom > 0) isNew = TryAcquire(mutex);

        ApplicationConfiguration.Initialize();
        if (!isNew)
        {
            StartupToast.ShowBlocking("Muten is already running in the background.");
            return;
        }

        Application.Run(new TrayApplicationContext(afterUpdate: updatedFrom > 0));
    }

    private static int ParseUpdatedFrom(string[] args)
    {
        int i = Array.FindIndex(args, a => a.Equals("--updated", StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int pid) ? pid : 0;
    }

    private static void WaitForExit(int pid)
    {
        try
        {
            using var previous = Process.GetProcessById(pid);
            previous.WaitForExit(15000);
        }
        catch
        {
            // Already gone
        }
    }

    // The previous instance releases the mutex when it exits
    private static bool TryAcquire(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(15000);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }
}
