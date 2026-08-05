using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shigure;

public sealed class KeymapService
{
    private readonly string _baseDirectory;
    private readonly ConfigService _config;
    private readonly Dictionary<(int Unit, string Spell), string> _hotkeys = new();
    private int? _currentClassId;
    private int? _currentSpecId;

    public KeymapService(string baseDirectory, ConfigService config)
    {
        _baseDirectory = baseDirectory;
        _config = config;
    }

    public void SelectForClass(int? classId)
    {
        SelectForClass(classId, null);
    }

    public void SelectForClass(int? classId, int? specId)
    {
        if (_currentClassId == classId && _currentSpecId == specId && _hotkeys.Count > 0)
        {
            return;
        }

        _currentClassId = classId;
        _currentSpecId = specId;
        _hotkeys.Clear();

        var path = KeymapCatalog.ResolveKeymapFilePath(_baseDirectory, _config.GetKeymapName(classId));
        if (!File.Exists(path))
        {
            return;
        }

        var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) as JsonObject;

        if (root is null)
        {
            return;
        }

        var entries = root;
        if (specId is { } id
            && JsonHelpers.Get(root, "专精") is JsonObject specRoot
            && JsonHelpers.Get(specRoot, id.ToString()) is JsonObject specEntries)
        {
            entries = specEntries;
        }

        foreach (var (_, node) in entries)
        {
            if (node is not JsonObject entry)
            {
                continue;
            }

            var unit = JsonHelpers.GetInt(JsonHelpers.Get(entry, "unit")) ?? 0;
            var spell = JsonHelpers.GetString(JsonHelpers.Get(entry, "spell"))
                ?? JsonHelpers.GetString(JsonHelpers.Get(entry, "技能"));
            var hotkey = JsonHelpers.GetString(JsonHelpers.Get(entry, "hotkey"))
                ?? JsonHelpers.GetString(JsonHelpers.Get(entry, "热键"));

            if (!string.IsNullOrWhiteSpace(spell) && !string.IsNullOrWhiteSpace(hotkey))
            {
                _hotkeys[(unit, spell)] = hotkey;
            }
        }
    }

    public string? GetHotkey(int? unit, string spell)
    {
        var normalizedUnit = unit.GetValueOrDefault();
        return _hotkeys.TryGetValue((normalizedUnit, spell), out var hotkey) ? hotkey : null;
    }

    public IReadOnlyDictionary<int, string> GetCurrentFailedSpells()
    {
        return _config.GetFailedSpells(_currentClassId);
    }

    public IReadOnlyDictionary<int, string> GetCurrentOneKeySpells()
    {
        return _config.GetOneKeySpells(_currentClassId);
    }
}
