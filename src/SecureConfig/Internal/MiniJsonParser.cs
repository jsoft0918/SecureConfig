using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SecureConfig.Internal
{
    /// <summary>
    /// Minimal dependency-free JSON parser. Objects are returned as List&lt;KeyValuePair&gt; to preserve order,
    /// numbers/true/false as their raw text, and null as null. Arrays are List&lt;object&gt;.
    /// </summary>
    internal sealed class MiniJsonParser
    {
        private readonly string _s;
        private int _i;
        private int _depth;
        private const int MaxDepth = 32;

        public MiniJsonParser(string s)
        {
            _s = s;
            const char Bom = (char)0xFEFF;
            if (_s.Length > 0 && _s[0] == Bom) _i = 1;
        }

        public object ParseDocument()
        {
            SkipWs();
            object v = ParseValue();
            SkipWs();
            if (_i < _s.Length) throw Error("Unexpected content after the end of the JSON.");
            return v;
        }

        private object ParseValue()
        {
            SkipWs();
            if (_i >= _s.Length) throw Error("Unexpected end of JSON; a value was expected.");
            char c = _s[_i];
            switch (c)
            {
                case '{': return ParseObject();
                case '[': return ParseArray();
                case '"': return ParseString();
                case 't': ExpectWord("true"); return "true";
                case 'f': ExpectWord("false"); return "false";
                case 'n': ExpectWord("null"); return null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                    throw Error("Unexpected character '" + c + "'");
            }
        }

        private List<KeyValuePair<string, object>> ParseObject()
        {
            if (++_depth > MaxDepth) throw Error("Nesting is too deep.");
            var list = new List<KeyValuePair<string, object>>();
            _i++; // {
            SkipWs();
            if (Peek() == '}') { _i++; _depth--; return list; }
            while (true)
            {
                SkipWs();
                if (Peek() != '"') throw Error("Expected a property name (\"...\").");
                string key = ParseString();
                SkipWs();
                if (Peek() != ':') throw Error("Expected ':'.");
                _i++;
                object value = ParseValue();
                list.Add(new KeyValuePair<string, object>(key, value));
                SkipWs();
                char c = Peek();
                if (c == ',') { _i++; continue; }
                if (c == '}') { _i++; break; }
                throw Error("Expected ',' or '}'.");
            }
            _depth--;
            return list;
        }

        private List<object> ParseArray()
        {
            if (++_depth > MaxDepth) throw Error("Nesting is too deep.");
            var list = new List<object>();
            _i++; // [
            SkipWs();
            if (Peek() == ']') { _i++; _depth--; return list; }
            while (true)
            {
                list.Add(ParseValue());
                SkipWs();
                char c = Peek();
                if (c == ',') { _i++; continue; }
                if (c == ']') { _i++; break; }
                throw Error("Expected ',' or ']'.");
            }
            _depth--;
            return list;
        }

        private string ParseString()
        {
            _i++; // "
            var sb = new StringBuilder();
            while (true)
            {
                if (_i >= _s.Length) throw Error("Unterminated string.");
                char c = _s[_i++];
                if (c == '"') break;
                if (c < 0x20) throw Error("Control characters (such as line breaks) inside a string must be escaped, e.g. \\n.");
                if (c != '\\') { sb.Append(c); continue; }
                if (_i >= _s.Length) throw Error("Invalid escape sequence");
                char e = _s[_i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (_i + 4 > _s.Length) throw Error("Invalid \\u escape sequence");
                        int code;
                        if (!int.TryParse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            throw Error("Invalid \\u escape sequence");
                        sb.Append((char)code);
                        _i += 4;
                        break;
                    default:
                        throw Error("Invalid escape sequence '\\" + e + "' (write a backslash as \\\\)");
                }
            }
            return sb.ToString();
        }

        private string ParseNumber()
        {
            int start = _i;
            if (Peek() == '-') _i++;
            while (_i < _s.Length && "0123456789+-.eE".IndexOf(_s[_i]) >= 0) _i++;
            string token = _s.Substring(start, _i - start);
            double d;
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw Error("Invalid number '" + token + "'");
            return token;
        }

        private void ExpectWord(string word)
        {
            if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw Error("Unexpected value");
            _i += word.Length;
        }

        private char Peek() { return _i < _s.Length ? _s[_i] : '\0'; }

        private void SkipWs()
        {
            while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\r' || _s[_i] == '\n')) _i++;
        }

        private FormatException Error(string message)
        {
            int line = 1, col = 1;
            for (int k = 0; k < _i && k < _s.Length; k++)
            {
                if (_s[k] == '\n') { line++; col = 1; } else col++;
            }
            return new FormatException("JSON error (line " + line + ", column " + col + "): " + message);
        }
    }
}
