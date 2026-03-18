namespace pengdows.crud.enums;

/// <summary>
/// Specifies how database commands should handle statement preparation.
/// </summary>
public enum CommandPrepareMode
{
    /// <summary>
    /// Preparation is determined automatically based on database product capabilities
    /// and connection health. This is the recommended default.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Commands are always prepared via <c>cmd.Prepare()</c>. May fail if the provider
    /// or database does not support it.
    /// </summary>
    Always = 1,

    /// <summary>
    /// Commands are never prepared. Use this to bypass buggy provider implementations
    /// or when using databases where preparation adds unnecessary overhead.
    /// </summary>
    Never = 2
}
