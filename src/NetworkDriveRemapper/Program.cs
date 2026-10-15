using NetworkDriveRemapper.Core;

namespace NetworkDriveRemapper;

/// <summary>Creates the desktop application in the interactive user's ordinary logon session.</summary>
internal static class Program
{
    /// <summary>Initializes DPI-aware WinForms and starts the drive-letter browser on an STA thread.</summary>
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm(new DriveMappingService(new WindowsNetworkDriveSystem())));
    }
}
