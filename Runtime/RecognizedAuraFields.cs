namespace Shigure;

public enum RecognizedAuraMetric
{
    Stacks,
    Time
}

public static class RecognizedAuraFields
{
    public const string StateKey = "recognizedAuras";
    public const string Prefix = "ra.";
    public const string LongPrefix = "recognizedAuras.";
    public const string LegacyPrefix = "recognizedAura.";
    private const string StacksPrefix = "层数.";
    private const string TimePrefix = "时间.";
    private const string EnglishStacksPrefix = "stacks.";
    private const string EnglishStackPrefix = "stack.";
    private const string EnglishTimePrefix = "time.";
    private const string EnglishRemainingPrefix = "remaining.";
    private const string TimeLookupPrefix = "$time:";

    public static bool TryGetName(string fieldName, out string name)
    {
        return TryParse(fieldName, out name, out _);
    }

    public static bool TryParse(string fieldName, out string name, out RecognizedAuraMetric metric)
    {
        var key = fieldName.Trim();
        foreach (var prefix in new[] { Prefix, LongPrefix, LegacyPrefix })
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                key = key[prefix.Length..].Trim();
                metric = RecognizedAuraMetric.Stacks;
                if (TryStripMetricPrefix(key, StacksPrefix, RecognizedAuraMetric.Stacks, out name, out metric)
                    || TryStripMetricPrefix(key, TimePrefix, RecognizedAuraMetric.Time, out name, out metric)
                    || TryStripMetricPrefix(key, EnglishStacksPrefix, RecognizedAuraMetric.Stacks, out name, out metric)
                    || TryStripMetricPrefix(key, EnglishStackPrefix, RecognizedAuraMetric.Stacks, out name, out metric)
                    || TryStripMetricPrefix(key, EnglishTimePrefix, RecognizedAuraMetric.Time, out name, out metric)
                    || TryStripMetricPrefix(key, EnglishRemainingPrefix, RecognizedAuraMetric.Time, out name, out metric))
                {
                    return name.Length > 0;
                }

                name = key;
                return name.Length > 0;
            }
        }

        name = string.Empty;
        metric = RecognizedAuraMetric.Stacks;
        return false;
    }

    public static bool TryGetLookupKey(string fieldName, out string lookupKey)
    {
        if (!TryParse(fieldName, out var name, out var metric))
        {
            lookupKey = string.Empty;
            return false;
        }

        lookupKey = ToLookupKey(name, metric);
        return true;
    }

    public static bool TryDescribeLookupKey(string lookupKey, out string name, out RecognizedAuraMetric metric)
    {
        var key = lookupKey.Trim();
        if (key.StartsWith(TimeLookupPrefix, StringComparison.Ordinal))
        {
            name = key[TimeLookupPrefix.Length..];
            metric = RecognizedAuraMetric.Time;
            return name.Length > 0;
        }

        name = key;
        metric = RecognizedAuraMetric.Stacks;
        return name.Length > 0;
    }

    public static bool IsBarePrefix(string text)
    {
        var trimmed = text.Trim();
        return string.Equals(trimmed, Prefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, LongPrefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, LegacyPrefix, StringComparison.OrdinalIgnoreCase)
            || IsBareMetricPrefix(trimmed, Prefix)
            || IsBareMetricPrefix(trimmed, LongPrefix)
            || IsBareMetricPrefix(trimmed, LegacyPrefix);
    }

    public static string ToFieldName(string name)
    {
        var trimmed = name.Trim();
        return trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith(LongPrefix, StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase)
            ? Prefix + (TryGetName(trimmed, out var auraName) ? auraName : string.Empty)
            : Prefix + trimmed;
    }

    public static string ToFieldName(string name, RecognizedAuraMetric metric)
    {
        var trimmed = name.Trim();
        var auraName = TryGetName(trimmed, out var parsedName)
            ? parsedName
            : trimmed;
        if (auraName.Length == 0)
        {
            return Prefix;
        }

        return metric == RecognizedAuraMetric.Time
            ? Prefix + TimePrefix + auraName
            : Prefix + StacksPrefix + auraName;
    }

    public static Dictionary<string, object?> BuildValueMap(IEnumerable<RecognizedAuraInfo> auras)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var aura in auras)
        {
            var name = aura.Name.Trim();
            if (name.Length == 0 || name == "-")
            {
                continue;
            }

            AddMax(values, ToLookupKey(name, RecognizedAuraMetric.Stacks), Math.Max(1, aura.Value));
            AddMax(values, ToLookupKey(name, RecognizedAuraMetric.Time), Math.Max(0, aura.Time));
        }

        return values;
    }

    private static bool TryStripMetricPrefix(
        string key,
        string prefix,
        RecognizedAuraMetric parsedMetric,
        out string name,
        out RecognizedAuraMetric metric)
    {
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            name = string.Empty;
            metric = RecognizedAuraMetric.Stacks;
            return false;
        }

        name = key[prefix.Length..].Trim();
        metric = parsedMetric;
        return true;
    }

    private static bool IsBareMetricPrefix(string text, string basePrefix)
    {
        if (!text.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var key = text[basePrefix.Length..].Trim();
        return string.Equals(key, "层数", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, StacksPrefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "时间", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, TimePrefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "stacks", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, EnglishStacksPrefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "stack", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, EnglishStackPrefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "time", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, EnglishTimePrefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "remaining", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, EnglishRemainingPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string ToLookupKey(string name, RecognizedAuraMetric metric)
        => metric == RecognizedAuraMetric.Time ? TimeLookupPrefix + name : name;

    private static void AddMax(Dictionary<string, object?> values, string key, int value)
    {
        if (values.TryGetValue(key, out var existing) && existing is int current)
        {
            values[key] = Math.Max(current, value);
        }
        else
        {
            values[key] = value;
        }
    }
}
