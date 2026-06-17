using System;
using System.Collections.Generic;
using System.Linq;
using Miloun.Cosmo;
using Xunit;

namespace Miloun.Cosmo.Tests
{
    /// <summary>
    /// Mirrors the Java port's expectations. Where ICU wording is volatile across
    /// versions we assert structure rather than a brittle literal. Methods the ICU
    /// C API cannot reach are asserted to throw <see cref="CosmoUnsupportedException"/>.
    /// </summary>
    public class CosmoTest
    {
        // 2020-02-02T00:13:20Z
        private static readonly DateTimeOffset TS = DateTimeOffset.FromUnixTimeMilliseconds(1_580_602_400_000L);
        private static readonly Modifiers UTC = new Modifiers(timeZone: "UTC");

        private static string NormSpace(string s) => s.Replace(' ', ' ').Replace(' ', ' ');

        // -- construction & subtags ------------------------------------- //

        [Fact]
        public void CanonicalisesLocaleIds()
        {
            Assert.Equal("en_AU", new Cosmo("en-AU").Locale);
            Assert.Equal("en_AU", new Cosmo("en_AU").Locale);
            var fa = new Cosmo("fa-IR");
            Assert.Equal("fa", fa.Subtags.Language);
            Assert.Equal("IR", fa.Subtags.Region);
        }

        [Fact]
        public void InfersCurrencyFromRegion()
        {
            Assert.Equal("AUD", new Cosmo("en_AU").Modifiers.Currency);
            Assert.Equal("USD", new Cosmo("en_US").Modifiers.Currency);
            Assert.Null(new Cosmo("en").Modifiers.Currency);
        }

        [Fact]
        public void ModifierOverrideBeatsInference()
            => Assert.Equal("EUR", new Cosmo("en_AU", new Modifiers(currency: "EUR")).Modifiers.Currency);

        // -- key → value lookups ---------------------------------------- //

        [Fact]
        public void Language()
        {
            Assert.Equal("English", new Cosmo("en").Language("en"));
            Assert.Equal("انگلیسی", new Cosmo("fa").Language("en"));
            Assert.Equal("", new Cosmo("en").Language(""));
        }

        [Fact]
        public void CountryAndScript()
        {
            Assert.Equal("Australia", new Cosmo("en").Country("AU"));
            Assert.Equal("", new Cosmo("en").Country(""));
            Assert.Equal("Latin", new Cosmo("en").Script("Latn"));
            Assert.Contains("Simplified", new Cosmo("en").Script("Hans"));
        }

        [Fact]
        public void CalendarAndDirection()
        {
            Assert.Equal("Buddhist Calendar", new Cosmo("en").Calendar("buddhist"));
            Assert.Equal("ltr", new Cosmo("en").Direction());
            Assert.Equal("rtl", new Cosmo("fa").Direction());
            Assert.Equal("rtl", new Cosmo("en").Direction("ar"));
        }

        [Fact]
        public void Flag()
        {
            Assert.Equal("🇦🇺", new Cosmo("en_AU").Flag());
            Assert.Equal("🇺🇸", new Cosmo("en").Flag("US"));
            Assert.Equal("", new Cosmo("en").Flag("X"));
        }

        [Fact]
        public void CurrencyNameAndSymbol()
        {
            var c = new Cosmo("en_US");
            Assert.Equal("Australian Dollar", c.Currency("AUD"));
            Assert.Equal("A$", c.Currency("AUD", true));
            Assert.Equal("ZZZ", c.Currency("ZZZ"));
            Assert.Throws<CosmoArgumentException>(() => c.Currency("ZZZ", false, true));
        }

        // -- numbers ---------------------------------------------------- //

        [Fact]
        public void NumberAndPercentage()
        {
            Assert.Equal("1,234,567.89", new Cosmo("en").Number(1234567.89));
            Assert.Equal("20%", new Cosmo("en").Percentage(0.2));
            Assert.Equal("12.35%", new Cosmo("en").Percentage(0.12345, 2));
        }

        [Fact]
        public void Precision()
        {
            Assert.Equal("1.00", new Cosmo("en").Precision(1)); // fractionDigits defaults to 2
            Assert.Equal("1.00", new Cosmo("en").Precision(1, 2));
            Assert.Equal("1.00", new Cosmo("en").Precision(1.002, 2));
            Assert.Equal("1.50", new Cosmo("en").Precision(1.5, 2));
            Assert.Equal("1,234.50", new Cosmo("en").Precision(1234.5, 2));
            Assert.Equal("1,50", new Cosmo("de").Precision(1.5, 2));
            // options widen the band: at least 1, at most 3 fraction digits.
            Assert.Equal("1.2", new Cosmo("en").Precision(1.2, 1, new NumberOptions { MaximumFractionDigits = 3 }));
        }

