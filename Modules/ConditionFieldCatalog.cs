using System.Text.Json.Nodes;

namespace Shigure;

public enum ConditionFieldType
{
    Int,
    Bool,
    String
}

public enum ConditionFieldCategory
{
    State,
    Shigure,
    Aura,
    Spell,
    DynamicUnit,
    DynamicValue
}

public static class ShigureConditionFields
{
    // 仅供条件编辑器承载规则级配置，不会写入条件表达式参与状态求值。
    public const string Delay = "$shigure.delay";
    public const string LogicDelay = "$shigure.logicDelay";
}

public sealed record ConditionField(
    string Name,
    string DisplayName,
    ConditionFieldType Type,
    ConditionFieldCategory Category = ConditionFieldCategory.State)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// 从 config 目录构建可在条件编辑器中选择的字段目录。
/// 字段按模块当前选中的职业/专精过滤；group 队伍字段暂不收录。
/// </summary>
public sealed class ConditionFieldCatalog
{
    private static readonly HashSet<string> RemovedCastFields = new(StringComparer.Ordinal)
    {
        "施法",
        "目标施法",
        "焦点施法",
        "首领1施法",
        "首领2施法",
        "首领3施法",
        "首领4施法",
        "首领5施法"
    };

    private readonly ConfigService? _config;

    private ConditionFieldCatalog(ConfigService? config)
    {
        _config = config;
    }

    public static ConditionFieldCatalog Load(string baseDirectory)
    {
        try
        {
            return new ConditionFieldCatalog(ConfigService.LoadFromBaseDirectory(baseDirectory));
        }
        catch
        {
            // config 缺失或损坏时返回空目录，编辑器降级为手动输入。
            return new ConditionFieldCatalog(null);
        }
    }

    /// <summary>
    /// 返回指定职业/专精下可用的条件字段。classId/specId 为空时只返回公共 state 字段。
    /// </summary>
    public IReadOnlyList<ConditionField> GetFields(int? classId, int? specId)
    {
        var fields = new List<ConditionField>();
        if (_config is null)
        {
            return fields;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stateConfig = _config.BuildStateConfig(classId, specId);

        foreach (var (key, node) in stateConfig)
        {
            if (key is "group" or "spells" or "auras"
                || key == "锚点"
                || RemovedCastFields.Contains(key))
            {
                continue;
            }

            if (node is JsonObject field && field.ContainsKey("step"))
            {
                AddField(fields, seen, key, key, ReadType(field), ConditionFieldCategory.State);
            }
        }

        if (JsonHelpers.Get(stateConfig, "auras") is JsonObject auras)
        {
            foreach (var (auraName, node) in auras)
            {
                if (node is JsonObject field && field.ContainsKey("step"))
                {
                    AddField(fields, seen, $"auras.{auraName}", $"光环: {auraName}", ReadType(field), ConditionFieldCategory.Aura);
                }
            }
        }

        if (JsonHelpers.Get(stateConfig, "spells") is JsonObject spells)
        {
            foreach (var (spellName, node) in spells)
            {
                if (node is JsonObject field && field.ContainsKey("step"))
                {
                    AddField(fields, seen, $"spells.{spellName}", $"技能: {spellName}", ReadType(field), ConditionFieldCategory.Spell);
                }
            }
        }

        // 原始“插入法术”按 config 中的 int 状态保留；转换为技能名的特殊字段单独置底。
        AddField(
            fields,
            seen,
            ModuleSpecialActions.FailedSpell,
            ModuleSpecialActions.FailedSpell,
            ConditionFieldType.String,
            ConditionFieldCategory.State);

        return fields;
    }

    /// <summary>
    /// 返回指定职业/专精下 group 队伍成员的字段(生命值/职责/驱散 + 该专精光环字段), 带类型。
    /// 供动态单位编辑器选择光环、以及条件编辑器构造 单位.字段 选项使用。
    /// </summary>
    public IReadOnlyList<ConditionField> GetGroupFields(int? classId, int? specId)
    {
        var fields = new List<ConditionField>();
        if (_config is null)
        {
            return fields;
        }

        var stateConfig = _config.BuildStateConfig(classId, specId);
        if (JsonHelpers.Get(stateConfig, "group") is not JsonObject group)
        {
            return fields;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, node) in group)
        {
            if (key is "start" or "num")
            {
                continue;
            }

            if (node is JsonObject field && field.ContainsKey("step") && seen.Add(key))
            {
                fields.Add(new ConditionField(key, key, ReadType(field)));
            }
        }

        // 治疗吸收由网格扫描注入，不在 config 的 group 字段里声明。
        if (seen.Add("治疗吸收"))
        {
            fields.Add(new ConditionField("治疗吸收", "治疗吸收", ConditionFieldType.Int));
        }

        return fields;
    }

    private static void AddField(
        List<ConditionField> fields,
        HashSet<string> seen,
        string name,
        string displayName,
        ConditionFieldType type,
        ConditionFieldCategory category = ConditionFieldCategory.State)
    {
        if (seen.Add(name))
        {
            fields.Add(new ConditionField(name, displayName, type, category));
        }
    }

    private static ConditionFieldType ReadType(JsonObject field)
    {
        return JsonHelpers.GetString(JsonHelpers.Get(field, "type")) switch
        {
            "bool" => ConditionFieldType.Bool,
            "string" => ConditionFieldType.String,
            _ => ConditionFieldType.Int
        };
    }
}
