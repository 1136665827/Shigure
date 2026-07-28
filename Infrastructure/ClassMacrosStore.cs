using System.Text;
using static Shigure.LuaLiteParser;

namespace Shigure;

/// <summary>
/// 读写 Fuyutsui core/classmacros.lua 中的 ClassMacros，
/// 保存时只替换 ClassMacros 表字面量，保留文件其余内容。
/// </summary>
internal static class ClassMacrosStore
{
    public const string AssignmentName = "Fuyutsui.ClassMacros";

    private static readonly string[] ClassFileOrder =
    [
        "WARRIOR", "PALADIN", "HUNTER", "ROGUE", "PRIEST", "DEATHKNIGHT",
        "SHAMAN", "MAGE", "WARLOCK", "MONK", "DRUID", "DEMONHUNTER", "EVOKER"
    ];

    public sealed class MacrosDocument
    {
        public string FilePath { get; set; } = string.Empty;
        public string SourceText { get; set; } = string.Empty;
        public int TableStart { get; set; }
        public int TableEndExclusive { get; set; }
        public Dictionary<string, ClassMacros> Classes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> ClassOrder { get; set; } = new();
    }

    public sealed class ClassMacros
    {
        public List<string> DynamicSpells { get; } = new();
        public List<ArrayEntry> StaticSpells { get; } = new();
        public List<ArrayEntry> SpecialSpells { get; } = new();
    }

    public sealed class ArrayEntry
    {
        public string Text { get; set; } = string.Empty;
        public string? Comment { get; set; }
    }

    public static string ToClassFileKey(int classId)
        => ClassNames.GetConfigFileName(classId).ToUpperInvariant();

    public static MacrosDocument Load(string filePath)
    {
        var source = File.ReadAllText(filePath, Encoding.UTF8);
        if (!TryExtractAssignedTable(source, AssignmentName, out var table, out var start, out var end))
        {
            throw new InvalidDataException($"{Path.GetFileName(filePath)} 中未找到 {AssignmentName}");
        }

        var doc = new MacrosDocument
        {
            FilePath = filePath,
            SourceText = source,
            TableStart = start,
            TableEndExclusive = end
        };

        foreach (var (key, value) in table.Entries)
        {
            if (key is not string classFile || value is not TableValue classTable)
            {
                continue;
            }

            var macros = ParseClass(classTable);
            doc.Classes[classFile] = macros;
            doc.ClassOrder.Add(classFile);
        }

        return doc;
    }

    public static void Save(MacrosDocument document)
    {
        var serialized = SerializeClassMacros(document);
        var updated = document.SourceText[..document.TableStart]
            + serialized
            + document.SourceText[document.TableEndExclusive..];
        File.WriteAllText(document.FilePath, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (!TryExtractAssignedTable(updated, AssignmentName, out _, out var start, out var end))
        {
            throw new InvalidOperationException("保存后无法重新定位 ClassMacros 表。");
        }

        document.SourceText = updated;
        document.TableStart = start;
        document.TableEndExclusive = end;
    }

    private static ClassMacros ParseClass(TableValue classTable)
    {
        var macros = new ClassMacros();
        if (classTable.GetTable("dynamicSpells") is { } dynamic)
        {
            foreach (var item in dynamic.IPairs())
            {
                if (item is StringValue s)
                {
                    macros.DynamicSpells.Add(s.Value);
                }
            }
        }

        ReadArray(classTable.GetTable("staticSpells"), macros.StaticSpells);
        ReadArray(classTable.GetTable("specialSpells"), macros.SpecialSpells);
        return macros;
    }

    private static void ReadArray(TableValue? table, List<ArrayEntry> target)
    {
        if (table is null)
        {
            return;
        }

        var index = 1;
        foreach (var value in table.IPairs())
        {
            if (value is not StringValue text)
            {
                break;
            }

            target.Add(new ArrayEntry
            {
                Text = text.Value,
                Comment = table.GetTrailingComment((long)index)
            });
            index++;
        }
    }

    public static string SerializeClassMacros(MacrosDocument document)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");

        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var classFile in EnumerateClassOrder(document))
        {
            if (!document.Classes.TryGetValue(classFile, out var macros))
            {
                continue;
            }

            written.Add(classFile);
            WriteClass(sb, classFile, macros);
        }

        foreach (var (classFile, macros) in document.Classes)
        {
            if (written.Contains(classFile))
            {
                continue;
            }

            WriteClass(sb, classFile, macros);
        }

        sb.Append('}');
        return sb.ToString();
    }

    private static IEnumerable<string> EnumerateClassOrder(MacrosDocument document)
    {
        if (document.ClassOrder.Count > 0)
        {
            foreach (var key in document.ClassOrder)
            {
                yield return key;
            }

            yield break;
        }

        foreach (var key in ClassFileOrder)
        {
            yield return key;
        }
    }

    private static void WriteClass(StringBuilder sb, string classFile, ClassMacros macros)
    {
        sb.Append("    ").Append(classFile).AppendLine(" = {");

        // dynamicSpells
        if (macros.DynamicSpells.Count == 0)
        {
            sb.AppendLine("        dynamicSpells = {},");
        }
        else
        {
            sb.Append("        dynamicSpells = { ");
            sb.Append(string.Join(", ", macros.DynamicSpells.Select(s => $"\"{Escape(s)}\"")));
            sb.AppendLine(" },");
        }

        // staticSpells
        WriteArrayTable(sb, "staticSpells", macros.StaticSpells);

        // specialSpells
        WriteArrayTable(sb, "specialSpells", macros.SpecialSpells);

        sb.AppendLine("    },");
        sb.AppendLine();
    }

    private static void WriteArrayTable(StringBuilder sb, string name, List<ArrayEntry> entries)
    {
        if (entries.Count == 0)
        {
            sb.Append("        ").Append(name).AppendLine(" = {},");
            return;
        }

        sb.Append("        ").Append(name).AppendLine(" = {");
        foreach (var entry in entries)
        {
            sb.Append("            \"").Append(Escape(entry.Text)).Append('"');
            if (!string.IsNullOrWhiteSpace(entry.Comment))
            {
                sb.Append(", -- ").Append(entry.Comment.Trim());
            }
            else
            {
                sb.Append(',');
            }

            sb.AppendLine();
        }

        sb.AppendLine("        },");
    }

    private static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }

        return sb.ToString();
    }
}
