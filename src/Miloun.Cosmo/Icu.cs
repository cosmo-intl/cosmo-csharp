using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Miloun.Cosmo
{
    /// <summary>
    /// Raw P/Invoke bindings to ICU4C (the native ICU C API, version 72, shipped
    /// by the Microsoft.ICU.ICU4C.Runtime package) plus the small marshalling
    /// helpers the rest of the library is built on. ICU symbols carry a "_72"
    /// version suffix; the native libraries are loaded through a DllImport
    /// resolver because they ship as version-stamped files (libicui18n.so.72.x).
    /// </summary>
    internal static unsafe class Icu
    {
        private const string Lib = "icu";
        private const int U_BUFFER_OVERFLOW_ERROR = 15;

        // --- native library loading ------------------------------------ //

        private static IntPtr _i18n, _uc, _data;
        private static readonly object Gate = new object();

        static Icu()
        {
            NativeLibrary.SetDllImportResolver(typeof(Icu).Assembly, Resolve);
        }

        private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
        {
            if (name != Lib) return IntPtr.Zero;
            lock (Gate)
            {
                if (_i18n != IntPtr.Zero) return _i18n;
                // Load data + common first so the i18n library's dependencies are
                // satisfied; on Linux symbol lookups on the i18n handle then chase
                // into common/data automatically.
                _data = LoadFirst(new[] { "libicudata.so*", "icudt*.dll", "libicudata*.dylib" });
                _uc = LoadFirst(new[] { "libicuuc.so*", "icuuc*.dll", "libicuuc*.dylib" });
                _i18n = LoadFirst(new[] { "libicui18n.so*", "icuin*.dll", "libicui18n*.dylib" });
                return _i18n;
            }
        }

        private static IntPtr LoadFirst(string[] patterns)
        {
            var dirs = new List<string> { AppContext.BaseDirectory };
            var rt = Path.Combine(AppContext.BaseDirectory, "runtimes");
            if (Directory.Exists(rt))
                foreach (var d in Directory.GetDirectories(rt, "*", SearchOption.AllDirectories)) dirs.Add(d);
            foreach (var dir in dirs)
                foreach (var pat in patterns)
                {
                    string[] hits;
                    try { hits = Directory.GetFiles(dir, pat); } catch { continue; }
                    Array.Sort(hits, StringComparer.Ordinal);
                    foreach (var h in hits)
                        if (NativeLibrary.TryLoad(h, out var handle)) return handle;
                }
            return IntPtr.Zero;
        }

        // --- error handling & marshalling ------------------------------ //

        [DllImport(Lib, EntryPoint = "u_errorName_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr u_errorName(int code);

        internal static void Check(int status)
        {
            if (status > 0)
            {
                var name = Marshal.PtrToStringAnsi(u_errorName(status)) ?? ("status " + status);
                throw new CosmoException("ICU error: " + name);
            }
        }

        /// <summary>NUL-terminated invariant (ASCII) bytes for a locale id / keyword.</summary>
        internal static byte[] Bytes(string? s)
        {
            s ??= "";
            var b = Encoding.UTF8.GetBytes(s);
            var outp = new byte[b.Length + 1];
            Array.Copy(b, outp, b.Length);
            return outp;
        }

        internal delegate int UFill(char* buf, int cap, int* status);

        /// <summary>The classic ICU "fill a UChar buffer, grow on overflow" pattern.</summary>
        internal static string Fill(UFill f)
        {
            int cap = 64;
            while (true)
            {
                var buf = new char[cap];
                int status = 0, need;
                fixed (char* b = buf) { need = f(b, cap, &status); }
                if (status == U_BUFFER_OVERFLOW_ERROR || need > cap)
                {
                    cap = (need > cap ? need : cap * 2) + 1;
                    continue;
                }
                Check(status);
                return new string(buf, 0, need < 0 ? 0 : (need > cap ? cap : need));
            }
        }

        internal delegate int BFill(byte* buf, int cap, int* status);

        /// <summary>The buffer-grow pattern for ICU functions that emit invariant
        /// (ASCII) <c>char*</c> output — locale ids and keyword values, not UChar.</summary>
        internal static string FillBytes(BFill f)
        {
            int cap = 64;
            while (true)
            {
                var buf = new byte[cap];
                int status = 0, need;
                fixed (byte* b = buf) { need = f(b, cap, &status); }
                if (status == U_BUFFER_OVERFLOW_ERROR || need > cap)
                {
                    cap = (need > cap ? need : cap * 2) + 1;
                    continue;
                }
                Check(status);
                return Encoding.UTF8.GetString(buf, 0, need < 0 ? 0 : (need > cap ? cap : need));
            }
        }

        // --- uloc (locale identity & display) -------------------------- //

        [DllImport(Lib, EntryPoint = "uloc_getDefault_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr uloc_getDefault();
        internal static string GetDefaultLocale() => Marshal.PtrToStringAnsi(uloc_getDefault()) ?? "en_US_POSIX";

        [DllImport(Lib, EntryPoint = "uloc_canonicalize_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int uloc_canonicalize(byte* localeID, byte* name, int cap, int* status);

        [DllImport(Lib, EntryPoint = "uloc_forLanguageTag_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int uloc_forLanguageTag(byte* langtag, byte* localeID, int cap, int* parsedLength, int* status);

        [DllImport(Lib, EntryPoint = "uloc_getLanguage_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int uloc_getLanguage(byte* localeID, byte* lang, int cap, int* status);
        [DllImport(Lib, EntryPoint = "uloc_getScript_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int uloc_getScript(byte* localeID, byte* script, int cap, int* status);
        [DllImport(Lib, EntryPoint = "uloc_getCountry_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int uloc_getCountry(byte* localeID, byte* country, int cap, int* status);

        [DllImport(Lib, EntryPoint = "uloc_getDisplayLanguage_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int uloc_getDisplayLanguage(byte* locale, byte* displayLocale, char* result, int cap, int* status);
        [DllImport(Lib, EntryPoint = "uloc_getDisplayCountry_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int uloc_getDisplayCountry(byte* locale, byte* displayLocale, char* result, int cap, int* status);
        [DllImport(Lib, EntryPoint = "uloc_getDisplayScript_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int uloc_getDisplayScript(byte* locale, byte* displayLocale, char* result, int cap, int* status);
        [DllImport(Lib, EntryPoint = "uloc_getDisplayKeywordValue_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int uloc_getDisplayKeywordValue(byte* locale, byte* keyword, byte* displayLocale, char* result, int cap, int* status);

        [DllImport(Lib, EntryPoint = "uloc_setKeywordValue_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int uloc_setKeywordValue(byte* keyword, byte* value, byte* buffer, int cap, int* status);
        [DllImport(Lib, EntryPoint = "uloc_getKeywordValue_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int uloc_getKeywordValue(byte* localeID, byte* keyword, byte* buffer, int cap, int* status);

        [DllImport(Lib, EntryPoint = "uloc_addLikelySubtags_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int uloc_addLikelySubtags(byte* localeID, byte* maximized, int cap, int* status);
        [DllImport(Lib, EntryPoint = "uloc_minimizeSubtags_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int uloc_minimizeSubtags(byte* localeID, byte* minimized, int cap, int* status);

        [DllImport(Lib, EntryPoint = "uloc_isRightToLeft_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern byte uloc_isRightToLeft(byte* locale);

        internal static string Canonicalize(string raw)
        {
            // BCP-47 first (handles -u- extensions like -u-nu-thai → @numbers=thai);
            // fall back to legacy canonicalisation for ids forLanguageTag rejects.
            var tag = Bytes(raw.Replace('_', '-'));
            string viaTag = FillBytes((b, cap, st) => { int p; fixed (byte* t = tag) return uloc_forLanguageTag(t, b, cap, &p, st); });
            if (!string.IsNullOrEmpty(viaTag) && viaTag != "und") return viaTag;
            var u = Bytes(raw.Replace('-', '_'));
            return FillBytes((b, cap, st) => { fixed (byte* p = u) return uloc_canonicalize(p, b, cap, st); });
        }

        internal static string GetLanguage(string locale) { var l = Bytes(locale); return FillBytes((b, c, s) => { fixed (byte* p = l) return uloc_getLanguage(p, b, c, s); }); }
        internal static string GetScript(string locale) { var l = Bytes(locale); return FillBytes((b, c, s) => { fixed (byte* p = l) return uloc_getScript(p, b, c, s); }); }
        internal static string GetCountry(string locale) { var l = Bytes(locale); return FillBytes((b, c, s) => { fixed (byte* p = l) return uloc_getCountry(p, b, c, s); }); }

        internal static string DisplayLanguage(string locale, string display) { var l = Bytes(locale); var d = Bytes(display); return Fill((b, c, s) => { fixed (byte* p = l) fixed (byte* q = d) return uloc_getDisplayLanguage(p, q, b, c, s); }); }
        internal static string DisplayCountry(string locale, string display) { var l = Bytes(locale); var d = Bytes(display); return Fill((b, c, s) => { fixed (byte* p = l) fixed (byte* q = d) return uloc_getDisplayCountry(p, q, b, c, s); }); }
        internal static string DisplayScript(string locale, string display) { var l = Bytes(locale); var d = Bytes(display); return Fill((b, c, s) => { fixed (byte* p = l) fixed (byte* q = d) return uloc_getDisplayScript(p, q, b, c, s); }); }
        internal static string DisplayCalendar(string localeWithCalendar, string display)
        {
            var l = Bytes(localeWithCalendar); var k = Bytes("calendar"); var d = Bytes(display);
            return Fill((b, c, s) => { fixed (byte* p = l) fixed (byte* kk = k) fixed (byte* q = d) return uloc_getDisplayKeywordValue(p, kk, q, b, c, s); });
        }

        internal static string SetKeyword(string locale, string keyword, string value)
        {
            var k = Bytes(keyword); var v = Bytes(value);
            var src = Encoding.UTF8.GetBytes(locale);
            int cap = src.Length + keyword.Length + value.Length + 16;
            while (true)
            {
                var buf = new byte[cap];
                Array.Copy(src, buf, src.Length);
                buf[src.Length] = 0; // the buffer must hold the source locale, NUL-terminated, on entry.
                int status = 0, need;
                fixed (byte* b = buf) fixed (byte* kk = k) fixed (byte* vv = v)
                    need = uloc_setKeywordValue(kk, vv, b, cap, &status);
                if (status == U_BUFFER_OVERFLOW_ERROR) { cap = need + 1; continue; }
                Check(status);
                return Encoding.UTF8.GetString(buf, 0, need);
            }
        }

        internal static string GetKeyword(string locale, string keyword)
        {
            var l = Bytes(locale); var k = Bytes(keyword);
            return FillBytes((b, c, s) => { fixed (byte* p = l) fixed (byte* kk = k) return uloc_getKeywordValue(p, kk, b, c, s); });
        }

        internal static string AddLikely(string locale) { var l = Bytes(locale); return FillBytes((b, c, s) => { fixed (byte* p = l) return uloc_addLikelySubtags(p, b, c, s); }); }
        internal static string MinimizeLikely(string locale) { var l = Bytes(locale); return FillBytes((b, c, s) => { fixed (byte* p = l) return uloc_minimizeSubtags(p, b, c, s); }); }

        internal static bool IsRtl(string locale) { var l = Bytes(locale); fixed (byte* p = l) return uloc_isRightToLeft(p) != 0; }

        // --- ures (resource bundle: delimiters for quote) -------------- //

        [DllImport(Lib, EntryPoint = "ures_open_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ures_open(byte* packageName, byte* locale, int* status);
        [DllImport(Lib, EntryPoint = "ures_getByKey_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ures_getByKey(IntPtr bundle, byte* key, IntPtr fillIn, int* status);
        [DllImport(Lib, EntryPoint = "ures_getStringByKey_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern char* ures_getStringByKey(IntPtr bundle, byte* key, int* len, int* status);
        [DllImport(Lib, EntryPoint = "ures_close_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ures_close(IntPtr bundle);

        internal static string? Delimiter(string locale, string key)
        {
            var loc = Bytes(locale); var dk = Bytes("delimiters"); var kk = Bytes(key);
            int status = 0;
            IntPtr root, delims;
            fixed (byte* l = loc) root = ures_open(null, l, &status);
            if (status > 0 || root == IntPtr.Zero) return null;
            try
            {
                fixed (byte* d = dk) delims = ures_getByKey(root, d, IntPtr.Zero, &status);
                if (status > 0 || delims == IntPtr.Zero) return null;
                try
                {
                    int len; char* s;
                    fixed (byte* k = kk) s = ures_getStringByKey(delims, k, &len, &status);
                    if (status > 0 || s == null) return null;
                    return new string(s, 0, len);
                }
                finally { ures_close(delims); }
            }
            finally { ures_close(root); }
        }

        // --- unumberformatter v2 (number/percent/money/unit/compact) --- //

        [DllImport(Lib, EntryPoint = "unumf_openForSkeletonAndLocale_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr unumf_openForSkeletonAndLocale(char* skeleton, int skeletonLen, byte* locale, int* status);
        [DllImport(Lib, EntryPoint = "unumf_openResult_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr unumf_openResult(int* status);
        [DllImport(Lib, EntryPoint = "unumf_formatDouble_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern void unumf_formatDouble(IntPtr uformatter, double value, IntPtr uresult, int* status);
        [DllImport(Lib, EntryPoint = "unumf_resultToString_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int unumf_resultToString(IntPtr uresult, char* buffer, int cap, int* status);
        [DllImport(Lib, EntryPoint = "unumf_closeResult_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern void unumf_closeResult(IntPtr uresult);
        [DllImport(Lib, EntryPoint = "unumf_close_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern void unumf_close(IntPtr uformatter);

        internal static string FormatSkeleton(string locale, string skeleton, double value)
        {
            // .NET's double.NaN has its sign bit set, which makes ICU emit "-NaN";
            // a quiet NaN has no meaningful sign, so normalise to the positive form
            // every other Cosmo port produces.
            if (double.IsNaN(value)) value = BitConverter.Int64BitsToDouble(0x7FF8000000000000L);
            var loc = Bytes(locale);
            int status = 0;
            IntPtr fmt;
            fixed (char* sk = skeleton) fixed (byte* l = loc) fmt = unumf_openForSkeletonAndLocale(sk, skeleton.Length, l, &status);
            Check(status);
            try
            {
                IntPtr res = unumf_openResult(&status);
                Check(status);
                try
                {
                    unumf_formatDouble(fmt, value, res, &status);
                    Check(status);
                    var r = res;
                    return Fill((b, c, s) => unumf_resultToString(r, b, c, s));
                }
                finally { unumf_closeResult(res); }
            }
            finally { unumf_close(fmt); }
        }

        // --- unumberrangeformatter (number/money ranges) --------------- //

        [DllImport(Lib, EntryPoint = "unumrf_openForSkeletonWithCollapseAndIdentityFallback_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr unumrf_open(char* skeleton, int skeletonLen, int collapse, int identityFallback, byte* locale, IntPtr parseError, int* status);
        [DllImport(Lib, EntryPoint = "unumrf_openResult_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr unumrf_openResult(int* status);
        [DllImport(Lib, EntryPoint = "unumrf_formatDoubleRange_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern void unumrf_formatDoubleRange(IntPtr fmt, double first, double second, IntPtr result, int* status);
        [DllImport(Lib, EntryPoint = "unumrf_resultAsValue_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr unumrf_resultAsValue(IntPtr result, int* status);
        [DllImport(Lib, EntryPoint = "unumrf_closeResult_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern void unumrf_closeResult(IntPtr result);
        [DllImport(Lib, EntryPoint = "unumrf_close_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern void unumrf_close(IntPtr fmt);

        [DllImport(Lib, EntryPoint = "ufmtval_getString_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern char* ufmtval_getString(IntPtr ufmtval, int* len, int* status);

        internal static string FormatRangeSkeleton(string locale, string skeleton, double first, double second)
        {
            var loc = Bytes(locale);
            int status = 0;
            IntPtr fmt;
            fixed (char* sk = skeleton) fixed (byte* l = loc) fmt = unumrf_open(sk, skeleton.Length, 0, 0, l, IntPtr.Zero, &status);
            Check(status);
            try
            {
                IntPtr res = unumrf_openResult(&status);
                Check(status);
                try
                {
                    unumrf_formatDoubleRange(fmt, first, second, res, &status);
                    Check(status);
                    IntPtr val = unumrf_resultAsValue(res, &status);
                    Check(status);
                    int len; char* s = ufmtval_getString(val, &len, &status);
                    Check(status);
                    return new string(s, 0, len);
                }
                finally { unumrf_closeResult(res); }
            }
            finally { unumrf_close(fmt); }
        }

        // --- unum v1 (parsing + RBNF spellout/ordinal/duration) -------- //

        [DllImport(Lib, EntryPoint = "unum_open_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr unum_open(int style, char* pattern, int patternLength, byte* locale, IntPtr parseErr, int* status);
        [DllImport(Lib, EntryPoint = "unum_close_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern void unum_close(IntPtr fmt);
        [DllImport(Lib, EntryPoint = "unum_formatDouble_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int unum_formatDouble(IntPtr fmt, double number, char* result, int cap, IntPtr pos, int* status);
        [DllImport(Lib, EntryPoint = "unum_setTextAttribute_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern void unum_setTextAttribute(IntPtr fmt, int tag, char* newValue, int newValueLength, int* status);
        [DllImport(Lib, EntryPoint = "unum_parseDouble_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern double unum_parseDouble(IntPtr fmt, char* text, int textLength, int* parsePos, int* status);
        [DllImport(Lib, EntryPoint = "unum_parseDoubleCurrency_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern double unum_parseDoubleCurrency(IntPtr fmt, char* text, int textLength, int* parsePos, char* currency, int* status);
        [DllImport(Lib, EntryPoint = "unum_getSymbol_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int unum_getSymbol(IntPtr fmt, int symbol, char* buffer, int size, int* status);

        internal const int UNUM_DECIMAL = 1, UNUM_CURRENCY = 2, UNUM_SPELLOUT = 5, UNUM_ORDINAL = 6, UNUM_DURATION = 7;
        private const int UNUM_DEFAULT_RULESET = 6;

        internal static string Rbnf(string locale, int style, double value, string? ruleSet)
        {
            var loc = Bytes(locale);
            int status = 0;
            IntPtr fmt;
            fixed (byte* l = loc) fmt = unum_open(style, null, 0, l, IntPtr.Zero, &status);
            Check(status);
            try
            {
                if (ruleSet != null)
                {
                    int s2 = 0;
                    fixed (char* rs = ruleSet) unum_setTextAttribute(fmt, UNUM_DEFAULT_RULESET, rs, ruleSet.Length, &s2);
                    // ignore failure: the locale's rules may not define this set.
                }
                var f = fmt;
                return Fill((b, c, s) => unum_formatDouble(f, value, b, c, IntPtr.Zero, s));
            }
            finally { unum_close(fmt); }
        }

        internal static double ParseNumber(string locale, string text)
        {
            var loc = Bytes(locale);
            int status = 0;
            IntPtr fmt;
            fixed (byte* l = loc) fmt = unum_open(UNUM_DECIMAL, null, 0, l, IntPtr.Zero, &status);
            Check(status);
            try
            {
                int pos = 0; double v;
                fixed (char* t = text) v = unum_parseDouble(fmt, t, text.Length, &pos, &status);
                if (status > 0 || pos < text.TrimEnd().Length) throw new CosmoArgumentException($"\"{text}\" cannot be parsed as a number in {locale}.");
                return v;
            }
            finally { unum_close(fmt); }
        }

        internal static (double amount, string currency) ParseMoney(string locale, string text)
        {
            var loc = Bytes(locale);
            int status = 0;
            IntPtr fmt;
            fixed (byte* l = loc) fmt = unum_open(UNUM_CURRENCY, null, 0, l, IntPtr.Zero, &status);
            Check(status);
            try
            {
                int pos = 0; double v; var ccy = new char[4];
                fixed (char* t = text) fixed (char* c = ccy) v = unum_parseDoubleCurrency(fmt, t, text.Length, &pos, c, &status);
                if (status > 0) throw new CosmoArgumentException($"\"{text}\" cannot be parsed as money in {locale}.");
                int n = 0; while (n < ccy.Length && ccy[n] != '\0') n++;
                return (v, new string(ccy, 0, n));
            }
            finally { unum_close(fmt); }
        }

        internal static string Symbol(string locale, int symbol)
        {
            var loc = Bytes(locale);
            int status = 0;
            IntPtr fmt;
            fixed (byte* l = loc) fmt = unum_open(UNUM_DECIMAL, null, 0, l, IntPtr.Zero, &status);
            Check(status);
            try { var f = fmt; return Fill((b, c, s) => unum_getSymbol(f, symbol, b, c, s)); }
            finally { unum_close(fmt); }
        }

        // --- ucurr (currency names / inference) ------------------------ //

        [DllImport(Lib, EntryPoint = "ucurr_getName_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern char* ucurr_getName(char* currency, byte* locale, int nameStyle, byte* isChoiceFormat, int* len, int* status);
        [DllImport(Lib, EntryPoint = "ucurr_forLocale_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucurr_forLocale(byte* locale, char* buff, int buffCapacity, int* status);

        internal const int UCURR_SYMBOL_NAME = 0, UCURR_LONG_NAME = 1;

        internal static string CurrencyName(string code, string locale, int nameStyle, out bool unknown)
        {
            var loc = Bytes(locale);
            int status = 0, len = 0; byte isChoice;
            char* p;
            var ccy = (code + "\0").ToCharArray();
            fixed (char* c = ccy) fixed (byte* l = loc) p = ucurr_getName(c, l, nameStyle, &isChoice, &len, &status);
            Check(status);
            var name = p == null ? "" : new string(p, 0, len);
            unknown = name == code; // ICU echoes the bare code for an unknown currency.
            return name;
        }

        internal static string? CurrencyForLocale(string locale)
        {
            var loc = Bytes(locale);
            var s = Fill((b, c, st) => { fixed (byte* l = loc) return ucurr_forLocale(l, b, c, st); });
            return string.IsNullOrEmpty(s) ? null : s;
        }

        // --- udat (dates & times) -------------------------------------- //

        [DllImport(Lib, EntryPoint = "udat_open_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr udat_open(int timeStyle, int dateStyle, byte* locale, char* tzID, int tzIDLength, char* pattern, int patternLength, int* status);
        [DllImport(Lib, EntryPoint = "udat_format_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int udat_format(IntPtr fmt, double dateToFormat, char* result, int cap, IntPtr pos, int* status);
        [DllImport(Lib, EntryPoint = "udat_parse_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern double udat_parse(IntPtr fmt, char* text, int textLength, int* parsePos, int* status);
        [DllImport(Lib, EntryPoint = "udat_close_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern void udat_close(IntPtr fmt);
        [DllImport(Lib, EntryPoint = "udat_getSymbols_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int udat_getSymbols(IntPtr fmt, int type, int symbolIndex, char* result, int resultLength, int* status);
        [DllImport(Lib, EntryPoint = "udat_countSymbols_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int udat_countSymbols(IntPtr fmt, int type);

        internal const int UDAT_FULL = 0, UDAT_LONG = 1, UDAT_MEDIUM = 2, UDAT_SHORT = 3, UDAT_NONE = -1, UDAT_PATTERN = -2;
        // UDateFormatSymbolType: format-context month / weekday names by width.
        internal const int UDAT_MONTHS = 1, UDAT_SHORT_MONTHS = 2, UDAT_NARROW_MONTHS = 8;
        internal const int UDAT_WEEKDAYS = 3, UDAT_SHORT_WEEKDAYS = 4, UDAT_NARROW_WEEKDAYS = 9;

        internal static List<string> DateSymbols(string locale, int type)
        {
            IntPtr fmt = OpenDate(UDAT_SHORT, UDAT_SHORT, locale, null, null);
            try
            {
                int count = udat_countSymbols(fmt, type);
                var outp = new List<string>(count);
                for (int idx = 0; idx < count; idx++)
                {
                    var f = fmt; int ii = idx;
                    var s = Fill((b, c, st) => udat_getSymbols(f, type, ii, b, c, st));
                    if (s.Length != 0) outp.Add(s); // weekday arrays carry an empty slot 0.
                }
                return outp;
            }
            finally { udat_close(fmt); }
        }

        private static IntPtr OpenDate(int dateStyle, int timeStyle, string locale, string? tz, string? pattern)
        {
            var loc = Bytes(locale);
            int status = 0;
            IntPtr fmt;
            char[] tzc = (tz ?? "").ToCharArray();
            char[] pat = (pattern ?? "").ToCharArray();
            fixed (byte* l = loc)
            fixed (char* z = tzc)
            fixed (char* p = pat)
                fmt = udat_open(timeStyle, dateStyle,
                    l,
                    tz == null ? null : z, tz == null ? 0 : tzc.Length,
                    pattern == null ? null : p, pattern == null ? 0 : pat.Length,
                    &status);
            Check(status);
            return fmt;
        }

        internal static string FormatDate(int dateStyle, int timeStyle, string locale, string? tz, double epochMillis)
        {
            IntPtr fmt = OpenDate(dateStyle, timeStyle, locale, tz, null);
            try { var f = fmt; return Fill((b, c, s) => udat_format(f, epochMillis, b, c, IntPtr.Zero, s)); }
            finally { udat_close(fmt); }
        }

        internal static string FormatPattern(string locale, string? tz, string pattern, double epochMillis)
        {
            IntPtr fmt = OpenDate(UDAT_PATTERN, UDAT_PATTERN, locale, tz, pattern);
            try { var f = fmt; return Fill((b, c, s) => udat_format(f, epochMillis, b, c, IntPtr.Zero, s)); }
            finally { udat_close(fmt); }
        }

        internal static double ParseDate(int dateStyle, int timeStyle, string locale, string? tz, string? pattern, string text)
        {
            IntPtr fmt = OpenDate(dateStyle, timeStyle, locale, tz, pattern);
            try
            {
                int pos = 0, status = 0; double v;
                fixed (char* t = text) v = udat_parse(fmt, t, text.Length, &pos, &status);
                if (status > 0) throw new CosmoArgumentException($"\"{text}\" cannot be parsed as a date in {locale}.");
                return v;
            }
            finally { udat_close(fmt); }
        }

        // --- udtitvfmt (date intervals / ranges) ----------------------- //

        [DllImport(Lib, EntryPoint = "udtitvfmt_open_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr udtitvfmt_open(byte* locale, char* skeleton, int skeletonLength, char* tzID, int tzIDLength, int* status);
        [DllImport(Lib, EntryPoint = "udtitvfmt_format_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int udtitvfmt_format(IntPtr fmt, double fromDate, double toDate, char* result, int resultCapacity, IntPtr pos, int* status);
        [DllImport(Lib, EntryPoint = "udtitvfmt_close_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern void udtitvfmt_close(IntPtr fmt);

        internal static string FormatInterval(string locale, string skeleton, string? tz, double from, double to)
        {
            var loc = Bytes(locale);
            int status = 0;
            IntPtr fmt;
            char[] tzc = (tz ?? "").ToCharArray();
            fixed (byte* l = loc) fixed (char* sk = skeleton) fixed (char* z = tzc)
                fmt = udtitvfmt_open(l, sk, skeleton.Length, tz == null ? null : z, tz == null ? 0 : tzc.Length, &status);
            Check(status);
            try { var f = fmt; return Fill((b, c, s) => udtitvfmt_format(f, from, to, b, c, IntPtr.Zero, s)); }
            finally { udtitvfmt_close(fmt); }
        }

        // --- ucal (calendar: weekInfo, tz display name) ---------------- //

        [DllImport(Lib, EntryPoint = "ucal_open_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ucal_open(char* zoneID, int len, byte* locale, int type, int* status);
        [DllImport(Lib, EntryPoint = "ucal_getAttribute_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucal_getAttribute(IntPtr cal, int attr);
        [DllImport(Lib, EntryPoint = "ucal_getDayOfWeekType_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucal_getDayOfWeekType(IntPtr cal, int dayOfWeek, int* status);
        [DllImport(Lib, EntryPoint = "ucal_getTimeZoneDisplayName_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucal_getTimeZoneDisplayName(IntPtr cal, int type, byte* locale, char* result, int resultLength, int* status);
        [DllImport(Lib, EntryPoint = "ucal_close_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ucal_close(IntPtr cal);

        private const int UCAL_FIRST_DAY_OF_WEEK = 1, UCAL_MINIMAL_DAYS = 2;
        private const int UCAL_WEEKDAY = 0, UCAL_WEEKEND = 1, UCAL_WEEKEND_ONSET = 2, UCAL_WEEKEND_CEASE = 3;

        internal static (int firstDay, int minimalDays, List<int> weekend) WeekData(string locale)
        {
            var loc = Bytes(locale);
            int status = 0;
            IntPtr cal;
            fixed (byte* l = loc) cal = ucal_open(null, 0, l, 0, &status);
            Check(status);
            try
            {
                int first = ucal_getAttribute(cal, UCAL_FIRST_DAY_OF_WEEK);
                int minimal = ucal_getAttribute(cal, UCAL_MINIMAL_DAYS);
                var weekend = new List<int>();
                // ICU days: Sunday=1 .. Saturday=7.
                for (int d = 1; d <= 7; d++)
                {
                    int t = ucal_getDayOfWeekType(cal, d, &status);
                    Check(status);
                    if (t == UCAL_WEEKEND || t == UCAL_WEEKEND_ONSET || t == UCAL_WEEKEND_CEASE)
                        weekend.Add(d);
                }
                return (first, minimal, weekend);
            }
            finally { ucal_close(cal); }
        }

        // --- ucol (collation) ------------------------------------------ //

        [DllImport(Lib, EntryPoint = "ucol_open_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ucol_open(byte* loc, int* status);
        [DllImport(Lib, EntryPoint = "ucol_strcoll_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucol_strcoll(IntPtr coll, char* source, int sourceLength, char* target, int targetLength);
        [DllImport(Lib, EntryPoint = "ucol_setStrength_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ucol_setStrength(IntPtr coll, int strength);
        [DllImport(Lib, EntryPoint = "ucol_setAttribute_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ucol_setAttribute(IntPtr coll, int attr, int value, int* status);
        [DllImport(Lib, EntryPoint = "ucol_close_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ucol_close(IntPtr coll);

        internal const int UCOL_PRIMARY = 0, UCOL_SECONDARY = 1, UCOL_TERTIARY = 2;
        private const int UCOL_CASE_FIRST = 2, UCOL_CASE_LEVEL = 3, UCOL_NUMERIC_COLLATION = 7;
        private const int UCOL_OFF = 16, UCOL_ON = 17, UCOL_LOWER_FIRST = 24, UCOL_UPPER_FIRST = 25;

        internal sealed class Collator : IDisposable
        {
            private IntPtr _c;
            internal Collator(string locale)
            {
                var loc = Bytes(locale);
                int status = 0;
                fixed (byte* l = loc) _c = ucol_open(l, &status);
                Check(status);
            }
            internal void SetStrength(int s) => ucol_setStrength(_c, s);
            internal void SetCaseLevel(bool on) { int st = 0; ucol_setAttribute(_c, UCOL_CASE_LEVEL, on ? UCOL_ON : UCOL_OFF, &st); Check(st); }
            internal void SetNumeric(bool on) { int st = 0; ucol_setAttribute(_c, UCOL_NUMERIC_COLLATION, on ? UCOL_ON : UCOL_OFF, &st); Check(st); }
            internal void SetCaseFirst(string mode)
            {
                int v = mode == "upper" ? UCOL_UPPER_FIRST : mode == "lower" ? UCOL_LOWER_FIRST : UCOL_OFF;
                int st = 0; ucol_setAttribute(_c, UCOL_CASE_FIRST, v, &st); Check(st);
            }
            internal int Compare(string a, string b)
            {
                fixed (char* pa = a) fixed (char* pb = b)
                    return ucol_strcoll(_c, pa, a.Length, pb, b.Length);
            }
            public void Dispose() { if (_c != IntPtr.Zero) { ucol_close(_c); _c = IntPtr.Zero; } }
        }

        // --- ubrk (segmentation) --------------------------------------- //

        [DllImport(Lib, EntryPoint = "ubrk_open_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ubrk_open(int type, byte* locale, char* text, int textLength, int* status);
        [DllImport(Lib, EntryPoint = "ubrk_first_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ubrk_first(IntPtr bi);
        [DllImport(Lib, EntryPoint = "ubrk_next_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ubrk_next(IntPtr bi);
        [DllImport(Lib, EntryPoint = "ubrk_preceding_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ubrk_preceding(IntPtr bi, int offset);
        [DllImport(Lib, EntryPoint = "ubrk_getRuleStatus_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ubrk_getRuleStatus(IntPtr bi);
        [DllImport(Lib, EntryPoint = "ubrk_close_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ubrk_close(IntPtr bi);

        internal const int UBRK_CHARACTER = 0, UBRK_WORD = 1, UBRK_SENTENCE = 3;
        internal const int UBRK_DONE = -1;
        internal const int UBRK_WORD_NONE_LIMIT = 100;

        /// <summary>Returns the boundary offsets [b0=0, b1, b2, ... text.Length].</summary>
        internal static List<(int start, int end, int status)> Boundaries(int type, string locale, string text)
        {
            var loc = Bytes(locale);
            int status = 0;
            IntPtr bi;
            char[] t = text.ToCharArray();
            fixed (byte* l = loc) fixed (char* tp = t) bi = ubrk_open(type, l, t.Length == 0 ? null : tp, t.Length, &status);
            Check(status);
            try
            {
                var outp = new List<(int, int, int)>();
                int start = ubrk_first(bi);
                for (int end = ubrk_next(bi); end != UBRK_DONE; start = end, end = ubrk_next(bi))
                    outp.Add((start, end, ubrk_getRuleStatus(bi)));
                return outp;
            }
            finally { ubrk_close(bi); }
        }

        internal static int PrecedingWordBoundary(string locale, string text, int offset)
        {
            var loc = Bytes(locale);
            int status = 0;
            IntPtr bi;
            char[] t = text.ToCharArray();
            fixed (byte* l = loc) fixed (char* tp = t) bi = ubrk_open(UBRK_WORD, l, t.Length == 0 ? null : tp, t.Length, &status);
            Check(status);
            try { return ubrk_preceding(bi, offset); }
            finally { ubrk_close(bi); }
        }

        // --- uplrules (plural categories) ------------------------------ //

        [DllImport(Lib, EntryPoint = "uplrules_openForType_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr uplrules_openForType(byte* locale, int type, int* status);
        [DllImport(Lib, EntryPoint = "uplrules_select_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int uplrules_select(IntPtr uplrules, double number, char* keyword, int capacity, int* status);
        [DllImport(Lib, EntryPoint = "uplrules_close_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern void uplrules_close(IntPtr uplrules);

        internal static string PluralCategory(string locale, double value, bool ordinal)
        {
            var loc = Bytes(locale);
            int status = 0;
            IntPtr pr;
            fixed (byte* l = loc) pr = uplrules_openForType(l, ordinal ? 1 : 0, &status);
            Check(status);
            try { var p = pr; return Fill((b, c, s) => uplrules_select(p, value, b, c, s)); }
            finally { uplrules_close(pr); }
        }

        // --- ulistfmt (list joining) ----------------------------------- //

        [DllImport(Lib, EntryPoint = "ulistfmt_openForType_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ulistfmt_openForType(byte* locale, int type, int width, int* status);
        [DllImport(Lib, EntryPoint = "ulistfmt_format_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ulistfmt_format(IntPtr fmt, IntPtr* strings, int* stringLengths, int stringCount, char* result, int cap, int* status);
        [DllImport(Lib, EntryPoint = "ulistfmt_close_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ulistfmt_close(IntPtr fmt);

        internal const int ULISTFMT_AND = 0, ULISTFMT_OR = 1, ULISTFMT_UNITS = 2;
        internal const int ULISTFMT_WIDE = 0, ULISTFMT_SHORT = 1, ULISTFMT_NARROW = 2;

        internal static string JoinList(string locale, int type, int width, IReadOnlyList<string> items)
        {
            if (items.Count == 0) return "";
            var loc = Bytes(locale);
            int status = 0;
            IntPtr fmt;
            fixed (byte* l = loc) fmt = ulistfmt_openForType(l, type, width, &status);
            Check(status);
            try
            {
                var handles = new GCHandle[items.Count];
                var ptrs = new IntPtr[items.Count];
                var lens = new int[items.Count];
                try
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        handles[i] = GCHandle.Alloc(items[i].ToCharArray(), GCHandleType.Pinned);
                        ptrs[i] = handles[i].AddrOfPinnedObject();
                        lens[i] = items[i].Length;
                    }
                    var f = fmt;
                    return Fill((b, c, s) =>
                    {
                        fixed (IntPtr* sp = ptrs) fixed (int* lp = lens)
                            return ulistfmt_format(f, sp, lp, items.Count, b, c, s);
                    });
                }
                finally { for (int i = 0; i < handles.Length; i++) if (handles[i].IsAllocated) handles[i].Free(); }
            }
            finally { ulistfmt_close(fmt); }
        }

        // --- ureldatefmt (relative durations) -------------------------- //

        [DllImport(Lib, EntryPoint = "ureldatefmt_open_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ureldatefmt_open(byte* locale, IntPtr nfToAdopt, int width, int capitalizationContext, int* status);
        [DllImport(Lib, EntryPoint = "ureldatefmt_format_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ureldatefmt_format(IntPtr fmt, double offset, int unit, char* result, int cap, int* status);
        [DllImport(Lib, EntryPoint = "ureldatefmt_formatNumeric_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ureldatefmt_formatNumeric(IntPtr fmt, double offset, int unit, char* result, int cap, int* status);
        [DllImport(Lib, EntryPoint = "ureldatefmt_close_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ureldatefmt_close(IntPtr fmt);

        private const int UDAT_STYLE_LONG = 0;
        private const int UDISPCTX_CAPITALIZATION_NONE = 0x100;

        internal static string RelativeDuration(string locale, double offset, int unit, bool auto)
        {
            var loc = Bytes(locale);
            int status = 0;
            IntPtr fmt;
            fixed (byte* l = loc) fmt = ureldatefmt_open(l, IntPtr.Zero, UDAT_STYLE_LONG, UDISPCTX_CAPITALIZATION_NONE, &status);
            Check(status);
            try
            {
                var f = fmt;
                return Fill((b, c, s) => auto ? ureldatefmt_format(f, offset, unit, b, c, s)
                                              : ureldatefmt_formatNumeric(f, offset, unit, b, c, s));
            }
            finally { ureldatefmt_close(fmt); }
        }

        // --- case transforms ------------------------------------------- //

        [DllImport(Lib, EntryPoint = "u_strToUpper_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int u_strToUpper(char* dest, int destCapacity, char* src, int srcLength, byte* locale, int* status);
        [DllImport(Lib, EntryPoint = "u_strToLower_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int u_strToLower(char* dest, int destCapacity, char* src, int srcLength, byte* locale, int* status);

        internal static string Upper(string locale, string text)
        {
            var loc = Bytes(locale); char[] src = text.ToCharArray();
            return Fill((b, c, s) => { fixed (char* sp = src) fixed (byte* l = loc) return u_strToUpper(b, c, src.Length == 0 ? null : sp, src.Length, l, s); });
        }
        internal static string Lower(string locale, string text)
        {
            var loc = Bytes(locale); char[] src = text.ToCharArray();
            return Fill((b, c, s) => { fixed (char* sp = src) fixed (byte* l = loc) return u_strToLower(b, c, src.Length == 0 ? null : sp, src.Length, l, s); });
        }

        // --- utrans (transliteration) ---------------------------------- //

        [DllImport(Lib, EntryPoint = "utrans_openU_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr utrans_openU(char* id, int idLength, int dir, char* rules, int rulesLength, IntPtr parseError, int* status);
        [DllImport(Lib, EntryPoint = "utrans_transUChars_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern void utrans_transUChars(IntPtr trans, char* text, int* textLength, int textCapacity, int start, int* limit, int* status);
        [DllImport(Lib, EntryPoint = "utrans_close_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern void utrans_close(IntPtr trans);

        internal static string Transliterate(string id, string text)
        {
            int status = 0;
            IntPtr trans;
            fixed (char* idp = id) trans = utrans_openU(idp, id.Length, 0, null, 0, IntPtr.Zero, &status);
            if (status > 0) throw new CosmoArgumentException($"\"{id}\" is not a valid transliterator id.");
            try
            {
                int cap = Math.Max(64, text.Length * 4);
                while (true)
                {
                    var buf = new char[cap];
                    for (int i = 0; i < text.Length; i++) buf[i] = text[i];
                    int len = text.Length, limit = text.Length, st = 0;
                    fixed (char* b = buf) utrans_transUChars(trans, b, &len, cap, 0, &limit, &st);
                    if (st == U_BUFFER_OVERFLOW_ERROR) { cap = len + 16; continue; }
                    Check(st);
                    return new string(buf, 0, len);
                }
            }
            finally { utrans_close(trans); }
        }

        // --- uspoof (confusables / suspicious) ------------------------- //

        [DllImport(Lib, EntryPoint = "uspoof_open_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr uspoof_open(int* status);
        [DllImport(Lib, EntryPoint = "uspoof_areConfusable_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int uspoof_areConfusable(IntPtr sc, char* id1, int len1, char* id2, int len2, int* status);
        [DllImport(Lib, EntryPoint = "uspoof_check_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern int uspoof_check(IntPtr sc, char* id, int length, int* position, int* status);
        [DllImport(Lib, EntryPoint = "uspoof_close_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern void uspoof_close(IntPtr sc);

        internal static bool Confusable(string a, string b)
        {
            int status = 0;
            IntPtr sc = uspoof_open(&status);
            Check(status);
            try
            {
                int r;
                fixed (char* pa = a) fixed (char* pb = b) r = uspoof_areConfusable(sc, pa, a.Length, pb, b.Length, &status);
                Check(status);
                return r != 0;
            }
            finally { uspoof_close(sc); }
        }

        internal static bool Suspicious(string text)
        {
            int status = 0;
            IntPtr sc = uspoof_open(&status);
            Check(status);
            try
            {
                int pos, r;
                fixed (char* t = text) r = uspoof_check(sc, t, text.Length, &pos, &status);
                Check(status);
                return r != 0;
            }
            finally { uspoof_close(sc); }
        }

        // --- enumerations (supportedValues) ---------------------------- //

        [DllImport(Lib, EntryPoint = "ucal_openTimeZones_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ucal_openTimeZones(int* status);
        [DllImport(Lib, EntryPoint = "ucol_getKeywordValues_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ucol_getKeywordValues(byte* keyword, int* status);
        [DllImport(Lib, EntryPoint = "unumsys_openAvailableNames_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr unumsys_openAvailableNames(int* status);
        [DllImport(Lib, EntryPoint = "ucurr_openISOCurrencies_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ucurr_openISOCurrencies(uint currType, int* status);
        [DllImport(Lib, EntryPoint = "ucal_getKeywordValuesForLocale_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ucal_getKeywordValuesForLocale(byte* key, byte* locale, byte commonlyUsed, int* status);
        [DllImport(Lib, EntryPoint = "utrans_openIDs_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr utrans_openIDs(int* status);

        [DllImport(Lib, EntryPoint = "uenum_next_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr uenum_next(IntPtr en, int* resultLength, int* status);
        [DllImport(Lib, EntryPoint = "uenum_close_72", CallingConvention = CallingConvention.Cdecl)]
        private static extern void uenum_close(IntPtr en);

        private static List<string> Drain(IntPtr en, int status)
        {
            Check(status);
            var outp = new List<string>();
            if (en == IntPtr.Zero) return outp;
            try
            {
                while (true)
                {
                    int len, st = 0;
                    IntPtr p = uenum_next(en, &len, &st);
                    Check(st);
                    if (p == IntPtr.Zero) break;
                    outp.Add(Marshal.PtrToStringAnsi(p, len) ?? "");
                }
                return outp;
            }
            finally { uenum_close(en); }
        }

        internal static List<string> TimeZones() { int st = 0; return Drain(ucal_openTimeZones(&st), st); }
        internal static List<string> Collations() { var k = Bytes("collation"); int st = 0; IntPtr e; fixed (byte* p = k) e = ucol_getKeywordValues(p, &st); return Drain(e, st); }
        internal static List<string> NumberingSystems() { int st = 0; return Drain(unumsys_openAvailableNames(&st), st); }
        internal static List<string> Currencies() { int st = 0; return Drain(ucurr_openISOCurrencies(1u | 8u /* COMMON | NON_DEPRECATED */, &st), st); }
        internal static List<string> Calendars() { var k = Bytes("calendar"); var l = Bytes(""); int st = 0; IntPtr e; fixed (byte* p = k) fixed (byte* ll = l) e = ucal_getKeywordValuesForLocale(p, ll, 0, &st); return Drain(e, st); }
        internal static List<string> Transliterators() { int st = 0; return Drain(utrans_openIDs(&st), st); }
    }
}
