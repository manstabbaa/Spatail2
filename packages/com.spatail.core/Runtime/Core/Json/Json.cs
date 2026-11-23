using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Spatail.Core
{
    /// <summary>
    /// Small dependency-free JSON reader/writer. Objects parse to <see cref="Dictionary{TKey,TValue}"/> of string to object,
    /// arrays to <see cref="List{T}"/> of object, numbers to double, plus string, bool and null.
    /// Kept in-house so the core has no package dependencies and behaves identically under IL2CPP stripping.
    /// </summary>
    public static class Json
    {
        public static object Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var p = new Parser(json);
            object v = p.ParseValue();
            p.SkipWhitespace();
            if (!p.AtEnd) throw new FormatException("Unexpected trailing characters at " + p.Position);
            return v;
        }

        public static bool TryParse(string json, out object value)
        {
            try { value = Parse(json); return true; }
            catch (Exception) { value = null; return false; }
        }

        public static Dictionary<string, object> ParseObject(string json)
        {
            var o = Parse(json) as Dictionary<string, object>;
            if (o == null) throw new FormatException("JSON root is not an object");
            return o;
        }

        public static string Write(object value, bool pretty = false)
        {
            var sb = new StringBuilder(256);
            WriteValue(sb, value, pretty, 0);
            return sb.ToString();
        }

        // ---- typed accessors -------------------------------------------------------------

        public static Dictionary<string, object> Obj(this Dictionary<string, object> o, string key)
        {
            object v;
            return o != null && o.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        public static List<object> Arr(this Dictionary<string, object> o, string key)
        {
            object v;
            return o != null && o.TryGetValue(key, out v) ? v as List<object> : null;
        }

        public static string Str(this Dictionary<string, object> o, string key, string fallback = null)
        {
            object v;
            if (o == null || !o.TryGetValue(key, out v) || v == null) return fallback;
            var s = v as string;
            if (s != null) return s;
            if (v is double) return ((double)v).ToString("R", CultureInfo.InvariantCulture);
            if (v is bool) return (bool)v ? "true" : "false";
            return fallback;
        }

        public static double Num(this Dictionary<string, object> o, string key, double fallback = 0)
        {
            object v;
            if (o == null || !o.TryGetValue(key, out v) || v == null) return fallback;
            return ToDouble(v, fallback);
        }

        public static float Float(this Dictionary<string, object> o, string key, float fallback = 0f) =>
            (float)Num(o, key, fallback);

        public static int Int(this Dictionary<string, object> o, string key, int fallback = 0) =>
            (int)Math.Round(Num(o, key, fallback));

        public static bool Bool(this Dictionary<string, object> o, string key, bool fallback = false)
        {
            object v;
            if (o == null || !o.TryGetValue(key, out v) || v == null) return fallback;
            if (v is bool) return (bool)v;
            var s = v as string;
            if (s != null) return s == "true";
            return fallback;
        }

        public static bool Has(this Dictionary<string, object> o, string key)
        {
            object v;
            return o != null && o.TryGetValue(key, out v) && v != null;
        }

        public static double ToDouble(object v, double fallback = 0)
        {
            if (v is double) return (double)v;
            if (v is float) return (float)v;
            if (v is int) return (int)v;
            if (v is long) return (long)v;
            var s = v as string;
            double d;
            if (s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return fallback;
        }

        /// <summary>Reads a vector written either as [x,y,z] or as {"x":..,"y":..,"z":..}.</summary>
        public static Vec3 Vec(this Dictionary<string, object> o, string key, Vec3 fallback = default(Vec3))
        {
            object v;
            if (o == null || !o.TryGetValue(key, out v) || v == null) return fallback;
            return ToVec3(v, fallback);
        }

        public static Vec3 ToVec3(object v, Vec3 fallback = default(Vec3))
        {
            var list = v as List<object>;
            if (list != null && list.Count >= 3)
                return new Vec3((float)ToDouble(list[0]), (float)ToDouble(list[1]), (float)ToDouble(list[2]));
            var d = v as Dictionary<string, object>;
            if (d != null) return new Vec3(d.Float("x"), d.Float("y"), d.Float("z"));
            return fallback;
        }

        public static List<object> FromVec3(Vec3 v) => new List<object> { Round(v.x), Round(v.y), Round(v.z) };

        public static List<string> StrList(this Dictionary<string, object> o, string key)
        {
            var result = new List<string>();
            var a = o.Arr(key);
            if (a == null) return result;
            foreach (object item in a)
                if (item is string) result.Add((string)item);
            return result;
        }

        /// <summary>Rounds to 4 decimals so serialized poses stay compact and stable across platforms.</summary>
        public static double Round(float f) => Math.Round(f, 4);

        // ---- writer ----------------------------------------------------------------------

        static void WriteValue(StringBuilder sb, object v, bool pretty, int depth)
        {
            if (v == null) { sb.Append("null"); return; }
            var s = v as string;
            if (s != null) { WriteString(sb, s); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is double) { WriteNumber(sb, (double)v); return; }
            if (v is float) { WriteNumber(sb, Math.Round((float)v, 5)); return; }
            if (v is int) { sb.Append(((int)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is long) { sb.Append(((long)v).ToString(CultureInfo.InvariantCulture)); return; }
            var dict = v as IDictionary<string, object>;
            if (dict != null)
            {
                if (dict.Count == 0) { sb.Append("{}"); return; }
                sb.Append('{');
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    NewLine(sb, pretty, depth + 1);
                    WriteString(sb, kv.Key);
                    sb.Append(pretty ? ": " : ":");
                    WriteValue(sb, kv.Value, pretty, depth + 1);
                }
                NewLine(sb, pretty, depth);
                sb.Append('}');
                return;
            }
            var list = v as IEnumerable;
            if (list != null)
            {
                // Arrays of scalars stay on one line even in pretty mode; it keeps vectors readable.
                bool scalars = true;
                int count = 0;
                foreach (object item in list)
                {
                    count++;
                    if (item is IDictionary<string, object> || (item is IEnumerable && !(item is string))) scalars = false;
                }
                if (count == 0) { sb.Append("[]"); return; }
                sb.Append('[');
                bool first = true;
                foreach (object item in list)
                {
                    if (!first) sb.Append(pretty && scalars ? ", " : ",");
                    first = false;
                    if (!scalars) NewLine(sb, pretty, depth + 1);
                    WriteValue(sb, item, pretty, depth + 1);
                }
                if (!scalars) NewLine(sb, pretty, depth);
                sb.Append(']');
                return;
            }
            WriteString(sb, v.ToString());
        }

        static void NewLine(StringBuilder sb, bool pretty, int depth)
        {
            if (!pretty) return;
            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }

        static void WriteNumber(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append('0'); return; }
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15) sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
            else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---- parser ----------------------------------------------------------------------

        sealed class Parser
        {
            readonly string _s;
            int _i;

            public Parser(string s) { _s = s; }
            public bool AtEnd => _i >= _s.Length;
            public int Position => _i;

            public void SkipWhitespace()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\n' || c == '\r' || c == '\t' || c == '﻿') _i++;
                    else break;
                }
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (AtEnd) throw new FormatException("Unexpected end of JSON");
                char c = _s[_i];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ParseNumber();
                }
            }

            Dictionary<string, object> ParseObject()
            {
                var d = new Dictionary<string, object>();
                _i++; // {
                SkipWhitespace();
                if (!AtEnd && _s[_i] == '}') { _i++; return d; }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != '"') throw new FormatException("Expected property name at " + _i);
                    string key = ParseString();
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != ':') throw new FormatException("Expected ':' at " + _i);
                    _i++;
                    d[key] = ParseValue();
                    SkipWhitespace();
                    if (AtEnd) throw new FormatException("Unterminated object");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == '}') { _i++; return d; }
                    throw new FormatException("Expected ',' or '}' at " + _i);
                }
            }

            List<object> ParseArray()
            {
                var l = new List<object>();
                _i++; // [
                SkipWhitespace();
                if (!AtEnd && _s[_i] == ']') { _i++; return l; }
                while (true)
                {
                    l.Add(ParseValue());
                    SkipWhitespace();
                    if (AtEnd) throw new FormatException("Unterminated array");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == ']') { _i++; return l; }
                    throw new FormatException("Expected ',' or ']' at " + _i);
                }
            }

            string ParseString()
            {
                var sb = new StringBuilder();
                _i++; // opening quote
                while (true)
                {
                    if (AtEnd) throw new FormatException("Unterminated string");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw new FormatException("Unterminated escape");
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
                            if (_i + 4 > _s.Length) throw new FormatException("Bad unicode escape");
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default: throw new FormatException("Bad escape '\\" + e + "' at " + _i);
                    }
                }
            }

            object ParseNumber()
            {
                int start = _i;
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') _i++;
                    else break;
                }
                if (_i == start) throw new FormatException("Unexpected character '" + _s[_i] + "' at " + _i);
                double d;
                if (!double.TryParse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    throw new FormatException("Bad number at " + start);
                return d;
            }

            void Expect(string word)
            {
                if (_i + word.Length > _s.Length || string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0)
                    throw new FormatException("Unexpected token at " + _i);
                _i += word.Length;
            }
        }
    }
}
