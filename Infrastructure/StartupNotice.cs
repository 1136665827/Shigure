namespace Shigure;

internal static class StartupNotice
{
    // 在这里填写首次打开或版本升级时展示的信息文本。
    private const string NoticeText = """
        1.2.1.11
        - 添加了首领战的编号
        - 添加了常用字段的参考
        - 添加了鼠标单位的信息显示
        
        1.2.1.10
        - 重要: 修改了施法时间和引导时间的比例, 1秒 = 10, 所有打断都需要修改。
        - 施法修改为:施法(倒计时), 添加了:施法(正计时)。
        - 可以监控boss的一些信息。
        - 优化了UI界面。

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
