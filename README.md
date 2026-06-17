# Cosmo

Ergonomic application localisation for .NET, powered by [ICU](https://icu.unicode.org/) (ICU4C).

Cosmo is a thin, ergonomic layer over the native [ICU4C](https://unicode-org.github.io/icu/userguide/icu4c/)
library — the reference [ICU](https://icu.unicode.org/) implementation. Give it a
locale (and optionally a time zone) and it formats numbers, money, dates, units,
lists and messages exactly the way your users expect. There is **no bundled locale
data** — every result comes straight from ICU and [CLDR](https://cldr.unicode.org/),
covering all languages, scripts, calendars and time zones.

Cosmo is implemented consistently across languages — the same concepts, method
names and behaviour, each built directly on its platform's ICU:
[JavaScript](https://github.com/cosmo-intl/cosmo-js) ([docs](https://cosmo.miloun.com/?lang=js)) ·
[Python](https://github.com/cosmo-intl/cosmo-python) ([docs](https://cosmo.miloun.com/?lang=python)) ·
[Java](https://github.com/cosmo-intl/cosmo-java) ([docs](https://cosmo.miloun.com/?lang=java)) ·
[PHP](https://github.com/salarmehr/cosmopolitan) ([docs](https://cosmo.miloun.com/?lang=php)) ·
**.NET / C#**.

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

Methods are `PascalCase`, the .NET convention — otherwise the surface mirrors the
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
