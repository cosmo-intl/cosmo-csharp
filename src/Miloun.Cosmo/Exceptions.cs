using System;

namespace Miloun.Cosmo
{
    /// <summary>
    /// Base exception for Cosmo. Catch this to handle any library error; catch a
    /// subclass to distinguish the cause.
    /// </summary>
    public class CosmoException : Exception
    {
        public CosmoException(string message) : base(message) { }
        public CosmoException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// A caller passed an invalid argument — an unknown currency code, an
    /// unsupported width/unit, a bad enum value, and so on.
    /// </summary>
    public class CosmoArgumentException : CosmoException
    {
        public CosmoArgumentException(string message) : base(message) { }
    }

    /// <summary>
    /// The underlying ICU build exposes no API for the requested operation.
    /// Environmental, not a caller bug — and on the C API a few ICU4J features
    /// (person names, alphabetic index, CLDR-distance matching) simply have no
    /// C binding.
    /// </summary>
    public class CosmoUnsupportedException : CosmoException
    {
        public CosmoUnsupportedException(string message) : base(message) { }
    }
}
