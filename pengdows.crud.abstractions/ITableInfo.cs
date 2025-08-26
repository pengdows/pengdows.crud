#region

using System.Collections.Generic;

#endregion

namespace pengdows.crud;

/// <summary>
/// Describes table-level metadata and associated column mappings.
/// </summary>
public interface ITableInfo
{
    /// <summary>
    /// Schema that owns the table.
    /// </summary>
    string Schema { get; set; }

    /// <summary>
    /// Name of the table.
    /// </summary>
    string Name { get; set; }

    /// <summary>
    /// Collection of columns keyed by property name.
    /// </summary>
    Dictionary<string, IColumnInfo> Columns { get; }

    /// <summary>
    /// Column representing the pseudo key used to uniquely identify a row.
    /// This <c>Id</c> differs from any business-defined <see cref="IColumnInfo.IsPrimaryKey"/>
    /// columns and should not be mistaken for the primary key.
    /// </summary>
    IColumnInfo Id { get; set; }

    /// <summary>
    /// Column used for optimistic concurrency checks.
    /// </summary>
    IColumnInfo Version { get; set; }

    /// <summary>
    /// Column capturing the last updater identifier.
    /// </summary>
    IColumnInfo LastUpdatedBy { get; set; }

    /// <summary>
    /// Column capturing the last update timestamp.
    /// </summary>
    IColumnInfo LastUpdatedOn { get; set; }

    /// <summary>
    /// Column capturing the creation timestamp.
    /// </summary>
    IColumnInfo CreatedOn { get; set; }

    /// <summary>
    /// Column capturing the creator identifier.
    /// </summary>
    IColumnInfo CreatedBy { get; set; }

    /// <summary>
    /// Indicates whether any audit-related columns are configured for the table.
    /// </summary>
    bool HasAuditColumns { get; set; }
}
