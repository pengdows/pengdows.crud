namespace pengdows.crud.enums;

/// <summary>
/// Specifies how database commands should handle statement preparation.
/// </summary>
public enum CommandPrepareMode
{
    /// <summary>
    /// Preparation is determined automatically from the dialect's recommendation for the
    /// database product and from connection health: a connection whose <c>Prepare()</c> fails with
    /// an error the dialect recognizes stops preparing for its lifetime, and the command runs
    /// unprepared. This is the recommended default.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Commands are always prepared via <c>cmd.Prepare()</c>, unless the dialect has vetoed
    /// preparation (e.g. after MySQL prepared-statement exhaustion) or this connection's earlier
    /// <c>Prepare()</c> failed with an error the dialect recognizes — then the command runs
    /// unprepared. Any other <c>Prepare()</c> failure is thrown.
    /// </summary>
    Always = 1,

    /// <summary>
    /// Commands are never prepared. Use this to bypass buggy provider implementations
    /// or when using databases where preparation adds unnecessary overhead.
    /// </summary>
    Never = 2
}
