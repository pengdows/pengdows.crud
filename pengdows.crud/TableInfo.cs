namespace pengdows.crud;

public class TableInfo : ITableInfo
{
    private IReadOnlyList<IColumnInfo>? _orderedColumns;
    private IReadOnlyList<IColumnInfo>? _primaryKeys;

    public Dictionary<string, IColumnInfo> Columns { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string Schema { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc />
    public IColumnInfo Id { get; set; } = null!;

    public IColumnInfo Version { get; set; } = null!;
    public IColumnInfo LastUpdatedBy { get; set; } = null!;
    public IColumnInfo LastUpdatedOn { get; set; } = null!;
    public IColumnInfo CreatedOn { get; set; } = null!;
    public IColumnInfo CreatedBy { get; set; } = null!;

    /// <summary>
    /// Columns sorted by their ordinal.
    /// </summary>
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