namespace pengdow.crud.enums;

public enum IsolationProfile
{
    /// <summary>
    /// MVCC snapshot-style, avoids blocking, no dirty reads
    /// </summary>
    SafeNonBlockingReads, // MVCC snapshot-style, avoids blocking, no dirty reads

    /// <summary>
    /// Serializable, fully isolated, best for financial or critical logic
    /// </summary>
    StrictConsistency, // Serializable, fully isolated, best for financial or critical logic

    /// <summary>
    /// dirty reads (almost never recommended)
    /// </summary>
    FastWithRisks // ReadUncommitted / dirty reads (almost never recommended)
}