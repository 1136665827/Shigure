using System.Text;

namespace Shigure;

/// <summary>
/// 从目标窗口进程定位 Fuyutsui 插件目录与相关文件。
/// </summary>
internal static class WowAddonLocator
{
    private const string AddonRelativePath = @"Interface\AddOns\Fuyutsui";
    private const string ClassRelativePath = @"Interface\AddOns\Fuyutsui\class";
    private const string ClassMacrosRelativePath = @"Interface\AddOns\Fuyutsui\core\classmacros.lua";

    public static string? FindClassDirectory(string windowTitle)
    {
        var addonRoot = FindAddonRoot(windowTitle);
        if (addonRoot is null)
        {
            return null;
        }

        var classDirectory = Path.Combine(addonRoot, "class");
        return Directory.Exists(classDirectory) ? classDirectory : null;
    }

    /// <summary>定位 Fuyutsui 插件根目录（含 class/、core/）。</summary>
    public static string? FindAddonRoot(string windowTitle)
    {
        var exePath = TryGetProcessPathByWindowTitle(windowTitle);
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(exePath);
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var addonRoot = Path.Combine(directory, AddonRelativePath);
            if (Directory.Exists(addonRoot))
            {
                return addonRoot;
            }

            // 兼容旧探测：仅存在 class 子目录时回退到其父目录。
            var classDirectory = Path.Combine(directory, ClassRelativePath);
            if (Directory.Exists(classDirectory))
            {
                return Path.GetDirectoryName(classDirectory);
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    public static string? FindClassMacrosPath(string windowTitle)
    {
        var addonRoot = FindAddonRoot(windowTitle);
        if (addonRoot is not null)
        {
            var fromRoot = Path.Combine(addonRoot, "core", "classmacros.lua");
            if (File.Exists(fromRoot))
            {
                return fromRoot;
            }
        }

        var exePath = TryGetProcessPathByWindowTitle(windowTitle);
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(exePath);
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var macrosPath = Path.Combine(directory, ClassMacrosRelativePath);
            if (File.Exists(macrosPath))
            {
                return macrosPath;
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
