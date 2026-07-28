using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static Shigure.LuaLiteParser;

namespace Shigure;

/// <summary>
/// 将 Fuyutsui core/classmacros.lua 的 ClassMacros 展开为 keymap/*.json
/// （对齐 core/macro.lua CreateMacro 的槽位与热键池）。
/// </summary>
internal static partial class FuyutsuiKeymapConverter
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly string[] Modifiers =
    [
        "CTRL", "ALT", "SHIFT",
        "ALT-CTRL", "ALT-SHIFT", "CTRL-SHIFT",
        "ALT-CTRL-SHIFT"
    ];

    private static readonly string[] Keys =
    [
        "NUMPAD1", "NUMPAD2", "NUMPAD3", "NUMPAD4", "NUMPAD5",
        "NUMPAD6", "NUMPAD7", "NUMPAD8", "NUMPAD9", "NUMPAD0",
        "NUMPADDECIMAL", "NUMPADPLUS", "NUMPADMINUS", "NUMPADMULTIPLY", "NUMPADDIVIDE",
        "F1", "F2", "F3", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        ",", ".", "/", ";", "'", "[", "]", "\\",
        "7", "8", "9", "0", "="
    ];

    private static readonly string[] MacroKind = BuildMacroKind();

    private static readonly Dictionary<string, int> ClassFileToId = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WARRIOR"] = 1,
        ["PALADIN"] = 2,
        ["HUNTER"] = 3,
        ["ROGUE"] = 4,
        ["PRIEST"] = 5,
        ["DEATHKNIGHT"] = 6,
        ["SHAMAN"] = 7,
        ["MAGE"] = 8,
        ["WARLOCK"] = 9,
        ["MONK"] = 10,
        ["DRUID"] = 11,
        ["DEMONHUNTER"] = 12,
        ["EVOKER"] = 13
    };

    public sealed record UpdateResult(
        string ClassMacrosPath,
        IReadOnlyList<string> UpdatedFiles,
        IReadOnlyList<string> Warnings);

    public static UpdateResult UpdateFromClassMacros(string classMacrosPath, string keymapDirectory)
    {
        if (!File.Exists(classMacrosPath))
        {
            throw new FileNotFoundException($"找不到 classmacros.lua: {classMacrosPath}", classMacrosPath);
        }

        Directory.CreateDirectory(keymapDirectory);
        var lua = File.ReadAllText(classMacrosPath, Encoding.UTF8);
        var classMacros = ExtractAssignedTable(lua, "Fuyutsui.ClassMacros")
            ?? throw new InvalidDataException("classmacros.lua 中未找到 Fuyutsui.ClassMacros");

        var updated = new List<string>();
        var warnings = new List<string>();

        foreach (var (classFile, classId) in ClassFileToId)
        {
            if (classMacros.GetTable(classFile) is not { } classTable)
            {
                warnings.Add($"跳过 {classFile}: ClassMacros 中无此职业表");
                continue;
            }

            var fileName = ClassNames.GetConfigFileName(classId).ToLowerInvariant() + ".json";
            var jsonPath = Path.Combine(keymapDirectory, fileName);
            var existing = LoadExistingSpellNames(jsonPath);

            var (root, classWarnings) = CompileClassKeymap(classTable, existing, classFile);
            warnings.AddRange(classWarnings);

            File.WriteAllText(jsonPath, root.ToJsonString(WriteOptions) + Environment.NewLine, Encoding.UTF8);
            updated.Add(jsonPath);
        }

        if (updated.Count == 0)
        {
            throw new InvalidOperationException("未成功转换任何职业 keymap。");
        }

        return new UpdateResult(classMacrosPath, updated, warnings);
    }

    private static (JsonObject Root, List<string> Warnings) CompileClassKeymap(
        TableValue classTable,
        IReadOnlyDictionary<int, string> existingSpellNames,
        string classFile)
    {
        var warnings = new List<string>();
        var dynamicSpells = ReadArrayStrings(classTable.GetTable("dynamicSpells"));
        var staticSpells = ReadSparseStrings(classTable.GetTable("staticSpells"));
        var specialSpells = ReadSparseStrings(classTable.GetTable("specialSpells"));
        var dynamicSlots = dynamicSpells.Count * 30;

        var root = new JsonObject();
        for (var i = 1; i <= MacroKind.Length; i++)
        {
            var hotkey = MacroKind[i - 1];
            var unit = 0;
            var spell = string.Empty;

            if (i <= dynamicSlots)
            {
                var groupIndex = (i - 1) / 30;
                var raidIdx = ((i - 1) % 30) + 1;
                if (groupIndex < dynamicSpells.Count)
                {
                    spell = dynamicSpells[groupIndex];
                    unit = raidIdx;
                }
            }
            else
            {
                var index = i - dynamicSlots;
                if (specialSpells.TryGetValue(index, out var specialEntry))
                {
                    spell = ResolveSpellName(specialEntry);
                }
                else if (staticSpells.TryGetValue(index, out var staticEntry))
                {
                    spell = ResolveSpellName(staticEntry);
                }

                if (IsWeakSpellName(spell)
                    && existingSpellNames.TryGetValue(i, out var preserved)
                    && !string.IsNullOrWhiteSpace(preserved)
                    && !IsWeakSpellName(preserved))
                {
                    warnings.Add($"{classFile}[{i}]: 保留原技能名「{preserved}」（宏推导为「{spell}」）");
                    spell = preserved;
                }
            }

            root[i.ToString()] = new JsonObject
            {
                ["unit"] = unit,
                ["技能"] = spell,
                ["热键"] = hotkey
            };
        }

        return (root, warnings);
    }

    private static bool IsWeakSpellName(string? spell)
    {
        if (string.IsNullOrWhiteSpace(spell))
        {
            return true;
        }

        return spell.StartsWith("item:", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>同行 `--` 注释优先作为技能名；否则从宏文本推导。</summary>
    private static string ResolveSpellName(MacroEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Comment))
        {
            return entry.Comment.Trim();
        }

        return DeriveSpellName(entry.Body);
    }

    private readonly record struct MacroEntry(string Body, string? Comment);

    internal static string DeriveSpellName(string raw)
    {
        var text = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (text.Length == 0)
        {
            return string.Empty;
        }

        if (StopCastingRegex().IsMatch(text))
        {
            return "停止施法";
        }

        var castSequence = CastSequenceRegex().Match(text);
        if (castSequence.Success)
        {
            var sequenceBody = castSequence.Groups[1].Value.Trim();
            sequenceBody = ResetOptionRegex().Replace(sequenceBody, string.Empty).Trim();
            foreach (var part in sequenceBody.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Equals("x", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return StripConditions(part);
            }
        }

        // cancelaura 后再 /cast：取最后一个 /cast 段；纯物品宏保留首行 item:
        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > 0 && lines[0].StartsWith("item:", StringComparison.OrdinalIgnoreCase))
        {
            return lines[0].Trim();
        }

        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (line.StartsWith("/cast", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("/castsequence", StringComparison.OrdinalIgnoreCase))
            {
                text = line["/cast".Length..].TrimStart();
                break;
            }

            if (i == 0 && !line.StartsWith('/'))
            {
                text = line;
            }
        }

        if (text.StartsWith("/cast", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("/castsequence", StringComparison.OrdinalIgnoreCase))
        {
            text = text["/cast".Length..].TrimStart();
        }

        // 取 ; 分支中第一段（专精/条件分支）
        var firstBranch = text.Split(';', 2, StringSplitOptions.TrimEntries)[0];
        var hasFocus = FocusTargetRegex().IsMatch(firstBranch) || FocusTargetRegex().IsMatch(text);
        var spell = StripConditions(firstBranch);

        if (string.IsNullOrWhiteSpace(spell))
        {
            return string.Empty;
        }

        if (hasFocus && !spell.EndsWith("(焦点)", StringComparison.Ordinal))
        {
            return spell + "(焦点)";
        }

        return spell;
    }

    private static string StripConditions(string text)
    {
        var stripped = ConditionRegex().Replace(text, string.Empty).Trim();
        return stripped;
    }

    private static List<string> ReadArrayStrings(TableValue? table)
    {
        var result = new List<string>();
        if (table is null)
        {
            return result;
        }

        foreach (var item in table.IPairs())
        {
            if (item is StringValue s && !string.IsNullOrWhiteSpace(s.Value))
            {
                result.Add(s.Value.Trim());
            }
            else
            {
                break;
            }
        }

        return result;
    }

    private static Dictionary<int, MacroEntry> ReadSparseStrings(TableValue? table)
    {
        var result = new Dictionary<int, MacroEntry>();
        if (table is null)
        {
            return result;
        }

        foreach (var (key, value) in table.Entries)
        {
            var index = key switch
            {
                long l => (int)l,
                int i => i,
                double d => (int)d,
                NumberValue n => n.AsInt(),
                _ => (int?)null
            };
            if (index is null || value is not StringValue s || string.IsNullOrWhiteSpace(s.Value))
            {
                continue;
            }

            var comment = key is null ? null : table.GetTrailingComment(key);
            result[index.Value] = new MacroEntry(s.Value, comment);
        }

        return result;
    }

    private static Dictionary<int, string> LoadExistingSpellNames(string jsonPath)
    {
        var result = new Dictionary<int, string>();
        if (!File.Exists(jsonPath))
        {
            return result;
        }

        try
        {
            if (JsonNode.Parse(File.ReadAllText(jsonPath)) is not JsonObject root)
            {
                return result;
            }

            foreach (var (key, node) in root)
            {
                if (!int.TryParse(key, out var id) || node is not JsonObject entry)
                {
                    continue;
                }

                var spell = JsonHelpers.GetString(JsonHelpers.Get(entry, "技能"))
                    ?? JsonHelpers.GetString(JsonHelpers.Get(entry, "spell"));
                if (!string.IsNullOrWhiteSpace(spell))
                {
                    result[id] = spell;
                }
            }
        }
        catch
        {
            // 旧 keymap 损坏时忽略，按宏全量重建。
        }

        return result;
    }

    private static string[] BuildMacroKind()
    {
        var list = new string[Modifiers.Length * Keys.Length];
        var i = 0;
        foreach (var modifier in Modifiers)
        {
            foreach (var key in Keys)
            {
                list[i++] = $"{modifier}-{key}";
            }
        }

        return list;
    }

    [GeneratedRegex(@"^\s*/stopcasting\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StopCastingRegex();

    [GeneratedRegex(@"^\s*/castsequence\b\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex CastSequenceRegex();

    [GeneratedRegex(@"\breset\s*=\s*\S+\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ResetOptionRegex();

    [GeneratedRegex(@"\[[^\]]*\]", RegexOptions.CultureInvariant)]
    private static partial Regex ConditionRegex();

    [GeneratedRegex(@"@focus\b|target\s*=\s*focus\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FocusTargetRegex();
}
