namespace pengdows.crud.enums;

/// <summary>
/// Which transaction lifecycle phase a <see cref="pengdows.crud.exceptions.TransactionException"/>
/// failed in. See <see cref="pengdows.crud.exceptions.TransactionException.Phase"/>.
/// </summary>
public enum TransactionPhase
{
    /// <summary>
    /// Failed while beginning the transaction. Nothing was ever started — safe to retry from
    /// scratch with no ambiguity.
    /// </summary>
    Begin = 0,

    /// <summary>
    /// Failed while committing. The write may have already been durably applied on the server
    /// before this failure surfaced — a COMMIT is a client-sends/server-acknowledges round trip,
    /// and a failure can land on either side of that gap. Not safe to treat as an ordinary
    /// transient failure to retry blind; see "Commit ambiguity" in
    /// <c>docs/planning/retry-context-design.md</c>.
    /// </summary>
    Commit = 1,

    /// <summary>
    /// Failed while rolling back. Carries no duplicate-application risk: the transaction's writes
    /// were never meant to apply, and a rollback failing does not retroactively commit them — safe
    /// to treat as an ordinary transient failure.
    /// </summary>
    Rollback = 2
}
