using System.Diagnostics;
using System.Text;

namespace Shigure;

/// <summary>
/// 按 wow_process.txt 中的进程名，从 Windows Z 顺序顶部查找第一个可见顶层窗口。
/// 每次查询都会重新读取配置与窗口顺序，以便运行期间直接切换游戏窗口或修改进程名。
/// </summary>
internal sealed class WowProcessLocator
{
    private const string ProcessFileName = "wow_process.txt";
    private readonly string _processFilePath;

    public WowProcessLocator(string baseDirectory)
    {
        _processFilePath = Path.Combine(baseDirectory, ProcessFileName);
    }

    public string ProcessFilePath => _processFilePath;

    public nint FindFrontmostWindow()
    {
        var processIds = GetCandidateProcessIds();
        if (processIds.Count == 0)
        {
            return 0;
        }

        nint foundWindow = 0;
        _ = NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd))
            {
                return true;
            }

            _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
            if (!processIds.Contains(processId))
            {
                return true;
            }

            foundWindow = hwnd;
            return false;
        }, 0);

        return foundWindow;
    }

    public string? FindFrontmostProcessPath()
    {
        var hwnd = FindFrontmostWindow();
        if (hwnd == 0)
        {
            return null;
        }

        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        return processId == 0 ? null : TryGetProcessPath(processId);
    }

    public string DescribeConfiguredProcesses()
    {
        var names = ReadProcessNames();
        return names.Count == 0 ? "未配置" : string.Join("、", names);
    }

    private HashSet<uint> GetCandidateProcessIds()
    {
        var result = new HashSet<uint>();
        foreach (var processName in ReadProcessNames())
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            foreach (var process in processes)
            {
                using (process)
                {
                    try
                    {
                        result.Add(unchecked((uint)process.Id));
                    }
                    catch (InvalidOperationException)
                    {
                        // 进程可能在枚举期间退出。
                    }
                }
            }
        }

        return result;
    }

    private IReadOnlyList<string> ReadProcessNames()
    {
        try
        {
            return File.ReadLines(_processFilePath)
                .Select(NormalizeProcessName)
                .Where(name => name is not null)
                .Select(name => name!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? NormalizeProcessName(string line)
    {
        var name = line.Trim();
        if (name.Length == 0 || name.StartsWith('#') || name.StartsWith(';'))
        {
            return null;
        }

        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name[..^4].Trim()
            : name;
    }

    private static string? TryGetProcessPath(uint processId)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        if (handle == 0)
        {
            return null;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) && size > 0
                ? buffer.ToString()
                : null;
        }
        finally
        {
            _ = NativeMethods.CloseHandle(handle);
        }
    }
}