        [Fact]
        public void Money()
        {
            Assert.Equal("$1,234.50", new Cosmo("en_AU").Money(1234.5));
            Assert.Equal("€1,234.50", new Cosmo("en_US").Money(1234.5, "EUR"));
            Assert.Equal("$1,235", new Cosmo("en_US").Money(1234.9, "USD", 0));
            Assert.Equal("", new Cosmo("en").Money(100));
        }

        [Fact]
        public void MoneyErrors()
        {
            Assert.Throws<CosmoArgumentException>(() => new Cosmo("en").Money(100, null, null, true));
            Assert.Throws<CosmoArgumentException>(() => new Cosmo("en_US").Money(100, "EURO"));
        }

        [Fact]
        public void ScientificAndCompact()
        {
            Assert.Equal("1.2345E4", new Cosmo("en").Scientific(12345));
            Assert.Equal("1.2K", new Cosmo("en").Compact(1200));
            Assert.Equal("1.2 million", new Cosmo("en").Compact(1200000, "long"));
        }

        [Fact]
        public void OrdinalAndSpellout()
        {
            var c = new Cosmo("en");
            Assert.Equal("1st", c.Ordinal(1));
            Assert.Equal("2nd", c.Ordinal(2));
            Assert.Equal("forty-two", c.Spellout(42));
        }

        // -- dates & times ---------------------------------------------- //

        [Fact]
        public void MomentDateTime()
        {
            var c = new Cosmo("en_US", UTC);
            Assert.Equal("February 2, 2020", c.Date(TS, "long"));
            Assert.Equal("12:13 AM", NormSpace(c.Time(TS, "short")));
            Assert.Equal("", c.Moment(TS, "none", "none"));
            Assert.Throws<CosmoArgumentException>(() => c.Date(TS, "bogus"));
        }

        [Fact]
        public void PersianCalendarIsImplicit()
            => Assert.Contains("۱۳۹۸", new Cosmo("fa_IR", UTC).Date(TS, "long"));

        // -- collation -------------------------------------------------- //

        [Fact]
        public void CompareAndSort()
        {
            var c = new Cosmo("en");
            Assert.True(c.Compare("a", "b") < 0);
            Assert.True(c.Compare("b", "a") > 0);
            Assert.Equal(new[] { "apple", "banana", "cherry" }, c.Sort(new[] { "banana", "apple", "cherry" }));
        }

        // -- messages, plurals, lists ----------------------------------- //

        [Fact]
        public void MessagePositionalAndNamed()
        {
            var c = new Cosmo("en");
            Assert.Equal("Bob has 3 items", c.Message("{0} has {1, plural, one {# item} other {# items}}", "Bob", 3));
            Assert.Equal("1 item", c.Message("{0, plural, one {# item} other {# items}}", 1));
            var args = new Dictionary<string, object?> { ["name"] = "Sue", ["n"] = 2 };
            Assert.Equal("Sue likes 2 cats", c.Message("{name} likes {n, plural, one {# cat} other {# cats}}", args));
        }

        [Fact]
        public void PluralCategory()
        {
            var c = new Cosmo("en");
            Assert.Equal("one", c.PluralCategory(1));
            Assert.Equal("other", c.PluralCategory(2));
            Assert.Equal("one", c.PluralCategory(1, true));
            Assert.Equal("two", c.PluralCategory(2, true));
            Assert.Equal("few", c.PluralCategory(3, true));
        }

        [Fact]
        public void Join()
        {
            var c = new Cosmo("en");
            Assert.Equal("A, B, and C", c.Join(new[] { "A", "B", "C" }));
            Assert.Equal("A, B, or C", c.Join(new[] { "A", "B", "C" }, "disjunction"));
            Assert.Throws<CosmoArgumentException>(() => c.Join(new[] { "A" }, "bogus"));
        }

        // -- relative durations & ranges -------------------------------- //

