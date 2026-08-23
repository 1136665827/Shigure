using System.Drawing;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Shigure;

/// <summary>
/// 技能名称/ID 到技能图标的目录。优先读取嵌入资源；未知 ID 会在后台从 Wowhead
/// 解析图标并按图标资源名缓存，因此多个 spellId 共用一份图片。
/// </summary>
internal static class SpellIconCatalog
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<long, Image> Icons = new();
    private static readonly Dictionary<string, Image?> NamedIcons = new(StringComparer.Ordinal);
    private static readonly HashSet<long> PendingDownloads = new();
    private static readonly Dictionary<long, DateTime> RetryAfter = new();
    private static readonly CatalogData Catalog = LoadCatalog();
    private static readonly Dictionary<string, long> SpellIdsByName = Catalog.SpellIdsByName;
    private static readonly Dictionary<long, string> IconTargetsBySpellId = Catalog.TargetsBySpellId;
    private static readonly string RuntimeCacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Shigure",
        "SpellIcons");
    private static readonly string RuntimeIndexPath = Path.Combine(RuntimeCacheDirectory, "index.json");
    private static readonly Dictionary<long, string> RuntimeIconsBySpellId = LoadRuntimeIndex();
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static readonly Dictionary<long, string> SpellIdIconResources = new()
    {
        [35395] = $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.crusader-strike.png"
    };

    private static readonly Dictionary<string, string> NamedIconResources = new(StringComparer.Ordinal)
    {
        ["银月城生命药水"] = $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.silvermoon-city-health-potion.png",
        [ModuleSpecialActions.OneKeySpell] = $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.one-key-spell.png",
        [ModuleSpecialActions.PauseSpell] = $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.pause.png",
        [ModuleSpecialActions.FailedSpell] = $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.auto-insert-spell.png",
        ["鲁莽药水"] = $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.recklessness-potion.jpg",
        ["圣光潜力"] = $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.lights-potential.jpg",
        ["光注法力药水"] = $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.light-infused-mana-potion.jpg",
        ["十字军打击"] = $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.crusader-strike.png",
        ["停止施法"] = $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.stop-casting.png"
    };

    private static readonly string LastRuleRowIconResource =
        $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.last-rule-row.png";

    internal static event Action<long>? IconAvailable;

    public static Image? Get(long spellId)
    {
        if (spellId <= 0)
        {
            return null;
        }

        lock (SyncRoot)
        {
            if (Icons.TryGetValue(spellId, out var cached))
            {
                return cached;
            }
        }

        var local = LoadCatalogIcon(spellId);
        if (local is not null)
        {
            return Cache(spellId, local);
        }

        string? runtimeIcon;
        lock (SyncRoot)
        {
            RuntimeIconsBySpellId.TryGetValue(spellId, out runtimeIcon);
        }

        if (!string.IsNullOrWhiteSpace(runtimeIcon))
        {
            var diskIcon = LoadImageFile(GetRuntimeIconPath(runtimeIcon));
            if (diskIcon is not null)
            {
                return Cache(spellId, diskIcon);
            }
        }

        QueueDownload(spellId);
        return null;
    }

    public static Image? Get(string? spellName)
    {
        var normalized = spellName?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (NamedIconResources.TryGetValue(normalized, out var resourceName))
        {
            return GetNamedIcon(normalized, resourceName);
        }

        long spellId;
        lock (SyncRoot)
        {
            if (!SpellIdsByName.TryGetValue(normalized, out spellId))
            {
                return null;
            }
        }

        return Get(spellId);
    }

    public static void Register(long spellId, string? spellName)
    {
        var normalized = spellName?.Trim();
        if (spellId <= 0 || string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        lock (SyncRoot)
        {
            SpellIdsByName[normalized] = spellId;
        }
    }

    public static Image? GetLastRuleRowIcon()
        => GetNamedIcon("last-rule-row", LastRuleRowIconResource);

    private static Image? LoadCatalogIcon(long spellId)
    {
        string? resourceName;
        lock (SyncRoot)
        {
            resourceName = SpellIdIconResources.GetValueOrDefault(spellId);
        }

        if (resourceName is not null)
        {
            return LoadResource(resourceName);
        }

        string? target;
        lock (SyncRoot)
        {
            target = IconTargetsBySpellId.GetValueOrDefault(spellId);
        }

        if (!string.IsNullOrWhiteSpace(target))
        {
            var assetRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Assets"));
            var iconPath = Path.GetFullPath(Path.Combine(assetRoot, target.Replace('/', Path.DirectorySeparatorChar)));
            var spellRoot = Path.GetFullPath(Path.Combine(assetRoot, "Spell"));
            if (string.Equals(Path.GetDirectoryName(iconPath), spellRoot, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetExtension(iconPath), ".jpg", StringComparison.OrdinalIgnoreCase))
            {
                var icon = LoadImageFile(iconPath);
                if (icon is not null)
                {
                    return icon;
                }
            }
        }

        // Compatibility with the earlier per-spell embedded resource layout.
        resourceName = $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.spell-{spellId}.jpg";
        return LoadResource(resourceName);
    }

    private static Image Cache(long spellId, Image icon)
    {
        lock (SyncRoot)
        {
            if (Icons.TryGetValue(spellId, out var cached))
            {
                icon.Dispose();
                return cached;
            }

            Icons[spellId] = icon;
            return icon;
        }
    }

    private static Image? GetNamedIcon(string cacheKey, string resourceName)
    {
        lock (SyncRoot)
        {
            if (NamedIcons.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }
        }

        var icon = LoadResource(resourceName);
        lock (SyncRoot)
        {
            NamedIcons[cacheKey] = icon;
        }

        return icon;
    }

    private static Image? LoadResource(string resourceName)
    {
        using var stream = typeof(SpellIconCatalog).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        try
        {
            using var source = Image.FromStream(stream);
            return new Bitmap(source);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static Image? LoadImageFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var source = Image.FromFile(path);
            return new Bitmap(source);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            return null;
        }
    }

    private static void QueueDownload(long spellId)
    {
        lock (SyncRoot)
        {
            if (PendingDownloads.Contains(spellId)
                || RetryAfter.TryGetValue(spellId, out var retryAfter) && retryAfter > DateTime.UtcNow)
            {
                return;
            }

            PendingDownloads.Add(spellId);
        }

        _ = DownloadAndCacheAsync(spellId);
    }

    private static async Task DownloadAndCacheAsync(long spellId)
    {
        try
        {
            using var tooltipResponse = await HttpClient.GetAsync(
                $"https://nether.wowhead.com/tooltip/spell/{spellId}?dataEnv=1").ConfigureAwait(false);
            tooltipResponse.EnsureSuccessStatusCode();
            await using var tooltipStream = await tooltipResponse.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var tooltip = await JsonDocument.ParseAsync(tooltipStream).ConfigureAwait(false);
            if (!tooltip.RootElement.TryGetProperty("icon", out var iconElement))
            {
                throw new InvalidDataException("Spell tooltip did not contain an icon.");
            }

            var iconName = NormalizeIconName(iconElement.GetString());
            if (iconName is null)
            {
                throw new InvalidDataException("Spell tooltip returned an invalid icon name.");
            }

            Directory.CreateDirectory(RuntimeCacheDirectory);
            var target = GetRuntimeIconPath(iconName);
            if (!File.Exists(target))
            {
                var bytes = await HttpClient.GetByteArrayAsync(
                    $"https://wow.zamimg.com/images/wow/icons/large/{iconName}.jpg").ConfigureAwait(false);
                if (bytes.Length < 512)
                {
                    throw new InvalidDataException("Downloaded icon is unexpectedly small.");
                }

                using (var memory = new MemoryStream(bytes, writable: false))
                using (Image.FromStream(memory))
                {
                    // Decode before persisting so error payloads never enter the cache.
                }

                var temporary = $"{target}.{Guid.NewGuid():N}.download";
                await File.WriteAllBytesAsync(temporary, bytes).ConfigureAwait(false);
                File.Move(temporary, target, overwrite: true);
            }

            var image = LoadImageFile(target)
                ?? throw new InvalidDataException("Downloaded icon could not be decoded.");
            Cache(spellId, image);

            lock (SyncRoot)
            {
                RuntimeIconsBySpellId[spellId] = iconName;
                RetryAfter.Remove(spellId);
                SaveRuntimeIndex();
            }

            IconAvailable?.Invoke(spellId);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or IOException or JsonException or InvalidDataException or ArgumentException)
        {
            lock (SyncRoot)
            {
                RetryAfter[spellId] = DateTime.UtcNow.AddMinutes(5);
            }
        }
        finally
        {
            lock (SyncRoot)
            {
                PendingDownloads.Remove(spellId);
            }
        }
    }

    private static string? NormalizeIconName(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return !string.IsNullOrWhiteSpace(normalized)
            && normalized.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
                ? normalized
                : null;
    }

    private static string GetRuntimeIconPath(string iconName)
        => Path.Combine(RuntimeCacheDirectory, $"icon-{iconName}.jpg");

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Shigure", "1.0"));
        return client;
    }

    private static Dictionary<long, string> LoadRuntimeIndex()
    {
        try
        {
            if (!File.Exists(RuntimeIndexPath))
            {
                return new Dictionary<long, string>();
            }

            return JsonSerializer.Deserialize<Dictionary<long, string>>(File.ReadAllText(RuntimeIndexPath))
                ?? new Dictionary<long, string>();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Dictionary<long, string>();
        }
    }

    private static void SaveRuntimeIndex()
    {
        Directory.CreateDirectory(RuntimeCacheDirectory);
        var temporary = $"{RuntimeIndexPath}.download";
        File.WriteAllText(temporary, JsonSerializer.Serialize(RuntimeIconsBySpellId));
        File.Move(temporary, RuntimeIndexPath, overwrite: true);
    }

    private static CatalogData LoadCatalog()
    {
        var result = new CatalogData();
        try
        {
            var manifestPath = Path.Combine(AppContext.BaseDirectory, "Assets", "SpellIconManifest.json");
            using var stream = File.OpenRead(manifestPath);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("spells", out var spells)
                || spells.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var spell in spells.EnumerateArray())
            {
                if (!spell.TryGetProperty("spellId", out var idElement)
                    || !idElement.TryGetInt64(out var id))
                {
                    continue;
                }

                if (spell.TryGetProperty("name", out var nameElement))
                {
                    var name = nameElement.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(name) && !result.SpellIdsByName.ContainsKey(name))
                    {
                        result.SpellIdsByName[name] = id;
                    }
                }

                if (spell.TryGetProperty("target", out var targetElement))
                {
                    var target = targetElement.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(target))
                    {
                        result.TargetsBySpellId[id] = target;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 缺少或损坏清单时继续使用自定义图标和在线回退。
        }

        return result;
    }

    private sealed class CatalogData
    {
        public Dictionary<string, long> SpellIdsByName { get; } = new(StringComparer.Ordinal);
        public Dictionary<long, string> TargetsBySpellId { get; } = new();
    }

}
