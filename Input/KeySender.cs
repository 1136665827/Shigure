namespace Shigure;

public sealed class KeySender
{
    private readonly string _windowTitle;

    private static readonly Dictionary<string, int> Vk = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SHIFT"] = 0x10,
        ["CONTROL"] = 0x11,
        ["CTRL"] = 0x11,
        ["MENU"] = 0x12,
        ["ALT"] = 0x12,
        ["XBUTTON1"] = 0x05,
        ["X1"] = 0x05,
        ["MOUSE4"] = 0x05,
        ["XBUTTON2"] = 0x06,
        ["X2"] = 0x06,
        ["MOUSE5"] = 0x06,
        ["F1"] = 0x70,
        ["F2"] = 0x71,
        ["F3"] = 0x72,
        ["F4"] = 0x73,
        ["F5"] = 0x74,
        ["F6"] = 0x75,
        ["F7"] = 0x76,
        ["F8"] = 0x77,
        ["F9"] = 0x78,
        ["F10"] = 0x79,
        ["F11"] = 0x7A,
        ["F12"] = 0x7B,
        ["NUMPAD0"] = 0x60,
        ["NUMPAD1"] = 0x61,
        ["NUMPAD2"] = 0x62,
        ["NUMPAD3"] = 0x63,
        ["NUMPAD4"] = 0x64,
        ["NUMPAD5"] = 0x65,
        ["NUMPAD6"] = 0x66,
        ["NUMPAD7"] = 0x67,
        ["NUMPAD8"] = 0x68,
        ["NUMPAD9"] = 0x69,
        ["NUMPADDECIMAL"] = 0x6E,
        ["NUMPADPLUS"] = 0x6B,
        ["NUMPADMINUS"] = 0x6D,
        ["NUMPADMULTIPLY"] = 0x6A,
        ["NUMPADDIVIDE"] = 0x6F
    };

    private static readonly Dictionary<string, int> CharVk = new()
    {
        [","] = 0xBC,
        ["."] = 0xBE,
        ["/"] = 0xBF,
        [";"] = 0xBA,
        ["'"] = 0xDE,
        ["["] = 0xDB,
        ["]"] = 0xDD,
        ["="] = 0xBB,
        ["-"] = 0xBD,
        ["`"] = 0xC0,
        ["\\"] = 0xDC
    };

    public KeySender(string windowTitle)
    {
        _windowTitle = windowTitle;
    }

    public string? LastFailureReason { get; private set; }

    public bool Send(string hotkey)
    {
        LastFailureReason = null;
        var (mods, mainKey) = ParseHotkey(hotkey);
        if (mainKey is null)
        {
            return Fail($"无法解析按键“{hotkey}”");
        }

        var vkMain = GetVk(mainKey);
        if (vkMain is null)
        {
            return Fail($"无法识别主键“{mainKey}”");
        }

        var hwnd = NativeMethods.FindWindow(null, _windowTitle);
        if (hwnd == 0)
        {
            return Fail($"未找到目标窗口“{_windowTitle}”");
        }

        // ParseHotkey 只产出去重后的 CTRL/ALT/SHIFT, 三者都在 Vk 表里且映射到互异 VK,
        // 故 GetVk 不会为 null、结果天然去重。
        var modVks = mods.Select(m => GetVk(m)!.Value).ToList();

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
            return true;
        }

        return Fail(firstError == 5
            ? "权限不足（Win32 错误码 5）：请确认 Shigure 与魔兽世界使用相同的管理员权限运行"
            : $"向目标窗口发送按键失败，Win32 错误码: {firstError}");
    }

    public static int? GetVk(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName))
        {
            return null;
        }

        var key = keyName.Trim();
        if (Vk.TryGetValue(key, out var mapped))
        {
            return mapped;
        }

        if (key.Length == 1)
        {
            if (CharVk.TryGetValue(key, out var charMapped))
            {
                return charMapped;
            }

            var scan = NativeMethods.VkKeyScanW(key[0]);
            if (scan != -1)
            {
                return scan & 0xFF;
            }
        }

        return null;
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

    private bool Fail(string reason)
    {
        LastFailureReason = reason;
        return false;
    }

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
