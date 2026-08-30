using System.Globalization;

namespace Shigure;

internal static class SpellFieldKey
{
    public const string AuraValue = "value";
    public const string AuraApplications = "apps";
    public const string SpellCooldown = "cooldown";
    public const string SpellChargeCooldown = "chargeCooldown";
    public const string SpellCount = "count";

    public static string Aura(string scope, long spellId, string metric = AuraValue)
        => $"auras.{scope}.{spellId.ToString(CultureInfo.InvariantCulture)}.{metric}";

    public static string AuraMember(long spellId, string metric = AuraValue)
        => $"auras.{spellId.ToString(CultureInfo.InvariantCulture)}.{metric}";

    public static string Spell(long spellId, string metric = SpellCooldown)
        => $"spells.{spellId.ToString(CultureInfo.InvariantCulture)}.{metric}";

    public static string StripRoot(string value)
    {
        var key = value.Trim();
        return key.StartsWith("state.", StringComparison.OrdinalIgnoreCase)
            ? key["state.".Length..]
            : key;
    }

    public static bool TryParseSpell(string? value, out long spellId, out string metric)
    {
        var key = StripRoot(value ?? string.Empty);
        var parts = key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 3
            && string.Equals(parts[0], "spells", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out spellId)
            && spellId > 0)
        {
            metric = parts[2];
            return true;
        }

        spellId = 0;
        metric = string.Empty;
        return false;
    }

    public static bool TryParseAura(string? value, out string scope, out long spellId, out string metric)
    {
        var key = StripRoot(value ?? string.Empty);
        var parts = key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 4
            && string.Equals(parts[0], "auras", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(parts[^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out spellId)
            && spellId > 0)
        {
            scope = string.Join('.', parts[1..^2]);
            metric = parts[^1];
            return true;
        }

        scope = string.Empty;
        spellId = 0;
        metric = string.Empty;
        return false;
    }

    public static bool TryParseAuraMember(string? value, out long spellId, out string metric)
    {
        var key = StripRoot(value ?? string.Empty);
        var auraIndex = key.IndexOf("auras.", StringComparison.OrdinalIgnoreCase);
        if (auraIndex > 0)
        {
            key = key[auraIndex..];
        }

        var parts = key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 3
            && string.Equals(parts[0], "auras", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out spellId)
            && spellId > 0)
        {
            metric = parts[2];
            return true;
        }

        spellId = 0;
        metric = string.Empty;
        return false;
    }

    public static long? CanonicalAuraId(long? spellId, IEnumerable<long>? spellIds)
    {
        if (spellId is > 0)
        {
            return spellId;
        }

        var ids = (spellIds ?? []).Where(id => id > 0).Distinct().OrderBy(id => id).ToArray();
        return ids.Length == 0 ? null : ids[0];
    }
}
