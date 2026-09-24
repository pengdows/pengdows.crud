namespace pengdows.crud.enums;

/// <summary>
/// Specifies how database commands should handle statement preparation.
/// </summary>
public enum CommandPrepareMode
{
    /// <summary>
    /// Preparation is determined automatically from the dialect's recommendation for the
    /// database product. This is the recommended default.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Commands are always prepared via <c>cmd.Prepare()</c>, unless the dialect has vetoed
    /// preparation (e.g. after MySQL prepared-statement exhaustion). May fail if the provider
    /// or database does not support it.
    /// </summary>
    Always = 1,

    /// <summary>
    /// Commands are never prepared. Use this to bypass buggy provider implementations
    /// or when using databases where preparation adds unnecessary overhead.
    /// </summary>
    Never = 2
}
