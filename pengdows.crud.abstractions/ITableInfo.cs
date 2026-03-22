namespace pengdows.crud;

/// <summary>
/// Describes table-level metadata and associated column mappings.
/// </summary>
internal interface ITableInfo
{
    /// <summary>
    /// Schema that owns the table.
    /// </summary>
    string Schema { get; }

    /// <summary>
    /// Name of the table.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Collection of columns keyed by column name (case-insensitive).
    /// </summary>
    IReadOnlyDictionary<string, IColumnInfo> Columns { get; }

    /// <summary>
    /// Columns sorted by their <see cref="IColumnInfo.Ordinal"/>.
    /// </summary>
    IReadOnlyList<IColumnInfo> OrderedColumns { get; }

    /// <summary>
    /// Columns marked with <see cref="IColumnInfo.IsPrimaryKey"/>, ordered by <see cref="IColumnInfo.PkOrder"/>.
    /// </summary>
    IReadOnlyList<IColumnInfo> PrimaryKeys { get; }

    /// <summary>
    /// Column representing the pseudo key used to uniquely identify a row.
    /// This <c>Id</c> differs from any business-defined <see cref="IColumnInfo.IsPrimaryKey"/>
    /// columns and should not be mistaken for the primary key.
    /// </summary>
    IColumnInfo Id { get; }

    /// <summary>
    /// Column used for optimistic concurrency checks.
    /// </summary>
    IColumnInfo Version { get; }

    /// <summary>
    /// Column used as a correlation token for identity retrieval.
    /// </summary>
    IColumnInfo CorrelationColumn { get; }

    /// <summary>
    /// Column used as a correlation token for identity retrieval.
    /// </summary>
    IColumnInfo CorrelationColumn { get; set; }

    /// <summary>
    /// Column capturing the last updater identifier.
    /// </summary>
    IColumnInfo LastUpdatedBy { get; }

    /// <summary>
    /// Column capturing the last update timestamp.
    /// </summary>
    IColumnInfo LastUpdatedOn { get; }

    /// <summary>
    /// Column capturing the creation timestamp.
    /// </summary>
    IColumnInfo CreatedOn { get; }

    /// <summary>
    /// Column capturing the creator identifier.
    /// </summary>
    IColumnInfo CreatedBy { get; }

    /// <summary>
    /// Indicates whether any audit-related columns are configured for the table.
    /// </summary>
    bool HasAuditColumns { get; }
}