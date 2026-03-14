namespace BusyLight;

internal static class Program
{
    // Named mutex that prevents more than one instance from running at the same time
    private const string SingleInstanceMutexName = "Global\\BusyLight_SingleInstance";

    [STAThread]
    private static void Main()
    {
        using var mutex = new System.Threading.Mutex(
            initiallyOwned: true,
            name: SingleInstanceMutexName,
            out bool createdNew);

        if (!createdNew)
        {
            // Another instance is already running — exit silently
            return;
        }

        // Initialise WinForms (visual styles, DPI awareness, etc.)
        ApplicationConfiguration.Initialize();

        Application.Run(new TrayApplication());
    }
}
