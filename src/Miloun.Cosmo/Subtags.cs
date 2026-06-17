namespace Miloun.Cosmo
{
    /// <summary>Parsed language / script / region subtags of a locale. Empty when absent.</summary>
    public sealed class Subtags
    {
        public string Language { get; }
        public string Script { get; }
        public string Region { get; }

        public Subtags(string language, string script, string region)
        {
            Language = language ?? "";
            Script = script ?? "";
            Region = region ?? "";
        }
    }
}
