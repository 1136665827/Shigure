namespace Shigure;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            MessageBox.Show(
                "Shigure 需要在 Windows 上运行。",
                "Shigure",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        ApplicationConfiguration.Initialize();

        var relaunchResult = RandomizedExecutableLauncher.TryRelaunch(args);
        if (relaunchResult == RandomizedRelaunchResult.Started)
        {
            return;
        }

        if (relaunchResult == RandomizedRelaunchResult.Failed)
        {
            return;
        }

        Application.Run(new MainForm(AppOptions.FromArgs(args)));
    }
}
