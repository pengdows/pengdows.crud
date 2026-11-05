using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.fakeDb;

/// <summary>
/// SQL Server binds a geometry as geometry::STGeomFromWKB(bytes, srid), the bound value being a big-endian SRID then the
/// WKB. The store evaluates it to the server's native encoding (version 1: SRID, version, flags, the points, and for a
/// line or polygon the figures and shapes), which SQL Server returns and the spatial reader decodes. A geography stores
/// latitude then longitude.
/// </summary>
public class FakeDataStoreSqlServerSpatialTests
{
    private const string Expression =
        "CASE WHEN @p0 IS NULL THEN NULL ELSE {0}::STGeomFromWKB(SUBSTRING(CAST(@p0 AS varbinary(max)), 5, DATALENGTH(@p0)), " +
        "CAST(SUBSTRING(CAST(@p0 AS varbinary(max)), 1, 4) AS int)) END";

    private static byte[] Argument(int srid, byte[] wkb)
    {
        var argument = new byte[4 + wkb.Length];
        BinaryPrimitives.WriteInt32BigEndian(argument, srid);
        wkb.CopyTo(argument, 4);
        return argument;
    }

    private static byte[] PointWkb(double x, double y)
    {
        var wkb = new byte[21];
        wkb[0] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(wkb.AsSpan(1), 1);
        BinaryPrimitives.WriteDoubleLittleEndian(wkb.AsSpan(5), x);
        BinaryPrimitives.WriteDoubleLittleEndian(wkb.AsSpan(13), y);
        return wkb;
    }

    [Fact]
    public async Task ReadResultsCarryTheirOwnUdtColumnsSoALaterReadCannotChangeThem()
    {
        // The UDT map of a result belongs to that result: a later statement on the same store must not change it.
        var store = new FakeDataStore();
        using var conn = new fakeDbConnection(store) { EnableDataPersistence = true };
        conn.Open();
        using (var create = conn.CreateCommand())
        {
            create.CommandText = "CREATE TABLE t (id INTEGER, g VARBINARY(max))";
            await create.ExecuteNonQueryAsync();
        }
        using (var plain = conn.CreateCommand())
        {
            plain.CommandText = "CREATE TABLE u (id INTEGER)";
            await plain.ExecuteNonQueryAsync();
        }
        using (var insert = conn.CreateCommand())
        {
            insert.CommandText = "INSERT INTO t (id, g) VALUES (1, " + string.Format(Expression, "geometry") + ")";
            var p = insert.CreateParameter();
            p.ParameterName = "@p0";
            p.Value = Argument(4326, PointWkb(1, 2));
            insert.Parameters.Add(p);
            await insert.ExecuteNonQueryAsync();
        }

        var first = store.ExecuteReaderResultSets("SELECT g FROM t", null, out var firstUdt);
        var second = store.ExecuteReaderResultSets("SELECT id FROM u", null, out var secondUdt);

        Assert.NotEmpty(first);
        Assert.NotNull(firstUdt);
        Assert.Equal("geometry", firstUdt!["g"]);
        Assert.Null(secondUdt);
        Assert.Equal("geometry", firstUdt["g"]);
        Assert.NotEmpty(second);
    }

    private static async Task<byte[]> StoreAsync(string geometryType, byte[] argument)
    {
        using var conn = new fakeDbConnection(new FakeDataStore()) { EnableDataPersistence = true };
        conn.Open();
        using (var create = conn.CreateCommand())
        {
            create.CommandText = "CREATE TABLE t (id INTEGER, g VARBINARY(max))";
            await create.ExecuteNonQueryAsync();
        }

        using (var insert = conn.CreateCommand())
        {
            insert.CommandText = "INSERT INTO t (id, g) VALUES (1, " + string.Format(Expression, geometryType) + ")";
            var p = insert.CreateParameter();
            p.ParameterName = "@p0";
            p.Value = argument;
            insert.Parameters.Add(p);
            await insert.ExecuteNonQueryAsync();
        }

        using var select = conn.CreateCommand();
        select.CommandText = "SELECT g FROM t WHERE id = 1";
        return (byte[])(await select.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task APointIsTheNativeSinglePointEncoding()
    {
        var native = await StoreAsync("geometry", Argument(4326, PointWkb(1, 2)));

        Assert.Equal(4326, BinaryPrimitives.ReadInt32LittleEndian(native.AsSpan(0)));
        Assert.Equal(1, native[4]);
        Assert.Equal(0x08, native[5]);
        Assert.Equal(1.0, BinaryPrimitives.ReadDoubleLittleEndian(native.AsSpan(6)));
        Assert.Equal(2.0, BinaryPrimitives.ReadDoubleLittleEndian(native.AsSpan(14)));
        Assert.Equal(22, native.Length);
    }

    [Fact]
    public async Task AGeographyStoresLatitudeThenLongitude()
    {
        // The point (longitude 1, latitude 2): the geography stores latitude first.
        var native = await StoreAsync("geography", Argument(4326, PointWkb(1, 2)));

        Assert.Equal(2.0, BinaryPrimitives.ReadDoubleLittleEndian(native.AsSpan(6)));
        Assert.Equal(1.0, BinaryPrimitives.ReadDoubleLittleEndian(native.AsSpan(14)));
    }

    [Fact]
    public async Task ASpatialColumnIsReportedAsItsUdtAndItsValueIsNotLoadable()
    {
        // SQL Server reports a geometry column as its UDT (the driver needs Microsoft.SqlServer.Types to load the value,
        // and reads the stored bytes with GetBytes instead).
        using var conn = new fakeDbConnection(new FakeDataStore()) { EnableDataPersistence = true };
        conn.Open();
        using (var create = conn.CreateCommand())
        {
            create.CommandText = "CREATE TABLE t (id INTEGER, g VARBINARY(max))";
            await create.ExecuteNonQueryAsync();
        }

        using (var insert = conn.CreateCommand())
        {
            insert.CommandText = "INSERT INTO t (id, g) VALUES (1, " + string.Format(Expression, "geometry") + ")";
            var p = insert.CreateParameter();
            p.ParameterName = "@p0";
            p.Value = Argument(4326, PointWkb(1, 2));
            insert.Parameters.Add(p);
            await insert.ExecuteNonQueryAsync();
        }

        using var select = conn.CreateCommand();
        select.CommandText = "SELECT g FROM t WHERE id = 1";
        using var reader = await select.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("geometry", reader.GetDataTypeName(0));
        Assert.Throws<System.IO.FileNotFoundException>(() => reader.GetValue(0));
    }
}
