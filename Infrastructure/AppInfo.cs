using System.Reflection;

namespace Shigure;

/// <summary>
/// 应用名称与版本信息的单一来源：AppName 取自程序集名，
/// Version 优先取 csproj 的 &lt;Version&gt;(AssemblyInformationalVersion)，退化到 AssemblyVersion。
/// </summary>
internal static class AppInfo
{
    // 取自程序集名（重命名程序集时自动跟随），与 Version 从程序集读取的惯例一致。
    public static string AppName { get; } = Assembly.GetExecutingAssembly().GetName().Name ?? "Shigure";

    public static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // 去掉构建元数据后缀(如 "1.1.0+abc123")。
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "未知";
    }
}
