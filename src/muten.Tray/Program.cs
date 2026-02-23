namespace muten.Tray;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "muten-single-instance", out bool isNew);
        if (!isNew)
        {
            ApplicationConfiguration.Initialize();
            StartupToast.ShowBlocking("Muten is already running in the background.");
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
