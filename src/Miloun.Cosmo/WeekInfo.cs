using System.Collections.Generic;

namespace Miloun.Cosmo
{
    /// <summary>
    /// Week conventions for a locale's region. Days use ISO-8601 numbering
    /// (1 = Monday … 7 = Sunday), matching the JS port's <c>Intl</c> convention.
    /// </summary>
    public sealed class WeekInfo
    {
        /// <summary>First day of the week, ISO numbering.</summary>
        public int FirstDay { get; }
        /// <summary>Minimal days required in the first week of the year.</summary>
        public int MinimalDays { get; }
        /// <summary>Weekend days, ISO numbering, in onset → cease order.</summary>
        public IReadOnlyList<int> Weekend { get; }

        public WeekInfo(int firstDay, int minimalDays, IReadOnlyList<int> weekend)
        {
            FirstDay = firstDay;
            MinimalDays = minimalDays;
            Weekend = weekend;
        }
    }
}
