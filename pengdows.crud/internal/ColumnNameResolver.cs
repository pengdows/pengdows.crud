// =============================================================================
// FILE: ColumnNameResolver.cs
// PURPOSE: Resolves an entity property name to its mapped [Column] name, for the public
//          ColumnName APIs on the gateways and on IDatabaseContext.
// =============================================================================

namespace pengdows.crud.@internal;

internal static class ColumnNameResolver
{
    internal static string Resolve(ITableInfo tableInfo, Type entityType, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);

        IColumnInfo? column = null;
        if (tableInfo is TableInfo concrete)
        {
            concrete.ColumnsByPropertyName.TryGetValue(propertyName, out column);
        }
        else
        {
            foreach (var candidate in tableInfo.Columns.Values)
            {
                if (string.Equals(candidate.PropertyInfo.Name, propertyName, StringComparison.Ordinal))
                {
                    column = candidate;
                    break;
                }
            }
        }

        return column?.Name ?? throw new ArgumentException(
            $"{entityType.Name}.{propertyName} is not a mapped column: no such property, or it has no [Column] attribute.",
            nameof(propertyName));
    }
}
