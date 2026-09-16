namespace pengdows.crud.enums;

/// <summary>
/// Controls how a connection is handled when applying session settings (read-only intent,
/// timezone/date semantics, ANSI behavior, etc.) fails on first open.
/// </summary>
/// <remarks>
/// This does not affect the separate, transaction-level read-only enforcement mechanism used by
/// MySQL, MariaDB, and Oracle (<c>TryEnterReadOnlyTransactionAsync</c>), which remains
/// best-effort regardless of this setting.
/// </remarks>
public enum SessionInitializationFailureMode
{
    /// <summary>
    /// Default. Log the failure and proceed with the connection in an unknown session state.
    /// This is the current (2.0) behavior.
    /// </summary>
    BestEffort = 0,

    /// <summary>
    /// Throw a <see cref="pengdows.crud.exceptions.ConnectionException"/> and reject the
    /// connection when session settings fail to apply.
    /// </summary>
    FailClosed = 1
}
