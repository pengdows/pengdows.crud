// =============================================================================
// FILE: TableInfo.cs
// PURPOSE: Metadata class that describes a database table's structure including
//          all columns, schema, name, and special columns (ID, version, audit).
//
// AI SUMMARY:
// - This is the runtime representation of a table's metadata, built from
//   entity attributes ([Table], [Column], etc.) by TypeMapRegistry.
// - Contains a dictionary of all columns keyed by column name (case-insensitive).
// - Provides lazy-computed OrderedColumns (by ordinal) and PrimaryKeys (by PkOrder).
// - Holds references to special columns: Id (pseudo key), Version (optimistic
//   concurrency), and audit columns (CreatedBy/On, LastUpdatedBy/On).
// - HasAuditColumns indicates if any audit tracking is configured.
// - Used by TableGateway to generate SQL statements and map data.
// - Thread-safe: computed collections use Interlocked for safe lazy init.
// =============================================================================

namespace pengdows.crud;

/// <summary>
/// Represents metadata about a database table including its schema, name, and column mappings.
/// </summary>
/// <remarks>
/// <para>
/// This class provides the structural information needed by <see cref="TableGateway{TEntity,TRowID}"/>
/// to generate SQL statements and perform data mapping operations.
/// </para>
/// <para>
/// <strong>Creation:</strong> Instances are created automatically by <see cref="TypeMapRegistry"/>
/// when an entity type is first accessed via <see cref="ITypeMapRegistry.GetTableInfo{T}"/>.
/// </para>
/// <para>
/// <strong>Thread Safety:</strong> The <see cref="OrderedColumns"/> and <see cref="PrimaryKeys"/>
/// properties are lazily computed with thread-safe initialization.
/// </para>
/// </remarks>
/// <seealso cref="ITableInfo"/>
/// <seealso cref="ColumnInfo"/>
/// <seealso cref="TypeMapRegistry"/>
public class TableInfo : ITableInfo
{
    private IReadOnlyList<IColumnInfo>? _orderedColumns;
    private IReadOnlyList<IColumnInfo>? _primaryKeys;

    /// <summary>
    /// Gets the dictionary of columns keyed by database column name.
    /// </summary>
    /// <remarks>
    /// Keys are case-insensitive to handle databases with different identifier casing.
    /// </remarks>
    public Dictionary<string, IColumnInfo> Columns { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the database schema name (e.g., "dbo", "public").
    /// </summary>
    /// <value>Empty string if no schema is specified.</value>
    public string Schema { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the database table name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc />
    /// <remarks>
    /// The Id column is the pseudo key / row identifier, marked with <see cref="Attributes.IdAttribute"/>.
    /// This is distinct from business/primary keys which may be composite.
    /// </remarks>
    public IColumnInfo Id { get; set; } = null!;

    /// <summary>
    /// Gets or sets the optimistic concurrency version column.
    /// </summary>
    /// <remarks>Null if the entity does not use optimistic concurrency.</remarks>
    public IColumnInfo Version { get; set; } = null!;

    /// <summary>
    /// Gets or sets the LastUpdatedBy audit column.
    /// </summary>
    public IColumnInfo LastUpdatedBy { get; set; } = null!;

    /// <summary>
    /// Gets or sets the LastUpdatedOn audit column.
    /// </summary>
    public IColumnInfo LastUpdatedOn { get; set; } = null!;

    /// <summary>
    /// Gets or sets the CreatedOn audit column.
    /// </summary>
    public IColumnInfo CreatedOn { get; set; } = null!;

    /// <summary>
    /// Gets or sets the CreatedBy audit column.
    /// </summary>
    public IColumnInfo CreatedBy { get; set; } = null!;

    /// <summary>
    /// Gets the columns sorted by their ordinal position (order defined in entity).
    /// </summary>
    /// <remarks>
    /// Lazily computed and cached. Thread-safe initialization via Interlocked.
    /// </remarks>
    public IReadOnlyList<IColumnInfo> OrderedColumns
    {
        get
        {
            var existing = Volatile.Read(ref _orderedColumns);
            if (existing != null)
            {
                return existing;
            }

            var computed = Columns.Values.OrderBy(c => c.Ordinal).ToList();
            Interlocked.CompareExchange(ref _orderedColumns, computed, null);
            return _orderedColumns!;
        }
    }

    /// <summary>
    /// Columns marked as primary keys, ordered by PkOrder.
    /// </summary>
    public IReadOnlyList<IColumnInfo> PrimaryKeys
    {
        get
        {
            var existing = Volatile.Read(ref _primaryKeys);
            if (existing != null)
            {
                return existing;
            }

            var computed = Columns.Values.Where(c => c.IsPrimaryKey)
                .OrderBy(k => k.PkOrder)
                .ToList();
            Interlocked.CompareExchange(ref _primaryKeys, computed, null);
            return _primaryKeys!;
        }
    }

    /// <summary>
    /// Indicates whether this table contains any audit columns.
    /// </summary>
    public bool HasAuditColumns { get; set; }
}