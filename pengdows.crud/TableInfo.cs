using System;
using System.Collections.Generic;
using System.Linq;

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
    public IReadOnlyList<IColumnInfo> OrderedColumns =>
        _orderedColumns ??= Columns.Values.OrderBy(c => c.Ordinal).ToList();

    /// <summary>
    /// Columns marked as primary keys, ordered by PkOrder.
    /// </summary>
    public IReadOnlyList<IColumnInfo> PrimaryKeys =>
        _primaryKeys ??= Columns.Values.Where(c => c.IsPrimaryKey)
            .OrderBy(k => k.PkOrder)
            .ToList();

    /// <summary>
    /// Indicates whether this table contains any audit columns.
    /// </summary>
    public bool HasAuditColumns { get; set; }
}
