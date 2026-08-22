using System.Drawing;
using System.Text.Json;

namespace Shigure;

/// <summary>
/// 技能名称/ID 到本地嵌入式技能图标的只读目录。
/// 图标由 Tools/Download-WowSpellIcons.ps1 从 Wowhead tooltip 与 Zamimg 缓存生成，
/// 因而编辑器运行时不依赖网络。
/// </summary>
internal static class SpellIconCatalog
{
    private static readonly Dictionary<long, Image?> Icons = new();
    private static readonly Dictionary<string, long> SpellIdsByName = LoadSpellIdsByName();

    public static Image? Get(long spellId)
    {
        if (Icons.TryGetValue(spellId, out var cached))
        {
            return cached;
        }

        var resourceName = $"{typeof(SpellIconCatalog).Namespace}.Assets.Spell.spell-{spellId}.jpg";
        using var stream = typeof(SpellIconCatalog).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            Icons[spellId] = null;
            return null;
        }

        using var source = Image.FromStream(stream);
        var icon = new Bitmap(source);
        Icons[spellId] = icon;
        return icon;
    }

    public static Image? Get(string? spellName)
        => TryResolveId(spellName, out var spellId) ? Get(spellId) : null;

    private static bool TryResolveId(string? spellName, out long spellId)
    {
        spellId = 0;
        var normalized = spellName?.Trim();
        return !string.IsNullOrWhiteSpace(normalized)
            && SpellIdsByName.TryGetValue(normalized, out spellId);
    }

    private static Dictionary<string, long> LoadSpellIdsByName()
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        var resourceName = $"{typeof(SpellIconCatalog).Namespace}.Assets.SpellIconManifest.json";
        using var stream = typeof(SpellIconCatalog).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("spells", out var spells)
                || spells.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var spell in spells.EnumerateArray())
            {
                if (!spell.TryGetProperty("spellId", out var idElement)
                    || !idElement.TryGetInt64(out var id)
                    || !spell.TryGetProperty("name", out var nameElement))
                {
                    continue;
                }

                var name = nameElement.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(name) && !result.ContainsKey(name))
                {
                    result[name] = id;
                }
            }
        }
        catch (JsonException)
        {
            // 缺少或损坏清单时由各表格显示空图标，不影响编辑功能。
        }

        return result;
    }
}
