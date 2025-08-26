using System;

namespace pengdows.crud;

public class TableInfo : ITableInfo
{
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
    /// Indicates whether this table contains any audit columns.
    /// </summary>
    public bool HasAuditColumns { get; set; }
}
