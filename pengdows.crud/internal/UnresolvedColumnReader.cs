using System.Data;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.@internal;

/// <summary>
/// Reads a column its provider reports no field type for, as the type the dialect named for it
/// (<c>SqlDialect.GetUnresolvedColumnType</c>, TYPE-016). The dialect knows which database type
/// it is; reading is keyed on the .NET type, so the gateway's tracked reader and DataReaderMapper
/// read it the same way.
/// </summary>
internal static class UnresolvedColumnReader
{
    public static object Read(IDataRecord record, int ordinal, Type type)
    {
        if (record.IsDBNull(ordinal))
        {
            return DBNull.Value;
        }

        if (type == typeof(HierarchyId))
        {
            // SqlClient without Microsoft.SqlServer.Types: GetValue throws, GetBytes returns
            // SQL Server's stored encoding (confirmed live).
            var bytes = new byte[record.GetBytes(ordinal, 0, null, 0, 0)];
            record.GetBytes(ordinal, 0, bytes, 0, bytes.Length);
            return HierarchyId.FromSqlServerBytes(bytes);
        }

        throw new NotSupportedException($"No reader for an unresolved column read as {type.Name}.");
    }
}
