namespace pengdows.crud.enums;

public enum IsolationProfile
{
    /// <summary>
    /// MVCC snapshot-style, avoids blocking, no dirty reads. Beginning a transaction with this
    /// profile throws TransactionModeNotSupportedException where it cannot be guaranteed (SQL Server
    /// without snapshot isolation enabled, PostgreSQL, YugabyteDB).
    /// </summary>
    SafeNonBlockingReads, // MVCC snapshot-style, avoids blocking, no dirty reads

    /// <summary>
    /// Serializable, fully isolated, best for financial or critical logic. Beginning a transaction
    /// with this profile throws TransactionModeNotSupportedException where Serializable is unavailable
    /// (TiDB, Snowflake, Access).
    /// </summary>
    StrictConsistency, // Serializable, fully isolated, best for financial or critical logic

    /// <summary>
    /// ReadUncommitted (dirty reads) where the database supports it, otherwise its weakest supported
    /// level. Almost never recommended.
    /// </summary>
    FastWithRisks // ReadUncommitted where supported / dirty reads (almost never recommended)
}