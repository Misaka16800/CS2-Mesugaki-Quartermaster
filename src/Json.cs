using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Cs2Roulette
{
    /// <summary>极简 JSON 解析器（递归下降）。仅依赖 BCL，避免第三方库。</summary>
    internal static class Json
    {
        public static object Parse(string s)
        {
            int i = 0;
            var v = ParseValue(s, ref i);
            SkipWs(s, ref i);
            return v;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') { i++; continue; }
                break;
            }
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON 意外结束");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default: return ParseNumber(s, ref i);
            }
        }

        private static void Expect(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                throw new FormatException("JSON 期待 " + word + " @ " + i);
            i += word.Length;
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipWs(s, ref i);
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (s[i] != ':') throw new FormatException("JSON 期待 : @ " + i);
                i++;
                d[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON 对象未闭合");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return d; }
                throw new FormatException("JSON 期待 , 或 } @ " + i);
            }
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON 数组未闭合");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return list; }
                throw new FormatException("JSON 期待 , 或 ] @ " + i);
            }
        }

        private static string ParseString(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (s[i] != '"') throw new FormatException("JSON 期待字符串 @ " + i);
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("JSON 字符串未闭合");
                char c = s[i++];
                if (c == '"') break;
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
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
                        string hex = s.Substring(i, 4);
                        i += 4;
                        sb.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        break;
                    default: throw new FormatException("未知转义 \\" + e);
                }
            }
            return sb.ToString();
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' ||
                                    s[i] == '.' || s[i] == 'e' || s[i] == 'E'))
                i++;
            string tok = s.Substring(start, i - start);
            if (tok.IndexOf('.') >= 0 || tok.IndexOf('e') >= 0 || tok.IndexOf('E') >= 0)
            {
                double d;
                if (double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    return d;
            }
            long l;
            if (long.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out l))
                return l;
            double dd;
            if (double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out dd))
                return dd;
            throw new FormatException("JSON 非法数字: " + tok);
        }

        // ---------------- 取值辅助 ----------------
        public static Dictionary<string, object> Obj(object o)
        {
            return (Dictionary<string, object>)o;
        }

        public static List<object> Arr(object o)
        {
            return (List<object>)o;
        }

        public static string Str(Dictionary<string, object> o, string key, string def = "")
        {
            object v;
            if (o != null && o.TryGetValue(key, out v) && v != null) return Convert.ToString(v, CultureInfo.InvariantCulture);
            return def;
        }

        public static int Int(Dictionary<string, object> o, string key, int def = 0)
        {
            object v;
            if (o != null && o.TryGetValue(key, out v) && v != null)
            {
                if (v is long) return (int)(long)v;
                if (v is double) return (int)(double)v;
                int r;
                if (int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out r)) return r;
            }
            return def;
        }

        public static long Long(Dictionary<string, object> o, string key, long def = 0)
        {
            object v;
            if (o != null && o.TryGetValue(key, out v) && v != null)
            {
                if (v is long) return (long)v;
                if (v is double) return (long)(double)v;
                long r;
                if (long.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out r)) return r;
            }
            return def;
        }

        public static bool Bool(Dictionary<string, object> o, string key, bool def = false)
        {
            object v;
            if (o != null && o.TryGetValue(key, out v) && v != null && v is bool) return (bool)v;
            return def;
        }

        public static List<object> ArrOf(Dictionary<string, object> o, string key)
        {
            object v;
            if (o != null && o.TryGetValue(key, out v) && v is List<object>) return (List<object>)v;
            return new List<object>();
        }
    }
}
