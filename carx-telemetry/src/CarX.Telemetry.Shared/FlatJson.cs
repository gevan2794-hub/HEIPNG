using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CarX.Telemetry
{
    /// <summary>
    /// Minimal writer/reader for the one JSON shape this protocol uses: a flat object
    /// whose values are numbers, strings or booleans. Hand-rolled on purpose -- the mod
    /// runs inside the game's Unity domain, where pulling in Newtonsoft (or whichever
    /// version the game already loaded) is a good way to get an assembly conflict.
    /// </summary>
    public static class FlatJson
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Write(TelemetryFrame frame)
        {
            var sb = new StringBuilder(1024);
            sb.Append('{');

            WriteKey(sb, "Sequence");
            sb.Append(frame.Sequence.ToString(Inv));
            sb.Append(',');

            WriteKey(sb, "TimestampMs");
            WriteNumber(sb, frame.TimestampMs);
            sb.Append(',');

            WriteKey(sb, "InCar");
            sb.Append(frame.InCar ? "true" : "false");

            foreach (var kv in frame.Numbers)
            {
                sb.Append(',');
                WriteKey(sb, kv.Key);
                WriteNumber(sb, kv.Value);
            }

            foreach (var kv in frame.Strings)
            {
                sb.Append(',');
                WriteKey(sb, kv.Key);
                WriteString(sb, kv.Value);
            }

            sb.Append('}');
            return sb.ToString();
        }

        private static void WriteKey(StringBuilder sb, string key)
        {
            WriteString(sb, key);
            sb.Append(':');
        }

        private static void WriteNumber(StringBuilder sb, double value)
        {
            // JSON has no NaN/Infinity. A channel that goes non-finite is a bug upstream,
            // but emitting an unparseable packet would take the whole frame down with it.
            if (double.IsNaN(value) || double.IsInfinity(value)) { sb.Append('0'); return; }
            sb.Append(value.ToString("0.######", Inv));
        }

        private static void WriteString(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", Inv));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        /// <summary>
        /// Parses a flat object. Numbers and booleans land in <paramref name="numbers"/>
        /// (booleans as 0/1, which is what SimHub wants anyway); strings land in
        /// <paramref name="strings"/>. Returns false on anything malformed rather than
        /// throwing -- a torn UDP packet should cost one frame, not the receive loop.
        /// </summary>
        public static bool TryRead(string json, Dictionary<string, double> numbers, Dictionary<string, string> strings)
        {
            if (string.IsNullOrEmpty(json)) return false;

            int i = 0;
            SkipWs(json, ref i);
            if (i >= json.Length || json[i] != '{') return false;
            i++;

            SkipWs(json, ref i);
            if (i < json.Length && json[i] == '}') return true;

            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (!TryReadString(json, ref i, out var key)) return false;

                SkipWs(json, ref i);
                if (i >= json.Length || json[i] != ':') return false;
                i++;
                SkipWs(json, ref i);
                if (i >= json.Length) return false;

                if (json[i] == '"')
                {
                    if (!TryReadString(json, ref i, out var s)) return false;
                    strings[key] = s;
                }
                else if (json[i] == 't' || json[i] == 'f' || json[i] == 'n')
                {
                    int start = i;
                    while (i < json.Length && char.IsLetter(json[i])) i++;
                    var lit = json.Substring(start, i - start);
                    if (lit == "true") numbers[key] = 1;
                    else if (lit == "false") numbers[key] = 0;
                    else if (lit == "null") numbers[key] = 0;
                    else return false;
                }
                else
                {
                    int start = i;
                    while (i < json.Length && "+-.eE0123456789".IndexOf(json[i]) >= 0) i++;
                    if (i == start) return false;
                    if (!double.TryParse(json.Substring(start, i - start), NumberStyles.Float, Inv, out var d)) return false;
                    numbers[key] = d;
                }

                SkipWs(json, ref i);
                if (i >= json.Length) return false;
                if (json[i] == ',') { i++; continue; }
                if (json[i] == '}') return true;
                return false;
            }

            return false;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }

        private static bool TryReadString(string s, ref int i, out string result)
        {
            result = null;
            if (i >= s.Length || s[i] != '"') return false;
            i++;
            var sb = new StringBuilder(32);
            while (i < s.Length)
            {
                var c = s[i++];
                if (c == '"') { result = sb.ToString(); return true; }
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) return false;
                var e = s[i++];
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
                        if (i + 4 > s.Length) return false;
                        if (!int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, Inv, out var cp)) return false;
                        sb.Append((char)cp);
                        i += 4;
                        break;
                    default: return false;
                }
            }
            return false;
        }
    }
}
