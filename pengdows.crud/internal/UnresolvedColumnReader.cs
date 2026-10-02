using System.Data;
using pengdows.crud.types.converters;
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

        if (type == typeof(Geometry) || type == typeof(Geography))
        {
            // Same for geometry/geography; GetBytes returns SQL Server's stored spatial encoding.
            var stored = new byte[record.GetBytes(ordinal, 0, null, 0, 0)];
            record.GetBytes(ordinal, 0, stored, 0, stored.Length);
            var geography = type == typeof(Geography);
            var (srid, wkb) = SqlServerSpatialFormat.Decode(stored, geography);
            return geography ? Geography.FromWellKnownBinary(wkb, srid) : Geometry.FromWellKnownBinary(wkb, srid);
        }

        throw new NotSupportedException($"No reader for an unresolved column read as {type.Name}.");
    }
}
