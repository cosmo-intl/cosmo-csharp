namespace Miloun.Cosmo
{
    /// <summary>
    /// Portable number-formatting controls, mirroring the option bag the other
    /// Cosmo ports accept. Any property left <c>null</c> keeps ICU's default.
    /// </summary>
    public sealed class NumberOptions
    {
        public int? MinimumIntegerDigits { get; set; }
        public int? MinimumFractionDigits { get; set; }
        public int? MaximumFractionDigits { get; set; }
        public int? MinimumSignificantDigits { get; set; }
        public int? MaximumSignificantDigits { get; set; }
        /// <summary>ceil / floor / expand / trunc / halfExpand / halfTrunc / halfEven.</summary>
        public string? RoundingMode { get; set; }
        /// <summary>Increment in units of the last fraction digit (e.g. 5 at 2 digits → step 0.05).</summary>
        public double? RoundingIncrement { get; set; }
        public bool? UseGrouping { get; set; }
    }

    /// <summary>Collation tailoring, mirroring the other ports' collation option bag.</summary>
    public sealed class CollationOptions
    {
        public bool? Numeric { get; set; }
        /// <summary>upper / lower / false.</summary>
        public string? CaseFirst { get; set; }
    }
}
