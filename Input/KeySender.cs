namespace Shigure;

public sealed class KeySender : IRuntimeKeyOutput
{
    private readonly string _windowTitle;

    public KeySender(string windowTitle)
    {
        _windowTitle = windowTitle;
    }

    public string? LastFailureReason { get; private set; }

    public bool Send(string hotkey)
    {
        var result = SendCore(hotkey);
        LastFailureReason = result.FailureReason;
        return result.Succeeded;
    }

    public static int? GetVk(string keyName) => WindowsVirtualKeyMap.Resolve(keyName);

    KeySendResult IRuntimeKeyOutput.Send(string hotkey) => SendCore(hotkey);

    private KeySendResult SendCore(string hotkey)
    {
        var (mods, mainKey) = ParseHotkey(hotkey);
        if (mainKey is null)
        {
            return Fail($"无法解析按键“{hotkey}”");
        }

        var vkMain = WindowsVirtualKeyMap.Resolve(mainKey);
        if (vkMain is null)
        {
            return Fail($"无法识别主键“{mainKey}”");
        }

        var hwnd = NativeMethods.FindWindow(null, _windowTitle);
        if (hwnd == 0)
        {
            return Fail($"未找到目标窗口“{_windowTitle}”");
        }

        // ParseHotkey 只产出去重后的 CTRL/ALT/SHIFT, 三者都在虚拟键表里且映射到互异 VK,
        // 故 Resolve 不会为 null、结果天然去重。
        var modVks = mods.Select(m => WindowsVirtualKeyMap.Resolve(m)!.Value).ToList();

        var succeeded = true;
        var firstError = 0;
        void SendMessage(int vk, bool keyUp)
        {
            if (!Post(hwnd, vk, keyUp, out var error))
            {
                succeeded = false;
                if (firstError == 0)
                {
                    firstError = error;
                }
            }
        }

        foreach (var vk in modVks)
        {
            SendMessage(vk, keyUp: false);
        }

        SendMessage(vkMain.Value, keyUp: false);
        SendMessage(vkMain.Value, keyUp: true);

        for (var i = modVks.Count - 1; i >= 0; i--)
        {
            SendMessage(modVks[i], keyUp: true);
        }

        if (succeeded)
        {
            return KeySendResult.Success;
        }

        return Fail(firstError == 5
            ? "权限不足（Win32 错误码 5）：请确认 Shigure 与魔兽世界使用相同的管理员权限运行"
            : $"向目标窗口发送按键失败，Win32 错误码: {firstError}");
    }

    private static (List<string> Mods, string? MainKey) ParseHotkey(string hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
        {
            return (new List<string>(), null);
        }

        var rawParts = hotkey.Trim().Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (rawParts.Length == 0)
        {
            return (new List<string>(), null);
        }

        var mainKey = rawParts[^1];
        var mods = new List<string>();
        foreach (var raw in rawParts[..^1])
        {
            var part = raw.Trim().ToUpperInvariant();
            part = part switch
            {
                "CONTROL" => "CTRL",
                "MENU" => "ALT",
                _ => part
            };

            if (part is "CTRL" or "ALT" or "SHIFT" && !mods.Contains(part))
            {
                mods.Add(part);
            }
        }

        return (mods, mainKey);
    }

    private static KeySendResult Fail(string reason) => KeySendResult.Failure(reason);

    private static bool Post(nint hwnd, int keyCode, bool keyUp, out int error)
    {
        var scanCode = NativeMethods.MapVirtualKeyW((uint)keyCode, 0) & 0xFF;
        var value = 1u | (scanCode << 16);
        if (keyCode == 0x6F) // VK_DIVIDE 是扩展键。
        {
            value |= 1u << 24;
        }

        if (keyUp)
        {
            value |= (1u << 30) | (1u << 31);
        }

        var posted = NativeMethods.PostMessageW(
            hwnd,
            keyUp ? NativeMethods.WmKeyUp : NativeMethods.WmKeyDown,
            (nint)keyCode,
            unchecked((nint)(int)value));
        error = posted ? 0 : System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        return posted;
    }
}
