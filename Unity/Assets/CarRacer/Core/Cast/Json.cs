using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CarRacer.Core.Cast
{
    /// <summary>
    /// Just enough JSON for the Cast protocol's messages (design doc §2.4): objects become
    /// <see cref="Dictionary{TKey, TValue}"/> of string to object, arrays <see cref="List{T}"/> of
    /// object, numbers <see cref="double"/>, and true, false and null themselves. Unity has no JSON
    /// reader for arbitrary shapes, so the game carries its own.
    /// </summary>
    public static class Json
    {
        /// <summary>Reads <paramref name="text"/>; throws <see cref="FormatException"/> if it isn't JSON.</summary>
        public static object Parse(string text)
        {
            int i = 0;
            object value = ReadValue(text, ref i, 0);
            SkipSpace(text, ref i);
            if (i != text.Length)
            {
                throw new FormatException("Unexpected text after the JSON value at " + i);
            }

            return value;
        }

        /// <summary>Reads <paramref name="text"/> as a JSON object, or returns null if it isn't one.</summary>
        public static Dictionary<string, object> TryParseObject(string text)
        {
            try
            {
                return Parse(text) as Dictionary<string, object>;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        /// <summary>Writes dictionaries, lists, strings, numbers, booleans and null as compact JSON.</summary>
        public static string Write(object value)
        {
            var text = new StringBuilder();
            WriteValue(text, value);
            return text.ToString();
        }

        /// <summary>The string at <paramref name="key"/>, or null.</summary>
        public static string String(Dictionary<string, object> obj, string key) =>
            obj != null && obj.TryGetValue(key, out var v) ? v as string : null;

        /// <summary>The object at <paramref name="key"/>, or null.</summary>
        public static Dictionary<string, object> Object(Dictionary<string, object> obj, string key) =>
            obj != null && obj.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;

        /// <summary>The array at <paramref name="key"/>, or an empty one.</summary>
        public static List<object> Array(Dictionary<string, object> obj, string key) =>
            obj != null && obj.TryGetValue(key, out var v) && v is List<object> list ? list : new List<object>();

        public static bool Bool(Dictionary<string, object> obj, string key) =>
            obj != null && obj.TryGetValue(key, out var v) && v is bool b && b;

        private const int MaxDepth = 32;

        private static object ReadValue(string s, ref int i, int depth)
        {
            if (depth > MaxDepth)
            {
                throw new FormatException("JSON nested too deeply");
            }

            SkipSpace(s, ref i);
            if (i >= s.Length)
            {
                throw new FormatException("JSON ended early");
            }

            char c = s[i];
            switch (c)
            {
                case '{':
                    return ReadObject(s, ref i, depth);
                case '[':
                    return ReadArray(s, ref i, depth);
                case '"':
                    return ReadString(s, ref i);
                case 't':
                    Expect(s, ref i, "true");
                    return true;
                case 'f':
                    Expect(s, ref i, "false");
                    return false;
                case 'n':
                    Expect(s, ref i, "null");
                    return null;
                default:
                    return ReadNumber(s, ref i);
            }
        }

        private static Dictionary<string, object> ReadObject(string s, ref int i, int depth)
        {
            var obj = new Dictionary<string, object>();
            i++;
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == '}')
            {
                i++;
                return obj;
            }

            while (true)
            {
                SkipSpace(s, ref i);
                if (i >= s.Length || s[i] != '"')
                {
                    throw new FormatException("Expected a key at " + i);
                }

                string key = ReadString(s, ref i);
                SkipSpace(s, ref i);
                if (i >= s.Length || s[i] != ':')
                {
                    throw new FormatException("Expected ':' at " + i);
                }

                i++;
                obj[key] = ReadValue(s, ref i, depth + 1);
                SkipSpace(s, ref i);
                if (i < s.Length && s[i] == ',')
                {
                    i++;
                    continue;
                }

                if (i < s.Length && s[i] == '}')
                {
                    i++;
                    return obj;
                }

                throw new FormatException("Expected ',' or '}' at " + i);
            }
        }

        private static List<object> ReadArray(string s, ref int i, int depth)
        {
            var list = new List<object>();
            i++;
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == ']')
            {
                i++;
                return list;
            }

            while (true)
            {
                list.Add(ReadValue(s, ref i, depth + 1));
                SkipSpace(s, ref i);
                if (i < s.Length && s[i] == ',')
                {
                    i++;
                    continue;
                }

                if (i < s.Length && s[i] == ']')
                {
                    i++;
                    return list;
                }

                throw new FormatException("Expected ',' or ']' at " + i);
            }
        }

        private static string ReadString(string s, ref int i)
        {
            var text = new StringBuilder();
            i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"')
                {
                    return text.ToString();
                }

                if (c != '\\')
                {
                    text.Append(c);
                    continue;
                }

                if (i >= s.Length)
                {
                    break;
                }

                char e = s[i++];
                switch (e)
                {
                    case 'n':
                        text.Append('\n');
                        break;
                    case 't':
                        text.Append('\t');
                        break;
                    case 'r':
                        text.Append('\r');
                        break;
                    case 'b':
                        text.Append('\b');
                        break;
                    case 'f':
                        text.Append('\f');
                        break;
                    case 'u':
                        if (i + 4 > s.Length || !ushort.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort code))
                        {
                            throw new FormatException("Bad \\u escape at " + i);
                        }

                        text.Append((char)code);
                        i += 4;
                        break;
                    default:
                        text.Append(e);
                        break;
                }
            }

            throw new FormatException("Unterminated string");
        }

        private static double ReadNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0)
            {
                i++;
            }

            if (i == start || !double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                throw new FormatException("Bad value at " + start);
            }

            return value;
        }

        private static void Expect(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
            {
                throw new FormatException("Expected " + word + " at " + i);
            }

            i += word.Length;
        }

        private static void SkipSpace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
            {
                i++;
            }
        }

        private static void WriteValue(StringBuilder text, object value)
        {
            switch (value)
            {
                case null:
                    text.Append("null");
                    break;
                case string str:
                    WriteString(text, str);
                    break;
                case bool b:
                    text.Append(b ? "true" : "false");
                    break;
                case IDictionary<string, object> obj:
                    text.Append('{');
                    bool first = true;
                    foreach (var pair in obj)
                    {
                        if (!first)
                        {
                            text.Append(',');
                        }

                        first = false;
                        WriteString(text, pair.Key);
                        text.Append(':');
                        WriteValue(text, pair.Value);
                    }

                    text.Append('}');
                    break;
                case System.Collections.IEnumerable list:
                    text.Append('[');
                    bool firstItem = true;
                    foreach (var item in list)
                    {
                        if (!firstItem)
                        {
                            text.Append(',');
                        }

                        firstItem = false;
                        WriteValue(text, item);
                    }

                    text.Append(']');
                    break;
                case IFormattable number:
                    text.Append(number.ToString(null, CultureInfo.InvariantCulture));
                    break;
                default:
                    WriteString(text, value.ToString());
                    break;
            }
        }

        private static void WriteString(StringBuilder text, string value)
        {
            text.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"':
                        text.Append("\\\"");
                        break;
                    case '\\':
                        text.Append("\\\\");
                        break;
                    case '\n':
                        text.Append("\\n");
                        break;
                    case '\r':
                        text.Append("\\r");
                        break;
                    case '\t':
                        text.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            text.Append(c);
                        }

                        break;
                }
            }

            text.Append('"');
        }
    }
}
