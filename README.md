# Cosmo

Turn raw values — numbers, amounts, dates, codes — into the polished text your users
actually read. Effortlessly: no formatting code to write, no duplication to maintain.

Your app stores machine values — a number, a timestamp, a country code. Your users
want to read them the way their part of the world writes them. Cosmo does that
conversion in a single call, so you stop scattering ad-hoc formatting logic across
your codebase and delete the near-duplicate code that collects around it. It's dead
simple, production-ready, and fast.

You don't need a multi-language app to benefit — point Cosmo at a single region and
everything just comes out right. The same one line already scales to every language,
script, calendar and time zone if you ever grow, with no data to ship or maintain.

```text
  ┌─ iphone.json ───────────────────┐     ┌─ locale ──────────────────┐
  │ {                               │     │                           │
  │   "model":    "iPhone 17 Pro",  │     │   en_US · en_GB · pt_BR   │
  │   "price":    999,              │     │   zh_CN · ar_SA · hi_IN   │
  │   "speed":    2000,             │     │                           │
  │   "pixels":   1234567,          │     └─────────────┬─────────────┘
  │   "cameras":  3,                │                   │
  │   "released": "2025-09-19"      │                   │
  │ }                               │                   │
  └────────────────┬────────────────┘                   │
                   └─────────────────┬──────────────────┘
                                     ▼
                               ┌───────────┐
                               │   Cosmo   │
                               └─────┬─────┘
                                     ▼
   🇺🇸  United States   ─►  September 19, 2025 · $999.00 · 2,000 MB/s · 1,234,567 · 3 cameras
   🇬🇧  United Kingdom  ─►  19 September 2025 · £999.00 · 2,000 MB/s · 1,234,567 · 3 cameras
   🇧🇷  Brazil          ─►  19 de setembro de 2025 · R$ 999,00 · 2.000 MB/s · 1.234.567 · 3 câmeras
   🇨🇳  China           ─►  2025年9月19日 · ¥999.00 · 2,000 MB/秒 · 1,234,567 · 3 个摄像头
   🇸🇦  Saudi Arabia    ─►  ٢٧ ربيع الأول ١٤٤٧ هـ · ٩٩٩٫٠٠ ر.س. · ٢٬٠٠٠ م.ب/ث · ١٬٢٣٤٬٥٦٧ · ٣ كاميرات
   🇮🇳  India           ─►  19 सितंबर 2025 · ₹999.00 · 2,000 MB/से॰ · 12,34,567 · 3 कैमरे
```

You pass a locale code; Cosmo decides the rest. There's no currency in the data, yet
each region gets its own (`$` / `£` / `R$` / `¥` / `ر.س` / `₹`). The thousands separator
follows local habit — even India's `12,34,567` grouping — Saudi Arabia switches to the
Hijri calendar and right-to-left Arabic-Indic digits, and the camera count takes the
correct plural form. All automatically.

Cosmo is implemented consistently across five languages — the same concepts, method
names and behaviour:
[JavaScript](https://github.com/cosmo-intl/cosmo-js) ([docs](https://cosmo.miloun.com/?lang=js)) ·
[Python](https://github.com/cosmo-intl/cosmo-python) ([docs](https://cosmo.miloun.com/?lang=python)) ·
[Java](https://github.com/cosmo-intl/cosmo-java) ([docs](https://cosmo.miloun.com/?lang=java)) ·
**.NET / C#** ·
[PHP](https://github.com/salarmehr/cosmopolitan) ([docs](https://cosmo.miloun.com/?lang=php)).

📖 **Full documentation, API reference and live playground:** https://cosmo.miloun.com/

## Requirements

- .NET 8+
- [`Microsoft.ICU.ICU4C.Runtime`](https://www.nuget.org/packages/Microsoft.ICU.ICU4C.Runtime) (ICU 72, declared transitively) — the native ICU4C runtime, bundled cross-platform

## Install

```sh
dotnet add package Miloun.Cosmo
```

*(Pending the first NuGet release — until then, `git clone` this repo and add a
project reference to `src/Miloun.Cosmo/Miloun.Cosmo.csproj`.)*

## Quick start

```csharp
using Miloun.Cosmo;

new Cosmo("es_ES").Money(11000.4, "EUR");   // "11.000,40 €"
new Cosmo("en").Percentage(0.2);            // "20%"
new Cosmo("en_AU").Money(1234.5);           // "$1,234.50"  (currency inferred from region)
new Cosmo("en").Precision(1);               // "1.00"       (fixed fraction digits, default 2)
new Cosmo("en").Spellout(42);               // "forty-two"
new Cosmo("fa").Language("en");             // "انگلیسی"
```

Cosmo is built around the **locale** — a short language-and-region tag like `en_US`,
`de_DE` or `fa_IR` that picks all of these conventions at once. Everything it produces
is **locale-aware** and read straight from ICU/[CLDR](https://cldr.unicode.org/), so
coverage is complete and always current — no locale tables of your own to bundle or
keep up to date. Methods are `PascalCase`, the .NET convention — otherwise the surface mirrors the
other ports one-to-one. `"en-AU"` and `"en_AU"` are both accepted (canonicalised by
ICU), as are [BCP-47](https://www.rfc-editor.org/info/bcp47)
[Unicode extensions](https://unicode.org/reports/tr35/#u_Extension)
(`fa-IR-u-nu-latn-ca-buddhist`).

## What you get

- **Locale display names** — languages, regions, scripts, calendars and currencies, plus emoji flags and writing direction.
- **Numbers & money** — decimals, fixed-precision (`Precision`), percentages, currencies (inferred from the region), units, compact notation, scientific, ranges, plus spelled-out and ordinal text.
- **Dates & times** — locale formats in any calendar (Gregorian, Persian, Buddhist…), custom ICU patterns, durations, date ranges, and relative times.
- **Text** — locale-aware sort and search, word/sentence/grapheme segmentation, case mapping and quotation marks.
- **Messages** — an [ICU MessageFormat](https://unicode-org.github.io/icu/userguide/format_parse/messages/) subset (`plural`, `selectordinal`, `select`, `number`).
- **Parsing & transforms** — the inverse formatters for numbers, money and dates, transliteration, UTS #39 spoof checks, locale negotiation, and raw resource-bundle access.

This port reaches the family's surface by P/Invoking native **ICU4C** (the managed
ICU4N is missing the entire formatting layer). A few ICU4J/C++ features have no C
binding and throw `CosmoUnsupportedException` — `PersonName`, `IndexBuckets`,
`BestMatch`, negotiated `FromAcceptLanguage(header, supported)`, and
`SupportedValues("unit")`. See [ICU-C-API-LIMITATIONS.md](ICU-C-API-LIMITATIONS.md)
for the full rationale and the handful of value divergences.

## Build & test

The library targets `net8.0` and depends on the native ICU4C runtime. The unit
suite mirrors the Java tests and runs in the .NET SDK container:

```sh
docker run --rm -v "$PWD":/work -w /work/tests/Miloun.Cosmo.Tests \
  -v cosmo-nuget:/root/.nuget/packages mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet test -c Release
```

## Errors

All library errors extend `CosmoException`:

- `CosmoArgumentException` — bad caller input (an unknown currency code, an unsupported width/unit, …)
- `CosmoUnsupportedException` — the native ICU build can't perform the operation

## License

MIT © Aiden Adrian
