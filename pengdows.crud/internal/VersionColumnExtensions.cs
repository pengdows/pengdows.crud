using pengdows.crud.types.valueobjects;

namespace pengdows.crud.@internal;

internal static class VersionColumnExtensions
{
    /// <summary>
    /// True for a [Version] column the database manages as an opaque token (<see cref="byte"/>[]
    /// or <see cref="RowVersion"/>, e.g. SQL Server rowversion): it is compared in the WHERE
    /// clause but never incremented by pengdows.crud.
    /// </summary>
    internal static bool IsOpaqueVersionColumn(this IColumnInfo column)
    {
        var type = Nullable.GetUnderlyingType(column.PropertyInfo.PropertyType) ?? column.PropertyInfo.PropertyType;
        return type == typeof(byte[]) || type == typeof(RowVersion);
    }
}
