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

        var options = AppOptions.FromArgs(args);
        var baseDirectory = AppPaths.BaseDirectory;
        var moduleStore = new ModuleStore(ModuleStore.ResolveModuleDirectory(baseDirectory));
        var triggerKeyState = new WindowsTriggerKeyState();
        var processLocator = new WowProcessLocator(baseDirectory);
        var runtimeFactory = new ShigureRuntimeFactory(baseDirectory, moduleStore, triggerKeyState, processLocator);
        var runtimeSession = new RuntimeSessionCoordinator(runtimeFactory);

        Application.Run(new MainForm(
            options,
            baseDirectory,
            moduleStore,
            triggerKeyState,
            processLocator,
            runtimeSession));
    }
}
