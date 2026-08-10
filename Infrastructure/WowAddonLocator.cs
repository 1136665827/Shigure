namespace Shigure;

/// <summary>
/// 从目标窗口进程定位 Fuyutsui 插件目录与相关文件。
/// </summary>
internal static class WowAddonLocator
{
    private const string InterfaceDirectoryName = "Interface";
    private const string AddOnsDirectoryName = "AddOns";
    private const string AddonDirectoryName = "Fuyutsui";

    public static string? FindClassDirectory(WowProcessLocator processLocator)
    {
        var addonRoot = FindAddonRoot(processLocator);
        if (addonRoot is null)
        {
            return null;
        }

        var classDirectory = Path.Combine(addonRoot, "class");
        return Directory.Exists(classDirectory) ? classDirectory : null;
    }

    /// <summary>定位 Fuyutsui 插件根目录（含 class/、core/）。</summary>
    public static string? FindAddonRoot(WowProcessLocator processLocator)
    {
        var addOnsDirectory = FindAddOnsDirectory(processLocator);
        if (addOnsDirectory is null)
        {
            return null;
        }

        var addonRoot = Path.Combine(addOnsDirectory, AddonDirectoryName);
        return Directory.Exists(addonRoot) ? addonRoot : null;
    }

    /// <summary>
    /// 从目标游戏进程定位 Interface\AddOns。即使 AddOns 或 Fuyutsui 尚未创建，
    /// 也会在找到 Interface 时返回预期路径；最后回退到游戏可执行文件同级目录。
    /// </summary>
    public static string? FindAddOnsDirectory(WowProcessLocator processLocator)
    {
        var exePath = processLocator.FindFrontmostProcessPath();
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return null;
        }

        var executableDirectory = Path.GetDirectoryName(exePath);
        var directory = executableDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var interfaceDirectory = Path.Combine(directory, InterfaceDirectoryName);
            var addOnsDirectory = Path.Combine(interfaceDirectory, AddOnsDirectoryName);
            if (Directory.Exists(addOnsDirectory) || Directory.Exists(interfaceDirectory))
            {
                return addOnsDirectory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return string.IsNullOrWhiteSpace(executableDirectory)
            ? null
            : Path.Combine(executableDirectory, InterfaceDirectoryName, AddOnsDirectoryName);
    }

    public static string? FindClassMacrosPath(WowProcessLocator processLocator)
    {
        var addonRoot = FindAddonRoot(processLocator);
        if (addonRoot is null)
        {
            return null;
        }

        var macrosPath = Path.Combine(addonRoot, "core", "classmacros.lua");
        return File.Exists(macrosPath) ? macrosPath : null;
    }
}