        [Fact]
        public void RelativeDuration()
        {
            var c = new Cosmo("en");
            Assert.Equal("3 days ago", c.RelativeDuration(-3, "day"));
            Assert.Equal("in 2 hours", c.RelativeDuration(2, "hour"));
            Assert.Equal("yesterday", c.RelativeDuration(-1, "day", "auto"));
            Assert.Throws<CosmoArgumentException>(() => c.RelativeDuration(1, "fortnight"));
        }

        [Fact]
        public void RelativeDurationBetween()
        {
            var c = new Cosmo("en");
            var b = DateTimeOffset.Parse("2020-01-01T12:00:00Z");
            Assert.Equal("in 5 days", c.RelativeDurationBetween(DateTimeOffset.Parse("2020-01-06T12:00:00Z"), b));
            Assert.Equal("3 days ago", c.RelativeDurationBetween(DateTimeOffset.Parse("2019-12-29T12:00:00Z"), b));
            Assert.Equal("yesterday", c.RelativeDurationBetween(DateTimeOffset.Parse("2019-12-31T12:00:00Z"), b));
        }

        [Fact]
        public void NumberAndMoneyRanges()
        {
            var c = new Cosmo("en_US");
            Assert.Equal("3–5", c.NumberRange(3, 5));
            Assert.Equal("$3.00 – $5.00", NormSpace(c.MoneyRange(3, 5, "USD")));
            Assert.Equal("", new Cosmo("en").MoneyRange(3, 5));
        }

        // -- locale metadata -------------------------------------------- //

        [Fact]
        public void LikelySubtags()
        {
            Assert.Equal("en_Latn_US", new Cosmo("en").AddLikelySubtags().Locale);
            Assert.Equal("en", new Cosmo("en_Latn_US").RemoveLikelySubtags().Locale);
        }

        [Fact]
        public void MonthAndWeekdayNames()
        {
            var en = new Cosmo("en");
            Assert.Equal("January", en.MonthNames()[0]);
            Assert.Equal(12, en.MonthNames().Count);
            Assert.Equal("Sunday", en.WeekdayNames()[0]);
            Assert.Equal(7, en.WeekdayNames().Count);
            Assert.Contains("فروردین", new Cosmo("fa_IR").MonthNames());
        }

        [Fact]
        public void WeekInfoTest()
        {
            var gb = new Cosmo("en_GB").WeekInfo();
            Assert.Equal(1, gb.FirstDay);
            Assert.Equal(new[] { 6, 7 }, gb.Weekend);
            Assert.Equal(7, new Cosmo("en_US").WeekInfo().FirstDay);
        }

        [Fact]
        public void FromSubtagsAndAcceptLanguage()
        {
            Assert.Equal("en_AU", Cosmo.FromSubtags(new Subtags("en", "", "AU")).Locale);
            Assert.Equal("fr_CH", Cosmo.FromAcceptLanguage("fr-CH, en;q=0.9, de;q=0.7").Locale);
            Assert.Equal("de", Cosmo.FromAcceptLanguage("en;q=0.2, de;q=0.8").Subtags.Language);
        }

        [Fact]
        public void HonoursNumberingSystemExtension()
            => Assert.Equal("๑,๒๓๔,๕๖๗.๘๙", new Cosmo("th-TH-u-nu-thai").Number(1234567.89));

        // -- number & collation options --------------------------------- //

        [Fact]
        public void NumberOptionsTest()
        {
            var c = new Cosmo("en");
            Assert.Equal("2", c.Number(2.9, new NumberOptions { RoundingMode = "floor", MaximumFractionDigits = 0 }));
            Assert.Equal("3", c.Number(2.1, new NumberOptions { RoundingMode = "ceil", MaximumFractionDigits = 0 }));
            Assert.Equal("1.25", c.Number(1.23, new NumberOptions { RoundingIncrement = 5, MinimumFractionDigits = 2, MaximumFractionDigits = 2 }));
            Assert.Equal("12345", c.Number(12345, new NumberOptions { UseGrouping = false }));
            Assert.Equal("120,000", c.Number(123456.789, new NumberOptions { MaximumSignificantDigits = 2 }));
            Assert.Equal("$10.00", c.Money(9.991, "USD", null, false, new NumberOptions { RoundingMode = "ceil" }));
            Assert.Equal("12.34%", c.Percentage(0.12349, 2, new NumberOptions { RoundingMode = "floor" }));
            Assert.Throws<CosmoArgumentException>(() => c.Number(1, new NumberOptions { RoundingMode = "bogus" }));
        }

