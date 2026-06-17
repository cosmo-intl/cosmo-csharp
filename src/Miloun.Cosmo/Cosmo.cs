using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Miloun.Cosmo
{
    /// <summary>
    /// Cosmo — application localisation for .NET.
    ///
    /// <para>A thin, ergonomic layer over <strong>ICU</strong> (reached through the
    /// native ICU4C library). It bundles <strong>no</strong> locale data of its
    /// own — every result comes straight from ICU. Construct once per locale and
    /// reuse.</para>
    ///
    /// <para>A few ICU4J features have no ICU C binding and therefore throw
    /// <see cref="CosmoUnsupportedException"/>: <see cref="PersonName"/> (needs
    /// ICU 73+), <see cref="IndexBuckets"/> (AlphabeticIndex is C++-only),
    /// <see cref="BestMatch"/> / negotiated <see cref="FromAcceptLanguage(string, IEnumerable{string})"/>
    /// (no C LocaleMatcher), and <c>SupportedValues("unit")</c>.</para>
    /// </summary>
    public sealed class Cosmo
    {
        private static readonly HashSet<string> DurationUnits = new HashSet<string>
            { "years", "months", "weeks", "days", "hours", "minutes", "seconds", "milliseconds" };

        private static readonly string[] DurationOrder =
            { "years", "months", "weeks", "days", "hours", "minutes", "seconds", "milliseconds" };

        private static readonly Dictionary<string, string> RangeSkeletons = new Dictionary<string, string>
        {
            ["short|none"] = "yMd", ["medium|none"] = "yMMMd", ["long|none"] = "yMMMMd", ["full|none"] = "yMMMMEEEEd",
            ["none|short"] = "jm", ["none|medium"] = "jms", ["medium|short"] = "yMMMdjm", ["short|short"] = "yMdjm",
        };

        /// <summary>Canonical ICU locale id, e.g. <c>"en_AU"</c>.</summary>
        public string Locale { get; }
        /// <summary>Parsed language / script / region subtags.</summary>
        public Subtags Subtags { get; }
        /// <summary>Resolved modifiers (calendar / currency / timeZone).</summary>
        public Modifiers Modifiers { get; }

        /// <summary>Builds a Cosmo for the given locale, optionally with modifier overrides.</summary>
        /// <param name="locale">BCP-47 or underscore id (<c>en_AU</c>, <c>fa-IR</c>); null/blank uses the system default.</param>
        /// <param name="modifiers">Optional calendar / currency / timeZone overrides.</param>
        public Cosmo(string? locale, Modifiers? modifiers = null)
        {
            string raw = (locale ?? "").Trim();
            if (raw.Length == 0) raw = Icu.GetDefaultLocale();
            Locale = Icu.Canonicalize(raw);
            Subtags = new Subtags(Icu.GetLanguage(Locale), Icu.GetScript(Locale), Icu.GetCountry(Locale));

            modifiers ??= Modifiers.None;
            string? calendar = modifiers.Calendar ?? NullIfEmpty(Icu.GetKeyword(Locale, "calendar"));
            string? currency = modifiers.Currency;
            if (currency == null && Subtags.Region.Length != 0)
            {
                var inferred = Icu.CurrencyForLocale(Locale);
                if (inferred != null && inferred != "XXX") currency = inferred;
            }
            Modifiers = new Modifiers(calendar, currency, modifiers.TimeZone);
        }

        // ---------------------------------------------------------------- //
        // factories
        // ---------------------------------------------------------------- //

        /// <summary>Builds a Cosmo from locale subtags, e.g. <c>new Subtags("en", "", "AU")</c>.</summary>
        public static Cosmo FromSubtags(Subtags subtags, Modifiers? modifiers = null)
        {
            var sb = new StringBuilder();
            if (subtags.Language.Length != 0) sb.Append(subtags.Language);
            if (subtags.Script.Length != 0) sb.Append('-').Append(subtags.Script);
            if (subtags.Region.Length != 0) sb.Append('-').Append(subtags.Region);
            return new Cosmo(sb.ToString(), modifiers);
        }

        /// <summary>Builds a Cosmo from an HTTP <c>Accept-Language</c> header, picking the best-quality tag.</summary>
        public static Cosmo FromAcceptLanguage(string? header, Modifiers? modifiers = null)
        {
            var tags = ParseAcceptLanguage(header);
            return new Cosmo(tags.Count == 0 ? null : tags[0], modifiers);
        }

        /// <summary>
        /// Negotiating variant — picks the supported locale that best serves the header.
        /// <strong>Unsupported on the C API</strong> (no CLDR-distance LocaleMatcher).
        /// </summary>
        public static Cosmo FromAcceptLanguage(string? header, IEnumerable<string> supported, Modifiers? modifiers = null)
            => throw new CosmoUnsupportedException(
                "Negotiated Accept-Language matching needs ICU's LocaleMatcher, which has no C binding.");

        private static List<string> ParseAcceptLanguage(string? header)
        {
            var entries = new List<(string tag, double q, int order)>();
            int order = 0;
            foreach (var part in (header ?? "").Split(','))
            {
                var pieces = part.Trim().Split(';');
                var tag = pieces[0].Trim();
                if (tag.Length == 0 || tag == "*") continue;
                double q = 1.0;
                for (int i = 1; i < pieces.Length; i++)
                {
                    var p = pieces[i].Trim();
                    if (p.StartsWith("q=", StringComparison.Ordinal))
                        q = double.TryParse(p.Substring(2), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
                }
                entries.Add((tag, q, order++));
            }
            return entries.OrderByDescending(e => e.q).ThenBy(e => e.order).Select(e => e.tag).ToList();
        }

        // ---------------------------------------------------------------- //
        // resource-bundle access
        // ---------------------------------------------------------------- //

        // ---------------------------------------------------------------- //
        // key → value lookups
        // ---------------------------------------------------------------- //

        /// <summary>Localised name of this locale's own language.</summary>
        public string Language() => Language(Locale);

        /// <summary>Localised language name (<c>"en"</c> → <c>"English"</c>).</summary>
        public string Language(string? code)
            => string.IsNullOrEmpty(code) ? "" : Icu.DisplayLanguage(code!.Replace('-', '_'), Locale);

        /// <summary>Localised name of this locale's own region.</summary>
        public string Country() => Country(Subtags.Region);

        /// <summary>Localised country/region name (<c>"AU"</c> → <c>"Australia"</c>).</summary>
        public string Country(string? code)
        {
            if (string.IsNullOrEmpty(code)) return "";
            var id = code!.Replace('-', '_');
            if (!id.Contains('_')) id = "_" + id; // ULocale needs a region context.
            return Icu.DisplayCountry(id, Locale);
        }

        /// <summary>Localised name of this locale's own script (<c>""</c> when it has none).</summary>
        public string Script() => Script(Subtags.Script);

        /// <summary>Localised script name (<c>"Hans"</c> → <c>"Simplified Han"</c>).</summary>
        public string Script(string? code)
        {
            if (string.IsNullOrEmpty(code)) return "";
            var titled = char.ToUpperInvariant(code![0]) + code.Substring(1).ToLowerInvariant();
            return Icu.DisplayScript("und_" + titled, Locale);
        }

        /// <summary>Localised calendar name (<c>"buddhist"</c> → <c>"Buddhist Calendar"</c>).</summary>
        public string Calendar(string? code)
        {
            if (string.IsNullOrEmpty(code)) return "";
            return Icu.DisplayCalendar("und@calendar=" + code, Locale);
        }

        /// <summary>Text direction of this locale: <c>"rtl"</c> or <c>"ltr"</c>.</summary>
        public string Direction() => Direction(Locale);

        /// <summary>Text direction of a language: <c>"rtl"</c> or <c>"ltr"</c> (script-based).</summary>
        public string Direction(string? language)
        {
            if (string.IsNullOrEmpty(language)) return "ltr";
            try { return Icu.IsRtl(language!.Replace('-', '_')) ? "rtl" : "ltr"; }
            catch { return "ltr"; }
        }

        /// <summary>Country flag emoji for this locale's region (<c>""</c> when it has none).</summary>
        public string Flag() => Flag(Subtags.Region);

        /// <summary>Country flag emoji for a region (<c>"AU"</c> → 🇦🇺). Pure codepoint math.</summary>
        public string Flag(string? country)
        {
            var cc = (country ?? "").ToUpperInvariant();
            if (cc.Length != 2 || cc[0] < 'A' || cc[0] > 'Z' || cc[1] < 'A' || cc[1] > 'Z') return "";
            const int offset = 0x1F1E6 - 'A';
            return char.ConvertFromUtf32(cc[0] + offset) + char.ConvertFromUtf32(cc[1] + offset);
        }

        /// <summary>Localised name of the <c>currency</c> modifier's currency.</summary>
        public string Currency() => Currency(null, false, false);

        /// <summary>Localised currency name (<c>"AUD"</c> → <c>"Australian Dollar"</c>) or symbol.</summary>
        /// <param name="code">ISO 4217 code; null falls back to the <c>currency</c> modifier.</param>
        /// <param name="symbol">Return the symbol (<c>"A$"</c>) instead of the name.</param>
        /// <param name="strict">Throw on an unknown code instead of echoing it back.</param>
        public string Currency(string? code, bool symbol = false, bool strict = false)
        {
            var ccy = (code ?? Modifiers.Currency ?? "").ToUpperInvariant().Trim();
            if (ccy.Length == 0) return "";
            var name = Icu.CurrencyName(ccy, Locale, symbol ? Icu.UCURR_SYMBOL_NAME : Icu.UCURR_LONG_NAME, out bool unknown);
            if (strict && unknown) throw new CosmoArgumentException($"\"{ccy}\" is not a valid currency code.");
            return name;
        }

        // ---------------------------------------------------------------- //
        // numbers
        // ---------------------------------------------------------------- //

        /// <summary>Format a number with the locale's default decimal format.</summary>
        public string Number(double value, NumberOptions? options = null)
            => Icu.FormatSkeleton(Locale, BuildSkeleton("", options, Fraction(0, 3)), value);

        /// <summary>Formats a number with a fixed number of fraction digits — always
        /// exactly <paramref name="fractionDigits"/>, padding with trailing zeros and
        /// rounding as needed. Use it when <c>1</c> should render as <c>"1.00"</c> and
        /// <c>1.002</c> should stay <c>"1.00"</c>, never <c>"1.0"</c>. Pass
        /// <see cref="NumberOptions"/> to widen the band or tweak rounding/grouping.</summary>
        public string Precision(double value, int fractionDigits = 2, NumberOptions? options = null)
            => Icu.FormatSkeleton(Locale, BuildSkeleton("", options, Fraction(fractionDigits, fractionDigits)), value);

        /// <summary>Format a fraction as a percentage (<c>0.2</c> → <c>"20%"</c>).</summary>
        public string Percentage(double value, int precision = 3, NumberOptions? options = null)
            => Icu.FormatSkeleton(Locale, BuildSkeleton("percent scale/100", options, Fraction(0, precision)), value);

        /// <summary>Formats a monetary value using the <c>currency</c> modifier.</summary>
        public string Money(double value) => Money(value, null, null, false, null);

        /// <summary>Formats a monetary value in the given ISO 4217 currency.</summary>
        public string Money(double value, string? code, int? precision = null, bool strict = false, NumberOptions? options = null)
        {
            var ccy = (code ?? Modifiers.Currency ?? "").ToUpperInvariant();
            if (ccy.Length == 0)
            {
                if (strict) throw new CosmoArgumentException("No currency provided. Pass a code or set the `currency` modifier.");
                return "";
            }
            if (!IsCurrencyCode(ccy)) throw new CosmoArgumentException($"\"{ccy}\" is not a valid currency code.");
            string? prec = precision.HasValue ? Fraction(precision.Value, precision.Value) : null;
            return Icu.FormatSkeleton(Locale, BuildSkeleton("currency/" + ccy, options, prec), value);
        }

        /// <summary>Returns a localised number symbol (<c>"decimal"</c>, <c>"percent"</c>, …).</summary>
        public string Symbol(string name)
        {
            var key = new string(name.ToLowerInvariant().Where(c => c != '_' && c != ' ' && c != '-').ToArray());
            foreach (var suffix in new[] { "symbol", "separator", "sign" })
                if (key.EndsWith(suffix, StringComparison.Ordinal) && key.Length > suffix.Length)
                    key = key.Substring(0, key.Length - suffix.Length);
            int sym = key switch
            {
                "decimal" => 0,
                "grouping" or "group" => 1,
                "pattern" => 2,
                "percent" => 3,
                "zerodigit" or "zero" => 4,
                "digit" => 5,
                "minus" => 6,
                "plus" => 7,
                "currency" => 8,
                "intlcurrency" => 9,
                "monetary" => 10,
                "exponential" or "exponent" => 11,
                "permill" or "permille" => 12,
                "padescape" or "pad" => 13,
                "infinity" => 14,
                "nan" => 15,
                "significantdigit" or "significant" => 16,
                _ => throw new CosmoArgumentException($"\"{name}\" is not a valid number-symbol name."),
            };
            return Icu.Symbol(Locale, sym);
        }

        /// <summary>Format a measurement with a localised unit (<c>2.19</c> gigabytes).</summary>
        public string Unit(string category, string unit, double value, string width = "full")
        {
            string ws = width switch
            {
                "none" or "long" or "full" => "unit-width-full-name",
                "medium" => "unit-width-short",
                "short" => "unit-width-narrow",
                _ => throw InvalidWidth(width),
            };
            try { return Icu.FormatSkeleton(Locale, $"unit/{unit} {ws}", value); }
            catch (CosmoException) { throw new CosmoArgumentException($"\"{unit}\" is not a unit supported by ICU."); }
        }

        /// <summary>Scientific notation (<c>12345</c> → <c>"1.2345E4"</c>).</summary>
        public string Scientific(double value) => Icu.FormatSkeleton(Locale, "scientific", value);

        /// <summary>Compact notation (<c>1200</c> → <c>"1.2K"</c>); <c>"long"</c>/<c>"full"</c> → <c>"1.2 thousand"</c>.</summary>
        public string Compact(double value, string width = "short")
            => Icu.FormatSkeleton(Locale, width is "full" or "long" ? "compact-long" : "compact-short", value);

        /// <summary>Ordinal text (<c>1</c> → <c>"1st"</c>). Uses ICU RBNF.</summary>
        public string Ordinal(long number) => Icu.Rbnf(Locale, Icu.UNUM_ORDINAL, number, null);

        /// <summary>Spell a number out (<c>42</c> → <c>"forty-two"</c>). Uses ICU RBNF.</summary>
        public string Spellout(double number) => Icu.Rbnf(Locale, Icu.UNUM_SPELLOUT, number, null);

        /// <summary>Format an undirected duration in seconds as the clock form (<c>"339:17:20"</c>).</summary>
        public string Duration(double seconds, bool withWords = false)
            => Icu.Rbnf(Locale, Icu.UNUM_DURATION, seconds, withWords ? "%with-words" : null);

        /// <summary>Format a multi-unit duration (<c>{hours:3, minutes:5}</c> → <c>"3 hr, 5 min"</c>).</summary>
        public string Duration(IDictionary<string, double> parts, bool withWords = false)
        {
            string ws = withWords ? "unit-width-full-name" : "unit-width-short";
            var pieces = new List<string>();
            foreach (var unit in DurationOrder)
            {
                if (parts.TryGetValue(unit, out var amount) && amount != 0)
                    pieces.Add(Icu.FormatSkeleton(Locale, $"unit/{unit.Substring(0, unit.Length - 1)} {ws}", amount));
            }
            if (pieces.Count == 0) return "";
            return Icu.JoinList(Locale, Icu.ULISTFMT_UNITS, withWords ? Icu.ULISTFMT_WIDE : Icu.ULISTFMT_SHORT, pieces);
        }

        // ---------------------------------------------------------------- //
        // dates & times
        // ---------------------------------------------------------------- //

        /// <summary>Format a date and/or time using the locale's conventions.</summary>
        public string Moment(DateTimeOffset value, string dateWidth, string timeWidth)
        {
            int d = DateStyle(dateWidth), t = DateStyle(timeWidth);
            if (d == Icu.UDAT_NONE && t == Icu.UDAT_NONE) return "";
            return Icu.FormatDate(d, t, CalendarLocale(null), Modifiers.TimeZone, value.ToUnixTimeMilliseconds());
        }

        /// <summary>Format just the date part of a moment.</summary>
        public string Date(DateTimeOffset value, string width = "short") => Moment(value, width, "none");

        /// <summary>Format just the time (clock) part of a moment.</summary>
        public string Time(DateTimeOffset value, string width = "short") => Moment(value, "none", width);

        /// <summary>Format a moment with a raw ICU pattern (<c>"yyyy-MM-dd"</c>), optionally forcing a calendar.</summary>
        public string FormatMoment(DateTimeOffset value, string pattern, string? calendar = null)
            => Icu.FormatPattern(CalendarLocale(calendar), Modifiers.TimeZone, pattern, value.ToUnixTimeMilliseconds());

        /// <summary>Format a moment range; supports the documented width combinations only.</summary>
        public string DateRange(DateTimeOffset start, DateTimeOffset end, string dateWidth = "medium", string timeWidth = "none")
        {
            DateStyle(dateWidth); DateStyle(timeWidth); // validate
            if (!RangeSkeletons.TryGetValue(dateWidth + "|" + timeWidth, out var skeleton))
                throw new CosmoArgumentException("dateRange supports the documented width combinations only.");
            return Icu.FormatInterval(CalendarLocale(null), skeleton, Modifiers.TimeZone, start.ToUnixTimeMilliseconds(), end.ToUnixTimeMilliseconds());
        }

        // ---------------------------------------------------------------- //
        // collation
        // ---------------------------------------------------------------- //

        /// <summary>Locale-aware comparison of two strings (negative / zero / positive).</summary>
        public int Compare(string a, string b, CollationOptions? options = null)
        {
            using var c = new Icu.Collator(Locale);
            ApplyCollation(c, options);
            return c.Compare(a, b);
        }

        /// <summary>A new list sorted by the locale's collation rules.</summary>
        public List<string> Sort(IEnumerable<string> items, CollationOptions? options = null)
            => Sort(items, x => x, options);

        /// <summary>Sort a collection of arbitrary items by a string key.</summary>
        public List<T> Sort<T>(IEnumerable<T> items, Func<T, string> key, CollationOptions? options = null)
        {
            using var c = new Icu.Collator(Locale);
            ApplyCollation(c, options);
            var list = items.ToList();
            list.Sort((a, b) => c.Compare(key(a), key(b)));
            return list;
        }

        /// <summary>Locale-aware substring test (accents/case can be ignored).</summary>
        /// <param name="sensitivity">base / accent / case / variant.</param>
        public bool Contains(string haystack, string needle, string sensitivity = "base", CollationOptions? options = null)
        {
            if (needle.Length == 0) return true;
            using var c = new Icu.Collator(Locale);
            switch (sensitivity)
            {
                case "base": c.SetStrength(Icu.UCOL_PRIMARY); break;
                case "accent": c.SetStrength(Icu.UCOL_SECONDARY); break;
                case "case": c.SetStrength(Icu.UCOL_PRIMARY); c.SetCaseLevel(true); break;
                case "variant": c.SetStrength(Icu.UCOL_TERTIARY); break;
                default: throw new CosmoArgumentException($"\"{sensitivity}\" is not a valid sensitivity.");
            }
            ApplyCollation(c, options);
            var hay = Graphemes(haystack);
            int needLen = Graphemes(needle).Count;
            for (int i = 0; i + needLen <= hay.Count; i++)
                if (c.Compare(string.Concat(hay.GetRange(i, needLen)), needle) == 0) return true;
            return false;
        }

        // ---------------------------------------------------------------- //
        // text segmentation
        // ---------------------------------------------------------------- //

        /// <summary>Split text into grapheme clusters (combining marks / emoji stay intact).</summary>
        public List<string> SplitGraphemes(string text) => Graphemes(text);

        /// <summary>Split text into words (drops whitespace/punctuation).</summary>
        public List<string> SplitWords(string text)
        {
            var outp = new List<string>();
            foreach (var (start, end, status) in Icu.Boundaries(Icu.UBRK_WORD, Locale, text))
                if (status >= Icu.UBRK_WORD_NONE_LIMIT) outp.Add(text.Substring(start, end - start));
            return outp;
        }

        /// <summary>Split text into sentences using the locale's boundary rules.</summary>
        public List<string> SplitSentences(string text)
        {
            var outp = new List<string>();
            foreach (var (start, end, _) in Icu.Boundaries(Icu.UBRK_SENTENCE, Locale, text))
            {
                var piece = text.Substring(start, end - start).Trim();
                if (piece.Length != 0) outp.Add(piece);
            }
            return outp;
        }

        /// <summary>Truncate to at most <paramref name="maxGraphemes"/> graphemes on a word boundary.</summary>
        public string Ellipsize(string text, int maxGraphemes, string ellipsis = "…")
        {
            var graphemes = Graphemes(text);
            if (graphemes.Count <= maxGraphemes) return text;
            int budget = Math.Max(0, maxGraphemes - Graphemes(ellipsis).Count);
            var head = string.Concat(graphemes.GetRange(0, budget));
            int boundary = Icu.PrecedingWordBoundary(Locale, head, head.Length);
            if (boundary > 0)
            {
                var cut = head.Substring(0, boundary).TrimEnd();
                if (cut.Length != 0) return cut + ellipsis;
            }
            return head.TrimEnd() + ellipsis;
        }

        // ---------------------------------------------------------------- //
        // messages, plurals, lists
        // ---------------------------------------------------------------- //

        /// <summary>
        /// Format an ICU MessageFormat pattern with named placeholders. A faithful
        /// subset (argument substitution + <c>plural</c>/<c>selectordinal</c>/<c>select</c>
        /// with <c>#</c>), backed by ICU plural rules and number formatting — the C
        /// <c>umsg</c> API supports neither named arguments nor a non-varargs entry.
        /// </summary>
        public string Message(string pattern, IDictionary<string, object?> args)
            => MessageFormatter.Format(this, pattern, k => args.TryGetValue(k, out var v) ? v : null);

        /// <summary>Format an ICU MessageFormat pattern with positional (<c>{0}</c>) placeholders.</summary>
        public string Message(string pattern, params object?[] args)
            => MessageFormatter.Format(this, pattern, k => int.TryParse(k, out var i) && i >= 0 && i < args.Length ? args[i] : null);

        /// <summary>The LDML cardinal plural category for a value (<c>1</c> → <c>"one"</c>).</summary>
        public string PluralCategory(double value, bool ordinal = false) => Icu.PluralCategory(Locale, value, ordinal);

        /// <summary>Join a list the locale's way (<c>"A, B, and C"</c>).</summary>
        /// <param name="type">conjunction (and) / disjunction (or) / unit.</param>
        public string Join(IEnumerable<string> items, string type = "conjunction", string width = "full")
        {
            int t = type switch
            {
                "conjunction" => Icu.ULISTFMT_AND,
                "disjunction" => Icu.ULISTFMT_OR,
                "unit" => Icu.ULISTFMT_UNITS,
                _ => throw new CosmoArgumentException($"\"{type}\" is not a valid list type."),
            };
            return Icu.JoinList(Locale, t, ListWidth(width), items.ToList());
        }

        /// <summary>Wrap text in the locale's quotation marks (<c>“x”</c> in en, <c>«x»</c> in fa).</summary>
        public string Quote(string text)
        {
            var start = Icu.Delimiter(Locale, "quotationStart") ?? "\"";
            var end = Icu.Delimiter(Locale, "quotationEnd") ?? "\"";
            return start + text + end;
        }

        // ---------------------------------------------------------------- //
        // relative durations & ranges
        // ---------------------------------------------------------------- //

        /// <summary>Render a directed duration (<c>(-3, "day")</c> → <c>"3 days ago"</c>).</summary>
        /// <param name="numeric">always (<c>"1 day ago"</c>) or auto (<c>"yesterday"</c>).</param>
        public string RelativeDuration(double amount, string unit, string numeric = "always")
        {
            int rel = RelativeUnit(unit);
            if (numeric == "auto") return Icu.RelativeDuration(Locale, amount, rel, true);
            if (numeric != "always") throw new CosmoArgumentException($"\"{numeric}\" is not a valid numeric mode (use always/auto).");
            return Icu.RelativeDuration(Locale, amount, rel, false);
        }

        /// <summary>Directed duration between two moments (<c>"in 5 days"</c>, <c>"3 days ago"</c>).</summary>
        public string RelativeDurationBetween(DateTimeOffset target, DateTimeOffset? reference = null, string numeric = "auto")
        {
            double refSec = (reference ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds() / 1000.0;
            double amount = target.ToUnixTimeMilliseconds() / 1000.0 - refSec;
            double[] sizes = { 60, 60, 24, 7, 4.34524, 12 };
            string[] units = { "second", "minute", "hour", "day", "week", "month" };
            for (int i = 0; i < sizes.Length; i++)
            {
                if (Math.Abs(amount) < sizes[i]) return RelativeDuration(Math.Round(amount), units[i], numeric);
                amount /= sizes[i];
            }
            return RelativeDuration(Math.Round(amount), "year", numeric);
        }

        /// <summary>Format a numeric range (<c>"3–5"</c>).</summary>
        public string NumberRange(double start, double end) => Icu.FormatRangeSkeleton(Locale, "", start, end);

        /// <summary>Format a monetary range (<c>"$3.00 – $5.00"</c>); <c>""</c> if no currency.</summary>
        public string MoneyRange(double start, double end, string? code = null)
        {
            var ccy = (code ?? Modifiers.Currency ?? "").ToUpperInvariant();
            if (ccy.Length == 0) return "";
            if (!IsCurrencyCode(ccy)) throw new CosmoArgumentException($"\"{ccy}\" is not a valid currency code.");
            return Icu.FormatRangeSkeleton(Locale, "currency/" + ccy, start, end);
        }

        // ---------------------------------------------------------------- //
        // locale metadata
        // ---------------------------------------------------------------- //

        /// <summary>A new Cosmo with likely subtags added (<c>"en"</c> → <c>"en_Latn_US"</c>).</summary>
        public Cosmo AddLikelySubtags() => new Cosmo(Icu.AddLikely(Locale), Modifiers);

        /// <summary>A new Cosmo with likely subtags removed (<c>"en_Latn_US"</c> → <c>"en"</c>).</summary>
        public Cosmo RemoveLikelySubtags() => new Cosmo(Icu.MinimizeLikely(Locale), Modifiers);

        /// <summary>Localised month names, following the active calendar (Persian for fa_IR, etc.).</summary>
        public List<string> MonthNames(string width = "full")
        {
            int type = width switch
            {
                "none" or "long" or "full" => Icu.UDAT_MONTHS,
                "medium" => Icu.UDAT_SHORT_MONTHS,
                "short" => Icu.UDAT_NARROW_MONTHS,
                _ => throw InvalidWidth(width),
            };
            return Icu.DateSymbols(CalendarLocale(null), type);
        }

        /// <summary>Localised weekday names, <strong>Sunday first</strong> (ICU symbol order).</summary>
        public List<string> WeekdayNames(string width = "full")
        {
            int type = width switch
            {
                "none" or "long" or "full" => Icu.UDAT_WEEKDAYS,
                "medium" => Icu.UDAT_SHORT_WEEKDAYS,
                "short" => Icu.UDAT_NARROW_WEEKDAYS,
                _ => throw InvalidWidth(width),
            };
            return Icu.DateSymbols(Locale, type);
        }

        /// <summary>Week conventions of the locale's region: first day, minimal days, weekend days.</summary>
        public WeekInfo WeekInfo()
        {
            var (first, minimal, weekend) = Icu.WeekData(Locale);
            return new WeekInfo(IsoDay(first), minimal, weekend.Select(IsoDay).OrderBy(d => d).ToList());
        }

        /// <summary>Display name of the <c>timeZone</c> modifier (or the system zone).</summary>
        /// <param name="style">long / short / longOffset / shortOffset / longGeneric / shortGeneric.</param>
        public string TimeZoneName(string style = "long")
        {
            string pat = style switch
            {
                "long" => "zzzz",
                "short" => "z",
                "longOffset" => "OOOO",
                "shortOffset" => "O",
                "longGeneric" => "vvvv",
                "shortGeneric" => "v",
                _ => throw new CosmoArgumentException($"\"{style}\" is not a valid time-zone name style."),
            };
            var tz = Modifiers.TimeZone ?? TimeZoneInfo.Local.Id;
            return Icu.FormatPattern(Locale, tz, pat, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        /// <summary>Generic localised display name — one entry point over the dedicated lookups.</summary>
        public string DisplayName(string type, string code) => type switch
        {
            "language" => Language(code),
            "region" => Country(code),
            "script" => Script(code),
            "calendar" => Calendar(code),
            "currency" => Currency(code),
            _ => throw new CosmoArgumentException($"\"{type}\" is not a display-name type (use language/region/script/calendar/currency)."),
        };

        /// <summary>Values the runtime's ICU supports for <paramref name="key"/> (e.g. all IANA time zones).</summary>
        public List<string> SupportedValues(string key) => key switch
        {
            "timeZone" => Icu.TimeZones(),
            "collation" => Icu.Collations(),
            "numberingSystem" => Icu.NumberingSystems(),
            "currency" => Icu.Currencies(),
            "calendar" => Icu.Calendars(),
            "transliterator" => Icu.Transliterators(),
            "unit" => throw new CosmoUnsupportedException("ICU's C API exposes no enumeration of measurement units."),
            _ => throw new CosmoArgumentException($"\"{key}\" is not a valid key (use timeZone/collation/numberingSystem/currency/calendar/transliterator)."),
        };

        // ---------------------------------------------------------------- //
        // transliteration & spoof detection
        // ---------------------------------------------------------------- //

        /// <summary>Run an ICU transform over the text (<c>"Any-Latin; Latin-ASCII"</c> makes ASCII slugs).</summary>
        public string Transliterate(string text, string id) => Icu.Transliterate(id, text);

        /// <summary>Romanise text (<c>"Москва"</c> → <c>"Moskva"</c>); shorthand for <c>Any-Latin</c>.</summary>
        public string Romanize(string text) => Transliterate(text, "Any-Latin");

        /// <summary>Whether two strings are visually confusable per UTS #39. Locale-independent.</summary>
        public bool Confusable(string a, string b) => Icu.Confusable(a, b);

        /// <summary>Whether a string fails ICU's default spoof checks per UTS #39.</summary>
        public bool Suspicious(string text) => Icu.Suspicious(text);

        // ---------------------------------------------------------------- //
        // locale-aware parsing
        // ---------------------------------------------------------------- //

        /// <summary>Parse a localised number (<c>"1.234,56"</c> in de → <c>1234.56</c>).</summary>
        public double ParseNumber(string text) => Icu.ParseNumber(Locale, text);

        /// <summary>Parse a localised monetary string (<c>"$12.30"</c> → 12.3 USD).</summary>
        public (double Amount, string Currency) ParseMoney(string text) => Icu.ParseMoney(Locale, text);

        /// <summary>Parse a localised date written at the given width.</summary>
        public DateTimeOffset ParseDate(string text, string width = "short")
        {
            int style = DateStyle(width);
            if (style == Icu.UDAT_NONE) throw InvalidWidth(width);
            return DateTimeOffset.FromUnixTimeMilliseconds((long)Icu.ParseDate(style, Icu.UDAT_NONE, CalendarLocale(null), Modifiers.TimeZone, null, text));
        }

        /// <summary>Parse a moment with a raw ICU pattern (the inverse of <see cref="FormatMoment"/>).</summary>
        public DateTimeOffset ParseMoment(string text, string pattern)
            => DateTimeOffset.FromUnixTimeMilliseconds((long)Icu.ParseDate(Icu.UDAT_PATTERN, Icu.UDAT_PATTERN, CalendarLocale(null), Modifiers.TimeZone, pattern, text));

        // ---------------------------------------------------------------- //
        // C-API casualties (no ICU C binding)
        // ---------------------------------------------------------------- //

        /// <summary><strong>Unsupported on the C API</strong> — AlphabeticIndex is C++-only.</summary>
        public IDictionary<string, List<string>> IndexBuckets(IEnumerable<string> names)
            => throw new CosmoUnsupportedException("AlphabeticIndex has no ICU C binding.");

        /// <summary><strong>Unsupported on the C API</strong> — needs CLDR-distance LocaleMatcher.</summary>
        public string BestMatch(IEnumerable<string> supported)
            => throw new CosmoUnsupportedException("CLDR-distance locale matching needs ICU's LocaleMatcher, which has no C binding.");

        /// <summary><strong>Unsupported on this ICU build</strong> — PersonNameFormatter is ICU 73+ (this ships ICU 72).</summary>
        public string PersonName(IDictionary<string, string> fields, string length = "medium", string formality = "formal")
            => throw new CosmoUnsupportedException("PersonNameFormatter requires ICU 73+; the bundled ICU4C is version 72.");

        // ---------------------------------------------------------------- //
        // case transforms
        // ---------------------------------------------------------------- //

        /// <summary>Locale-aware upper-casing (e.g. Turkish dotted/dotless I).</summary>
        public string Upper(string text) => Icu.Upper(Locale, text);

        /// <summary>Locale-aware lower-casing.</summary>
        public string Lower(string text) => Icu.Lower(Locale, text);

        // ---------------------------------------------------------------- //
        // helpers
        // ---------------------------------------------------------------- //

        private string CalendarLocale(string? calendar)
        {
            var cal = calendar ?? Modifiers.Calendar;
            return cal == null ? Locale : Icu.SetKeyword(Locale, "calendar", cal);
        }

        private List<string> Graphemes(string text)
        {
            var outp = new List<string>();
            foreach (var (start, end, _) in Icu.Boundaries(Icu.UBRK_CHARACTER, Locale, text))
                outp.Add(text.Substring(start, end - start));
            return outp;
        }

        private static void ApplyCollation(Icu.Collator c, CollationOptions? o)
        {
            if (o == null) return;
            if (o.Numeric.HasValue) c.SetNumeric(o.Numeric.Value);
            if (o.CaseFirst != null)
            {
                if (o.CaseFirst is not ("upper" or "lower" or "false"))
                    throw new CosmoArgumentException($"\"{o.CaseFirst}\" is not a valid caseFirst value.");
                c.SetCaseFirst(o.CaseFirst);
            }
        }

        // --- number skeleton building ---------------------------------- //

        private static string BuildSkeleton(string baseSkel, NumberOptions? o, string? precisionOverride)
        {
            var parts = new List<string>();
            if (baseSkel.Length != 0) parts.Add(baseSkel);

            string? precision = precisionOverride;
            bool hasSig = o?.MinimumSignificantDigits != null || o?.MaximumSignificantDigits != null;
            if (hasSig)
            {
                int min = o!.MinimumSignificantDigits ?? 1;
                int max = o.MaximumSignificantDigits ?? Math.Max(min, 1);
                precision = new string('@', Math.Max(1, min)) + new string('#', Math.Max(0, max - Math.Max(1, min)));
            }
            else if (o?.RoundingIncrement != null)
            {
                int frac = o.MaximumFractionDigits ?? 0;
                double step = o.RoundingIncrement.Value * Math.Pow(10, -frac);
                precision = "precision-increment/" + step.ToString("0.##########", CultureInfo.InvariantCulture);
            }
            else if (o?.MinimumFractionDigits != null || o?.MaximumFractionDigits != null)
            {
                precision = Fraction(o.MinimumFractionDigits ?? 0, o.MaximumFractionDigits ?? Math.Max(o.MinimumFractionDigits ?? 0, 3));
            }
            if (precision != null) parts.Add(precision);

            // The leading '+' means "minimum, no maximum"; without it ICU caps the
            // integer digits too (truncating e.g. 12345 to 2 digits).
            if (o?.MinimumIntegerDigits != null) parts.Add("integer-width/+" + new string('0', Math.Max(1, o.MinimumIntegerDigits.Value)));
            if (o?.UseGrouping == false) parts.Add("group-off");

            parts.Add("rounding-mode-" + RoundingMode(o?.RoundingMode));
            return string.Join(" ", parts);
        }

        private static string Fraction(int min, int max)
            => max <= 0 ? "precision-integer" : "." + new string('0', Math.Max(0, min)) + new string('#', Math.Max(0, max - min));

        private static string RoundingMode(string? name) => name switch
        {
            null => "half-up",
            "ceil" => "ceiling",
            "floor" => "floor",
            "expand" => "up",
            "trunc" => "down",
            "halfExpand" => "half-up",
            "halfTrunc" => "half-down",
            "halfEven" => "half-even",
            _ => throw new CosmoArgumentException($"\"{name}\" is not a valid rounding mode (use ceil/floor/expand/trunc/halfExpand/halfTrunc/halfEven)."),
        };

        // --- date/width mapping ---------------------------------------- //

        private static int DateStyle(string? width) => width switch
        {
            "none" => Icu.UDAT_NONE,
            "short" => Icu.UDAT_SHORT,
            "medium" => Icu.UDAT_MEDIUM,
            "long" => Icu.UDAT_LONG,
            "full" => Icu.UDAT_FULL,
            _ => throw InvalidWidth(width),
        };

        private static int ListWidth(string? width) => width switch
        {
            "none" or "long" or "full" => Icu.ULISTFMT_WIDE,
            "medium" => Icu.ULISTFMT_SHORT,
            "short" => Icu.ULISTFMT_NARROW,
            _ => throw InvalidWidth(width),
        };

        // URelativeDateTimeUnit orders YEAR=0 … SECOND=7.
        private static int RelativeUnit(string unit) => unit switch
        {
            "year" => 0, "quarter" => 1, "month" => 2, "week" => 3,
            "day" => 4, "hour" => 5, "minute" => 6, "second" => 7,
            _ => throw new CosmoArgumentException($"\"{unit}\" is not a valid relative unit."),
        };

        private static CosmoArgumentException InvalidWidth(string? width)
            => new CosmoArgumentException($"\"{width}\" is not a valid format width (use none/short/medium/long/full).");

        /// <summary>ICU day (1=Sunday..7=Saturday) → ISO day (1=Monday..7=Sunday).</summary>
        private static int IsoDay(int icuDay) => ((icuDay + 5) % 7) + 1;

        private static bool IsCurrencyCode(string s) => s.Length == 3 && s.All(c => c >= 'A' && c <= 'Z');

        private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
    }
}
