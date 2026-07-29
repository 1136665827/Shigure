namespace Shigure;

internal static class ModuleDisplay
{
    public static string FormatListItem(ModuleDefinition module)
    {
        return module.Name;
    }

    public static string FormatMatch(ModuleMatch match)
    {
        return $"{FormatMatchValue(match.ClassId)}/{FormatMatchValue(match.SpecId)}/{FormatPartyTypeValue(match.PartyType)}/{FormatMatchValue(match.HeroTalent)}";
    }

    public static string FormatState(RenderSnapshot snapshot)
    {
        var classText = snapshot.ClassName is null
            ? FormatMatchValue(snapshot.ClassId)
            : $"{snapshot.ClassName} ({snapshot.ClassId})";
        var specText = snapshot.SpecName is null
            ? FormatMatchValue(snapshot.SpecId)
            : $"{snapshot.SpecName} ({snapshot.SpecId})";
        var partyType = snapshot.State?.GetInt("队伍类型");
        var heroTalent = snapshot.State?.GetInt("英雄天赋");
        return $"{classText} / {specText} / {FormatPartyTypeValue(partyType?.ToString())} / {FormatMatchValue(heroTalent)}";
    }

    private static string FormatMatchValue(int? value)
    {
        return value?.ToString() ?? "*";
    }

    private static string FormatPartyTypeValue(string? value)
    {
        return ModuleMatch.NormalizePartyTypeValue(value) switch
        {
            null => "*",
            "0" => "单人",
            "1-40" => "团队",
            "46" => "队伍",
            var other => other
        };
    }
}
