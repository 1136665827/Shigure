namespace Shigure;

/// <summary>
/// 轻量 Lua 表字面量解析：足够读取 Fuyutsui ClassBlocks 声明。
/// </summary>
internal static class LuaLiteParser
{
    public abstract class Value;

    public sealed class NilValue : Value
    {
        public static readonly NilValue Instance = new();
        private NilValue() { }
    }

    public sealed class BoolValue(bool value) : Value
    {
        public bool Value { get; } = value;
    }

    public sealed class NumberValue(double value) : Value
    {
        public double Value { get; } = value;
        public int AsInt() => (int)Math.Truncate(Value);
    }

    public sealed class StringValue(string value) : Value
    {
        public string Value { get; } = value;
    }

    public sealed class TableValue : Value
    {
        private readonly Dictionary<object, Value> _map = new();
        private readonly List<(object? Key, Value Value)> _entries = new();

        public IReadOnlyList<(object? Key, Value Value)> Entries => _entries;

        public void Set(object? key, Value value)
        {
            if (key is null)
            {
                _entries.Add((null, value));
                return;
            }

            _map[key] = value;
            _entries.Add((key, value));
        }

        public bool TryGet(object key, out Value value) => _map.TryGetValue(key, out value!);

        public Value? Get(object key) => _map.TryGetValue(key, out var value) ? value : null;

        public IEnumerable<Value> IPairs()
        {
            for (var i = 1; ; i++)
            {
                if (!_map.TryGetValue((long)i, out var value) && !_map.TryGetValue(i, out value))
                {
                    yield break;
                }

                yield return value;
            }
        }

        public string? GetString(object key)
            => Get(key) is StringValue s ? s.Value : null;

        public double? GetNumber(object key)
            => Get(key) is NumberValue n ? n.Value : null;

        public bool? GetBool(object key)
            => Get(key) is BoolValue b ? b.Value : null;

        public TableValue? GetTable(object key)
            => Get(key) as TableValue;
    }

