namespace Shigure;

internal static class AppPaths
{
    public static string BaseDirectory => AppContext.BaseDirectory;

    public static string UserDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        AppInfo.AppName);
}
