namespace Shigure;

internal static class StartupNotice
{
    // 在这里填写首次打开或版本升级时展示的信息文本。
    private const string NoticeText = """
        1.2.1.12
        - 修复了在某些情况下无法正确加载配置文件的问题。
        - 不再需要检测"救赎之魂"buff, 现在可以在不使用该buff的情况下正常筛选单位
        
        """;

    public static void ShowIfNeeded()
    {
        try
        {
            var cache = UiCacheStore.Load();
            var currentVersion = AppInfo.Version;
            if (!ShouldShow(cache.LastShownVersion, currentVersion))
            {
                return;
            }

            MessageBox.Show(
                NoticeText,
                $"{AppInfo.AppName} v{currentVersion}",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            cache.LastShownVersion = currentVersion;
            UiCacheStore.Save(cache);
        }
        catch
        {
            // 提示或缓存异常时静默跳过，避免影响应用启动。
        }
    }

    private static bool ShouldShow(string? cachedVersion, string currentVersion)
    {
        if (string.IsNullOrWhiteSpace(cachedVersion))
        {
            return true;
        }

        if (System.Version.TryParse(currentVersion, out var current)
            && System.Version.TryParse(cachedVersion, out var cached))
        {
            return cached < current;
        }

        // 缓存损坏或版本格式发生变化时，仅在文本不一致时重新提示一次。
        return !string.Equals(cachedVersion, currentVersion, StringComparison.OrdinalIgnoreCase);
    }
}