        [Fact]
        public void CollationOptionsTest()
        {
            var c = new Cosmo("en");
            Assert.True(c.Compare("item2", "item10", new CollationOptions { Numeric = true }) < 0);
            Assert.Equal(new[] { "item1", "item2", "item10" },
                c.Sort(new[] { "item10", "item2", "item1" }, new CollationOptions { Numeric = true }));
            Assert.Equal(new[] { "A", "a", "B", "b" },
                c.Sort(new[] { "b", "B", "a", "A" }, new CollationOptions { CaseFirst = "upper" }));
        }

        // -- symbols, units, durations ---------------------------------- //

        [Fact]
        public void Symbol()
        {
            var c = new Cosmo("en");
            Assert.Equal(".", c.Symbol("decimal"));
            Assert.Equal(",", c.Symbol("grouping_separator"));
            Assert.Equal("%", c.Symbol("percent"));
            Assert.Equal(",", new Cosmo("de").Symbol("decimal"));
            Assert.Throws<CosmoArgumentException>(() => c.Symbol("bogus"));
        }

        [Fact]
        public void Unit()
        {
            Assert.Equal("2.19 gigabytes", new Cosmo("en").Unit("digital", "gigabyte", 2.19));
            Assert.Contains("GB", new Cosmo("en").Unit("digital", "gigabyte", 2.19, "short"));
            Assert.Throws<CosmoArgumentException>(() => new Cosmo("en").Unit("x", "not-a-unit", 1));
        }

        [Fact]
        public void Duration()
        {
            var c = new Cosmo("en");
            Assert.Equal("339:17:20", c.Duration(1221440));
            Assert.Contains("hours", c.Duration(1221440, true));
            Assert.Equal("3 hours, 5 minutes",
                c.Duration(new Dictionary<string, double> { ["hours"] = 3, ["minutes"] = 5 }, true));
            Assert.Contains("2 days", c.Duration(new Dictionary<string, double> { ["days"] = 2, ["hours"] = 3 }));
            Assert.Equal("", c.Duration(new Dictionary<string, double>()));
        }

        // -- contains & segmentation ------------------------------------ //

        [Fact]
        public void Contains()
        {
            var c = new Cosmo("en");
            Assert.True(c.Contains("Résumé", "resume"));
            Assert.True(c.Contains("hello world", "WORLD"));
            Assert.False(c.Contains("hello", "xyz"));
            Assert.True(c.Contains("anything", ""));
            Assert.False(c.Contains("Résumé", "resume", "variant"));
        }

        [Fact]
        public void Segmentation()
        {
            var c = new Cosmo("en");
            Assert.Equal(new[] { "Hello", "world", "ICU", "rocks" }, c.SplitWords("Hello, world! ICU rocks."));
            Assert.Equal(new[] { "Hi there.", "How are you?" }, c.SplitSentences("Hi there. How are you?"));
            Assert.Equal(new[] { "a", "👩‍👧", "b" }, c.SplitGraphemes("a👩‍👧b"));
            Assert.Empty(c.SplitGraphemes(""));
        }

        [Fact]
        public void Ellipsize()
        {
            var c = new Cosmo("en");
            Assert.Equal("short", c.Ellipsize("short", 20));
            var outp = c.Ellipsize("The quick brown fox jumps", 15);
            Assert.EndsWith("…", outp);
            Assert.True(c.SplitGraphemes(outp).Count <= 15);
        }

        // -- quotes, case, time zones, display names -------------------- //

        [Fact]
        public void Quote()
        {
            Assert.Equal("“hi”", new Cosmo("en").Quote("hi"));
            Assert.Equal("«x»", new Cosmo("fa").Quote("x"));
        }

        [Fact]
        public void CaseTransforms()
        {
            Assert.Equal("ISTANBUL", new Cosmo("en").Upper("istanbul"));
            Assert.Equal("İSTANBUL", new Cosmo("tr").Upper("istanbul"));
            Assert.Equal("hello", new Cosmo("en").Lower("HELLO"));
        }

        [Fact]
        public void TimeZoneNameTest()
        {
            var c = new Cosmo("en", new Modifiers(timeZone: "Australia/Sydney"));
            Assert.Contains("Australian Eastern", c.TimeZoneName("long"));
            Assert.Contains(c.TimeZoneName("short"), new[] { "AEST", "AEDT", "GMT+10", "GMT+11" });
            Assert.Throws<CosmoArgumentException>(() => c.TimeZoneName("bogus"));
        }

