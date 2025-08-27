namespace pengdows.crud;

public class TableInfo : ITableInfo
{
    private IReadOnlyList<IColumnInfo>? _orderedColumns;
    private IReadOnlyList<IColumnInfo>? _primaryKeys;

    public Dictionary<string, IColumnInfo> Columns { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string Schema { get; set; }
    public string Name { get; set; }

    /// <inheritdoc />
    public IColumnInfo Id { get; set; }

    public IColumnInfo Version { get; set; }
    public IColumnInfo LastUpdatedBy { get; set; }
    public IColumnInfo LastUpdatedOn { get; set; }
    public IColumnInfo CreatedOn { get; set; }
    public IColumnInfo CreatedBy { get; set; }

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
