# cosmo-csharp — ICU binding & C-API limitations

The .NET port reaches the rest of the Cosmo family's surface by P/Invoking the
**native ICU4C** library (the `Microsoft.ICU.ICU4C.Runtime` package, ICU 72),
rather than a managed ICU.

## Why ICU4C and not a managed ICU

- **ICU4N** (the managed ICU4J port) is only ~50% ported: it ships collation,
  break iteration, transliteration, plural rules and `UCultureInfo`, but the
  entire **formatting layer is absent** — `NumberFormat`/`DateFormat`/`Calendar`/
  `TimeZone`/`MeasureFormat`/`MessageFormat`/`RelativeDateTimeFormatter`/
  `ListFormatter` are not in the package. That is the bulk of Cosmo, so ICU4N
  cannot back this port today.
- **`System.Globalization` (BCL)** is ICU-backed but exposes a much smaller
  surface (no transliteration, spoof, RBNF spellout, word/sentence segmentation,
  units, list formatting, relative time, …) — a reduced port, not parity.
- **ICU4C via P/Invoke** is the only path to full parity, matching the
  `cosmo-go-scope.md` conclusion that "CGo + ICU4C is the only path to true
  parity." The cost is a native, per-platform dependency.

## Methods with no ICU C binding

The C API does not expose a handful of ICU4J/C++ features. These throw
`CosmoUnsupportedException` and are reported as **N/A** in the cross-port verifier:

| Method | Reason |
|---|---|
| `PersonName(...)` | `PersonNameFormatter` is ICU 73+; the bundled ICU4C is 72. |
| `IndexBuckets(...)` | `AlphabeticIndex` is C++-only (no `ualphaidx`). |
| `BestMatch(...)` | CLDR-distance `LocaleMatcher` is C++-only. |
| `FromAcceptLanguage(header, supported, …)` | Same — negotiated matching needs `LocaleMatcher`. |
| `SupportedValues("unit")` | ICU C has no enumeration of measurement units. |

Non-negotiated `FromAcceptLanguage(header)`, and every other method, are fully
supported.

## Known value divergences from the other ports

- **`Script(code)`** returns the *standalone* CLDR script name (`"Simplified Han"`)
  rather than the *contextual* form (`"Simplified"`) the other ports emit. The
  contextual variant is only reachable through a C++/deprecated API; `uloc_getDisplayScript`
  returns the standalone name. (The Java port notes the same trade-off in reverse.)
- **`Message(...)`** is a faithful *subset* of ICU MessageFormat — argument
  substitution plus `plural` / `selectordinal` / `select` / `number` with `#`,
  backed by ICU plural rules and number formatting. The native C `umsg` API
  supports neither named arguments nor a non-varargs entry point, so the pattern
  is parsed in managed code while every locale-sensitive decision is still ICU's.

## Building, testing, verifying

The library targets **net8.0** and depends on the native ICU4C runtime, so the
`NativeLibrary` DLL-import resolver (`Icu.cs`) is required — hence net8.0 rather
than a `netstandard2.0` multi-target.

```sh
# unit tests (47, mirroring the Java suite) — runs in the dotnet SDK container:
docker run --rm -v "$PWD":/work -w /work/tests/Miloun.Cosmo.Tests \
  -v cosmo-nuget:/root/.nuget/packages mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet test -c Release

# cross-port verifier (from ../verify): builds & runs the C# runner in Docker too
./run.sh                      # default suite
./run.sh cases-rare.json rare # edge-case suite
```
