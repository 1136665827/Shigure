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
        var staticSpells = ReadArrayEntries(classTable.GetTable("staticSpells"));
        var specialSpells = ReadArrayEntries(classTable.GetTable("specialSpells"));
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
                if (groupIndex < dynamicSpells.Count
                    && !string.IsNullOrWhiteSpace(dynamicSpells[groupIndex]))
                {
                    spell = dynamicSpells[groupIndex];
                    unit = raidIdx;
                }
            }
            else
            {
                var relativeIndex = i - dynamicSlots - 1;
                MacroEntry? entry = null;
                var isStaticEntry = false;
                if (relativeIndex < staticSpells.Count)
                {
                    entry = staticSpells[relativeIndex];
                    isStaticEntry = true;
                }
                else
                {
                    var specialIndex = relativeIndex - staticSpells.Count;
                    if (specialIndex < specialSpells.Count)
                    {
                        entry = specialSpells[specialIndex];
                    }
                }

                if (entry is { Body.Length: > 0 } macroEntry)
                {
                    if (isStaticEntry)
                    {
                        var parsed = ParseStaticMacro(macroEntry.Body, macroEntry.Comment);
                        unit = parsed.Unit;
                        spell = parsed.Spell;
                    }
                    else
                    {
                        var parsed = ParseSpecialMacro(macroEntry.Body, macroEntry.Comment);
                        unit = parsed.Unit;
                        spell = parsed.Spell;
                    }
                }

                if (entry is { Body.Length: > 0 }
                    && IsWeakSpellName(spell)
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

    internal readonly record struct ParsedMacro(int Unit, string Spell);

    /// <summary>
    /// 解析静态宏供 keymap 与宏列表共用。玩家、当前目标、焦点、地面、鼠标指向分别使用保留单位 31-35。
    /// </summary>
    internal static ParsedMacro ParseStaticMacro(string raw, string? comment = null)
    {
        var target = StaticTargetRegex().Match(raw);
        var unit = target.Success
            ? ResolveUnitName(target.Groups["unit"].Value)
            : ReservedUnit.None;

        return new ParsedMacro(unit, ResolveSpellName(new MacroEntry(raw, comment)));
    }

    /// <summary>
    /// 解析特殊宏：方括号中的首个单位作为 unit，技能沿用宏技能推导（castsequence 只取逗号前首项）。
    /// </summary>
    internal static ParsedMacro ParseSpecialMacro(string raw, string? comment = null)
    {
        // 标准 WoW 条件允许单位出现在其它条件之后，例如 [known:123,@cursor]。
        var target = StaticTargetRegex().Match(raw);
        var unit = target.Success
            ? ResolveUnitName(target.Groups["unit"].Value)
            : ReservedUnit.None;

        // 特殊宏也接受 [player]/[玩家]/[31] 这种“方括号内直接写单位”的简写。
        if (unit == ReservedUnit.None && SpecialUnitRegex().Match(raw) is { Success: true } specialUnit)
        {
            unit = ResolveUnitName(specialUnit.Groups["unit"].Value);
        }

        var spell = ResolveSpellName(new MacroEntry(raw, comment));
        spell = spell.Split(',', 2, StringSplitOptions.TrimEntries)[0];
        return new ParsedMacro(unit, spell);
    }

    private static int ResolveUnitName(string raw)
    {
        return raw.Trim().TrimStart('@').ToLowerInvariant() switch
        {
            "player" or "玩家" or "31" => ReservedUnit.Player,
            "target" or "目标" or "32" => ReservedUnit.Target,
            "focus" or "焦点" or "33" => ReservedUnit.Focus,
            "cursor" or "地面" or "34" => ReservedUnit.Cursor,
            "mouseover" or "鼠标" or "35" => ReservedUnit.Mouseover,
            _ => ReservedUnit.None
        };
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
        var spell = StripConditions(firstBranch);

        if (string.IsNullOrWhiteSpace(spell))
        {
            return string.Empty;
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
            if (item is StringValue s)
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

    private static List<MacroEntry> ReadArrayEntries(TableValue? table)
    {
        var result = new List<MacroEntry>();
        if (table is null)
        {
            return result;
        }

        var index = 1;
        foreach (var value in table.IPairs())
        {
            if (value is not StringValue s)
            {
                break;
            }

            result.Add(new MacroEntry(s.Value, table.GetTrailingComment((long)index)));
            index++;
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

    [GeneratedRegex(@"\[[^\]]*@(?<unit>cursor|target|focus|player|mouseover)\b[^\]]*\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StaticTargetRegex();

    [GeneratedRegex(@"\[\s*@?(?<unit>player|target|focus|cursor|mouseover|玩家|目标|焦点|地面|鼠标|无目标|0|31|32|33|34|35)\s*(?:,|\])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpecialUnitRegex();
}
