using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Miloun.Cosmo
{
    /// <summary>
    /// A faithful subset of ICU MessageFormat: argument substitution plus
    /// <c>plural</c> / <c>selectordinal</c> / <c>select</c> with <c>#</c>, backed by
    /// ICU plural rules and number formatting. The native C <c>umsg</c> API supports
    /// neither named arguments nor a non-varargs entry point, so Cosmo parses the
    /// pattern here and defers every locale-sensitive decision (plural category,
    /// number rendering) to ICU.
    /// </summary>
    internal static class MessageFormatter
    {
        public static string Format(Cosmo cosmo, string pattern, Func<string, object?> lookup)
        {
            int i = 0;
            return Render(cosmo, pattern, ref i, lookup, hash: null, stopAtBrace: false);
        }

        private static string Render(Cosmo cosmo, string s, ref int i, Func<string, object?> lookup, double? hash, bool stopAtBrace)
        {
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\'') { i++; AppendQuoted(s, ref i, sb); continue; }
                if (c == '}' && stopAtBrace) break;
                if (c == '#' && hash.HasValue) { sb.Append(cosmo.Number(hash.Value)); i++; continue; }
                if (c == '{') { i++; sb.Append(RenderArg(cosmo, s, ref i, lookup)); continue; }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        // ICU quoting: '' is a literal apostrophe; 'xyz' quotes the run verbatim.
        private static void AppendQuoted(string s, ref int i, StringBuilder sb)
        {
            if (i < s.Length && s[i] == '\'') { sb.Append('\''); i++; return; }
            while (i < s.Length && s[i] != '\'') { sb.Append(s[i]); i++; }
            if (i < s.Length) i++; // closing quote
        }

        // Called just after a '{'. Consumes through the matching '}'.
        private static string RenderArg(Cosmo cosmo, string s, ref int i, Func<string, object?> lookup)
        {
            SkipWs(s, ref i);
            string name = ReadToken(s, ref i);
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return Stringify(lookup(name)); }
            if (i >= s.Length || s[i] != ',') throw new CosmoArgumentException("Malformed placeholder in message pattern.");
            i++; // ',' before the format type
            SkipWs(s, ref i);
            string type = ReadToken(s, ref i);
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; SkipWs(s, ref i); } // ',' before the branches

            if (type == "plural" || type == "selectordinal")
            {
                // Optional "offset:N" prefix.
                double offset = 0;
                if (i + 7 <= s.Length && s.Substring(i, 7) == "offset:")
                {
                    i += 7;
                    int start = i;
                    while (i < s.Length && s[i] != ' ' && s[i] != '{') i++;
                    double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out offset);
                    SkipWs(s, ref i);
                }
                var branches = ReadBranches(s, ref i);
                double value = ToDouble(lookup(name));
                string raw = ChoosePlural(cosmo, branches, value, ordinal: type == "selectordinal");
                int j = 0;
                return Render(cosmo, raw, ref j, lookup, hash: value - offset, stopAtBrace: false);
            }
            if (type == "select")
            {
                var branches = ReadBranches(s, ref i);
                string key = Stringify(lookup(name));
                string raw = branches.TryGetValue(key, out var r) ? r : (branches.TryGetValue("other", out var o) ? o : "");
                int j = 0;
                return Render(cosmo, raw, ref j, lookup, hash: null, stopAtBrace: false);
            }
            if (type == "number")
            {
                int s0 = i;
                ReadToBrace(s, ref i);
                string style = s.Substring(s0, i - s0).Trim();
                if (i < s.Length && s[i] == '}') i++;
                double val = ToDouble(lookup(name));
                return style == "integer"
                    ? cosmo.Number(val, new NumberOptions { MaximumFractionDigits = 0 })
                    : cosmo.Number(val);
            }
            // Other subformats (date/time/...): consume to the matching '}' and fall
            // back to the raw argument value.
            ReadToBrace(s, ref i);
            if (i < s.Length && s[i] == '}') i++;
            return Stringify(lookup(name));
        }

        private static Dictionary<string, string> ReadBranches(string s, ref int i)
        {
            var branches = new Dictionary<string, string>(StringComparer.Ordinal);
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] == '}') { if (i < s.Length) i++; break; }
                string keyword = ReadToken(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '{') throw new CosmoArgumentException("Malformed plural/select branch in message pattern.");
                i++; // '{'
                branches[keyword] = ReadBalanced(s, ref i);
            }
            return branches;
        }

        private static string ChoosePlural(Cosmo cosmo, Dictionary<string, string> branches, double value, bool ordinal)
        {
            string exact = "=" + value.ToString(CultureInfo.InvariantCulture);
            if (branches.TryGetValue(exact, out var e)) return e;
            if (value == Math.Floor(value))
            {
                string exactInt = "=" + ((long)value).ToString(CultureInfo.InvariantCulture);
                if (branches.TryGetValue(exactInt, out var ei)) return ei;
            }
            string cat = cosmo.PluralCategory(value, ordinal);
            if (branches.TryGetValue(cat, out var c)) return c;
            return branches.TryGetValue("other", out var o) ? o : "";
        }

        // i points just after '{'; returns inner text, advances past the matching '}'.
        private static string ReadBalanced(string s, ref int i)
        {
            int depth = 1, start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\'') { i++; while (i < s.Length && s[i] != '\'') i++; if (i < s.Length) i++; continue; }
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) { var inner = s.Substring(start, i - start); i++; return inner; } }
                i++;
            }
            throw new CosmoArgumentException("Unbalanced braces in message pattern.");
        }

        private static void ReadToBrace(string s, ref int i)
        {
            int depth = 0;
            while (i < s.Length)
            {
                if (s[i] == '{') depth++;
                else if (s[i] == '}') { if (depth == 0) return; depth--; }
                i++;
            }
        }

        private static void SkipWs(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        private static string ReadToken(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != ',' && s[i] != '{' && s[i] != '}') i++;
            return s.Substring(start, i - start);
        }

        private static string Stringify(object? v) => v switch
        {
            null => "",
            string str => str,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => v.ToString() ?? "",
        };

        private static double ToDouble(object? v) => v switch
        {
            null => 0,
            double d => d,
            string str => double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : 0,
            IConvertible c => c.ToDouble(CultureInfo.InvariantCulture),
            _ => 0,
        };
    }
}