        [Fact]
        public void DisplayName()
        {
            var c = new Cosmo("en");
            Assert.Equal("French", c.DisplayName("language", "fr"));
            Assert.Equal("Japan", c.DisplayName("region", "JP"));
            Assert.Contains("Simplified", c.DisplayName("script", "Hans"));
            Assert.Equal("Buddhist Calendar", c.DisplayName("calendar", "buddhist"));
            Assert.Equal("Euro", c.DisplayName("currency", "EUR"));
            Assert.Throws<CosmoArgumentException>(() => c.DisplayName("nope", "x"));
        }

        [Fact]
        public void SupportedValues()
        {
            var c = new Cosmo("en");
            Assert.Contains("Australia/Sydney", c.SupportedValues("timeZone"));
            Assert.Contains("standard", c.SupportedValues("collation"));
            Assert.Contains("latn", c.SupportedValues("numberingSystem"));
            Assert.Contains("EUR", c.SupportedValues("currency"));
            Assert.Contains("buddhist", c.SupportedValues("calendar"));
            Assert.Throws<CosmoArgumentException>(() => c.SupportedValues("bogus"));
            Assert.Throws<CosmoUnsupportedException>(() => c.SupportedValues("unit"));
        }

        // -- date patterns & ranges ------------------------------------- //

        [Fact]
        public void FormatMomentPattern()
            => Assert.Equal("2020-02-02", new Cosmo("en_US", UTC).FormatMoment(TS, "yyyy-MM-dd"));

        [Fact]
        public void DateRange()
        {
            var start = DateTimeOffset.Parse("2020-02-02T12:00:00Z");
            var end = DateTimeOffset.Parse("2020-02-05T12:00:00Z");
            var outp = new Cosmo("en_US", UTC).DateRange(start, end);
            Assert.True(outp.Contains('2') && outp.Contains('5') && outp.Contains("Feb"));
            Assert.Throws<CosmoArgumentException>(() => new Cosmo("en_US").DateRange(start, end, "full", "full"));
        }

        // -- transforms, spoofing, parsing ------------------------------ //

        [Fact]
        public void TransliterateAndRomanize()
        {
            var c = new Cosmo("en");
            Assert.Equal("Moskva", c.Romanize("Москва"));
            Assert.Equal("Lodz cafe", c.Transliterate("Łódź café", "Any-Latin; Latin-ASCII"));
            Assert.Throws<CosmoArgumentException>(() => c.Transliterate("x", "Nope-Nope"));
            Assert.Contains("Any-Latin", c.SupportedValues("transliterator"));
        }

        [Fact]
        public void SpoofChecks()
        {
            var c = new Cosmo("en");
            Assert.True(c.Confusable("paypal", "раураl"));
            Assert.False(c.Confusable("hello", "world"));
            Assert.True(c.Suspicious("pаypal"));
            Assert.False(c.Suspicious("paypal"));
        }

        [Fact]
        public void Parsing()
        {
            Assert.Equal(1234.56, new Cosmo("de").ParseNumber("1.234,56"));
            Assert.Equal(1234.56, new Cosmo("en").ParseNumber("1,234.56"));
            var money = new Cosmo("en_US").ParseMoney("$12.30");
            Assert.Equal(12.3, money.Amount, 3);
            Assert.Equal("USD", money.Currency);
            var utc = new Cosmo("en_US", UTC);
            Assert.Equal("February 2, 2020", utc.Date(utc.ParseDate("February 2, 2020", "long"), "long"));
            Assert.Equal(1_580_601_600_000L, utc.ParseMoment("2020-02-02", "yyyy-MM-dd").ToUnixTimeMilliseconds());
            Assert.Throws<CosmoArgumentException>(() => new Cosmo("en").ParseNumber("not a number"));
        }

        // -- C-API casualties (no ICU C binding) ------------------------ //

        [Fact]
        public void CApiCasualtiesThrow()
        {
            var c = new Cosmo("en");
            Assert.Throws<CosmoUnsupportedException>(() => c.IndexBuckets(new[] { "apple", "banana" }));
            Assert.Throws<CosmoUnsupportedException>(() => c.BestMatch(new[] { "en-US", "en-GB" }));
            Assert.Throws<CosmoUnsupportedException>(() => c.PersonName(new Dictionary<string, string> { ["given"] = "John" }));
            Assert.Throws<CosmoUnsupportedException>(() => Cosmo.FromAcceptLanguage("fr", new[] { "en-US", "fr-FR" }));
        }
    }
}
