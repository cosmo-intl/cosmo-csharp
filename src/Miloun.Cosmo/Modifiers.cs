namespace Miloun.Cosmo
{
    /// <summary>
    /// Optional overrides resolved at construction: <c>calendar</c>, <c>currency</c>
    /// and <c>timeZone</c>. Any field may be <c>null</c> (meaning "derive from the
    /// locale, or unavailable").
    /// </summary>
    public sealed class Modifiers
    {
        /// <summary>Calendar keyword, e.g. <c>"buddhist"</c>.</summary>
        public string? Calendar { get; }
        /// <summary>ISO 4217 currency code used as the default for <see cref="Cosmo.Money(double)"/>.</summary>
        public string? Currency { get; }
        /// <summary>IANA time-zone id, e.g. <c>"Australia/Sydney"</c>.</summary>
        public string? TimeZone { get; }

        public Modifiers(string? calendar = null, string? currency = null, string? timeZone = null)
        {
            Calendar = calendar;
            Currency = currency;
            TimeZone = timeZone;
        }

        /// <summary>All-null modifiers — everything derived from the locale.</summary>
        public static Modifiers None => new Modifiers();
    }
}
