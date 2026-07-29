using System.Globalization;

namespace Shigure;

/// <summary>
/// keymap 中团队槽位 1-30 之外的保留单位，以及模块编辑器使用的中文显示名称。
/// </summary>
internal static class ReservedUnit
{
    public const int None = 0;
    public const int Player = 31;
    public const int Target = 32;
    public const int Focus = 33;
    public const int Cursor = 34;
    public const int Mouseover = 35;

    public static string ToDisplayText(int unit)
    {
        return unit switch
        {
            None => "无目标",
            Player => "玩家",
            Target => "目标",
            Focus => "焦点",
            Cursor => "地面",
            Mouseover => "鼠标",
            _ => unit.ToString(CultureInfo.InvariantCulture)
        };
    }

    public static int? ParseDisplayText(string? text)
    {
        var value = text?.Trim() ?? string.Empty;
        return value switch
        {
            "无目标" => None,
            "玩家" => Player,
            "目标" => Target,
            "焦点" => Focus,
            "地面" => Cursor,
            "鼠标" => Mouseover,
            _ => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unit)
                ? unit
                : null
        };
    }
}