    public static TableValue? ExtractAssignedTable(string source, string assignmentName)
    {
        var index = source.IndexOf(assignmentName, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var eq = source.IndexOf('=', index + assignmentName.Length);
        if (eq < 0)
        {
            return null;
        }

        var cursor = eq + 1;
        while (cursor < source.Length && char.IsWhiteSpace(source[cursor]))
        {
            cursor++;
        }

        if (cursor >= source.Length || source[cursor] != '{')
        {
            return null;
        }

        var parser = new Parser(source, cursor);
        return parser.ParseValue() as TableValue;
    }

    private sealed class Parser
    {
        private readonly string _text;
        private int _pos;

        public Parser(string text, int pos)
        {
            _text = text;
            _pos = pos;
        }

        public Value ParseValue()
        {
            SkipTrivia();
            if (_pos >= _text.Length)
            {
                return NilValue.Instance;
            }

            var ch = _text[_pos];
            if (ch == '{')
            {
                return ParseTable();
            }

            if (ch is '"' or '\'')
            {
                return new StringValue(ParseQuotedString());
            }

            if (ch == '-' || char.IsDigit(ch))
            {
                return ParseNumber();
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                var ident = ParseIdentifier();
                return ident switch
                {
                    "true" => new BoolValue(true),
                    "false" => new BoolValue(false),
                    "nil" => NilValue.Instance,
                    _ => new StringValue(ident)
                };
            }

            throw new InvalidDataException($"无法解析的 Lua 值，位置 {_pos}: '{ch}'");
        }

        private TableValue ParseTable()
        {
            Expect('{');
            var table = new TableValue();
            long nextArrayIndex = 1;

            while (true)
            {
                SkipTrivia();
                if (_pos >= _text.Length)
                {
                    throw new InvalidDataException("Lua 表缺少结束 }");
                }

                if (_text[_pos] == '}')
                {
                    _pos++;
                    break;
                }

                object? key = null;
                Value value;

                if (_text[_pos] == '[')
                {
                    _pos++;
                    SkipTrivia();
                    var keyValue = ParseValue();
                    SkipTrivia();
                    Expect(']');
                    SkipTrivia();
                    Expect('=');
                    key = ToKey(keyValue);
                    value = ParseValue();
                }
                else
                {
                    var start = _pos;
                    var peeked = ParseValue();
                    SkipTrivia();
                    if (_pos < _text.Length && _text[_pos] == '=' && peeked is StringValue name
                        && IsIdentifierAt(start))
                    {
                        _pos++;
                        key = name.Value;
                        value = ParseValue();
                    }
                    else
                    {
                        key = nextArrayIndex++;
                        value = peeked;
                    }
                }

                table.Set(key, value);
                SkipTrivia();
                if (_pos < _text.Length && _text[_pos] == ',')
                {
                    _pos++;
                    continue;
                }

                if (_pos < _text.Length && _text[_pos] == ';')
                {
                    _pos++;
                }
            }

            return table;
        }

        private bool IsIdentifierAt(int start)
        {
            if (start < 0 || start >= _text.Length)
            {
                return false;
            }

            var ch = _text[start];
            return char.IsLetter(ch) || ch == '_';
        }

        private static object ToKey(Value value) => value switch
        {
            NumberValue n => (long)Math.Truncate(n.Value),
            StringValue s => s.Value,
            BoolValue b => b.Value,
            _ => throw new InvalidDataException("不支持的表键类型")
        };

        private string ParseQuotedString()
        {
            var quote = _text[_pos++];
            var chars = new List<char>();
            while (_pos < _text.Length)
            {
                var ch = _text[_pos++];
                if (ch == quote)
                {
                    return new string(chars.ToArray());
                }

                if (ch == '\\' && _pos < _text.Length)
                {
                    var esc = _text[_pos++];
                    chars.Add(esc switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        '\\' => '\\',
                        '\'' => '\'',
                        '"' => '"',
                        _ => esc
                    });
                    continue;
                }

                chars.Add(ch);
            }

            throw new InvalidDataException("字符串未闭合");
        }

        private NumberValue ParseNumber()
        {
            var start = _pos;
            if (_text[_pos] == '-')
            {
                _pos++;
            }

            while (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] is '.' or 'e' or 'E' or '+' or '-'))
            {
                // 避免把 `1,` 后的逗号吃掉；仅允许数字本体字符。
                var c = _text[_pos];
                if (c is '+' or '-')
                {
                    if (_pos == start || (_text[_pos - 1] is not ('e' or 'E')))
                    {
                        break;
                    }
                }

                if (c is '.' or 'e' or 'E' || char.IsDigit(c) || c is '+' or '-')
                {
                    _pos++;
                    continue;
                }

                break;
            }

            var text = _text[start.._pos];
            if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var number))
            {
                throw new InvalidDataException($"无法解析数字: {text}");
            }

            return new NumberValue(number);
        }

        private string ParseIdentifier()
        {
            var start = _pos;
            _pos++;
            while (_pos < _text.Length)
            {
                var ch = _text[_pos];
                if (char.IsLetterOrDigit(ch) || ch == '_')
                {
                    _pos++;
                    continue;
                }

                break;
            }

            return _text[start.._pos];
        }

        private void Expect(char expected)
        {
            SkipTrivia();
            if (_pos >= _text.Length || _text[_pos] != expected)
            {
                throw new InvalidDataException($"期望 '{expected}'，位置 {_pos}");
            }

            _pos++;
        }

        private void SkipTrivia()
        {
            while (_pos < _text.Length)
            {
                if (char.IsWhiteSpace(_text[_pos]))
                {
                    _pos++;
                    continue;
                }

                if (_text[_pos] == '-' && _pos + 1 < _text.Length && _text[_pos + 1] == '-')
                {
                    _pos += 2;
                    if (_pos + 1 < _text.Length && _text[_pos] == '[' && _text[_pos + 1] == '[')
                    {
                        _pos += 2;
                        while (_pos + 1 < _text.Length && !(_text[_pos] == ']' && _text[_pos + 1] == ']'))
                        {
                            _pos++;
                        }

                        if (_pos + 1 < _text.Length)
                        {
                            _pos += 2;
                        }

                        continue;
                    }

                    while (_pos < _text.Length && _text[_pos] is not ('\r' or '\n'))
                    {
                        _pos++;
                    }

                    continue;
                }

                break;
            }
        }
    }
}
