namespace pengdows.crud.analyzers;

// The IDs of the diagnostics these code fixes handle; the analyzers define them (the code fixes
// don't reference the analyzer assembly, which packs this one).
internal static class DiagnosticIds
{
    public const string DatabaseContextSingleton = "PGC001";
    public const string SplitWrapObjectName = "PGC026";
}
