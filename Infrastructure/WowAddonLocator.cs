using System.Text;

namespace Shigure;

/// <summary>
/// 从目标窗口进程定位 Fuyutsui 的 class 目录。
/// </summary>
internal static class WowAddonLocator
{
    private const string AddonRelativePath = @"Interface\AddOns\Fuyutsui\class";

    public static string? FindClassDirectory(string windowTitle)
    {
        var exePath = TryGetProcessPathByWindowTitle(windowTitle);
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(exePath);
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var classDirectory = Path.Combine(directory, AddonRelativePath);
            if (Directory.Exists(classDirectory))
            {
                return classDirectory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    private static string? TryGetProcessPathByWindowTitle(string windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle))
        {
            return null;
        }

        var hwnd = NativeMethods.FindWindow(null, windowTitle.Trim());
        if (hwnd == 0)
        {
            return null;
        }

        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0)
        {
            return null;
        }

        var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        if (handle == 0)
        {
            return null;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            if (!NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) || size <= 0)
            {
                return null;
            }

            return buffer.ToString();
        }
        finally
        {
            _ = NativeMethods.CloseHandle(handle);
        }
    }
}
