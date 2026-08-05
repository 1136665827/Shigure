namespace Shigure;

public interface IKeymapResolver
{
    void SelectForClass(int? classId, int? specId);

    string? GetHotkey(int? unit, string spell);

    IReadOnlyDictionary<int, string> GetCurrentFailedSpells();

    IReadOnlyDictionary<int, string> GetCurrentOneKeySpells();
}
