using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.fakeDb;

/// <summary>
/// SAP HANA binds an array as ARRAY(SELECT V FROM JSON_TABLE(?, '$[*]' COLUMNS (O FOR ORDINALITY, V &lt;type&gt; PATH '$'))
/// ORDER BY O). The store evaluates that expression: the bound JSON array becomes the varbinary value the driver returns
/// for an ARRAY column, in HANA's wire encoding (an int32 element count, then each element: INTEGER, BIGINT and SMALLINT
/// a 00 (NULL) or 01 indicator and the little-endian value; DOUBLE and REAL the raw value, all 0xFF for NULL; (N)VARCHAR a
/// length (one byte, or F6 + uint16, or F7 + int32) and CESU-8 bytes, FF for NULL).
/// </summary>
public class FakeDataStoreHanaArrayTests
{
    private const string ArrayOf = "ARRAY(SELECT V FROM JSON_TABLE(?, '$[*]' COLUMNS (O FOR ORDINALITY, V {0} PATH '$')) ORDER BY O)";
    private const string ArrayOfCast = "ARRAY(SELECT CAST(V AS {0}) FROM JSON_TABLE(?, '$[*]' COLUMNS (O FOR ORDINALITY, V INT PATH '$')) ORDER BY O)";

    private static Task<byte[]> StoreAsync(string elementType, string json) =>
        StoreExpressionAsync(string.Format(ArrayOf, elementType), json);

    [Fact]
    public async Task ACastElementTakesTheCastTypeNotTheJsonTableType()
    {
        // A SMALLINT element (the subquery's cast) is two bytes with its indicator, though JSON_TABLE typed it INT.
        var bytes = await StoreExpressionAsync(string.Format(ArrayOfCast, "SMALLINT"), "[7,-2]");

        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0)));
        Assert.Equal(new byte[] { 1, 7, 0 }, bytes.AsSpan(4, 3).ToArray());
        Assert.Equal(new byte[] { 1, 0xFE, 0xFF }, bytes.AsSpan(7, 3).ToArray());
        Assert.Equal(10, bytes.Length);
    }

    [Fact]
    public async Task ARealCastIsFourBytesAndNullIsAllFF()
    {
        var bytes = await StoreExpressionAsync(string.Format(ArrayOfCast, "REAL"), "[1.5,null]");

        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0)));
        Assert.Equal(1.5f, BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(4)));
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, bytes.AsSpan(8, 4).ToArray());
    }

    private static async Task<byte[]> StoreExpressionAsync(string expression, string json)
    {
        using var conn = new fakeDbConnection(new FakeDataStore()) { EnableDataPersistence = true };
        conn.Open();
        using (var create = conn.CreateCommand())
        {
            create.CommandText = "CREATE TABLE t (id INTEGER, v VARBINARY(4000))";
            await create.ExecuteNonQueryAsync();
        }

        using (var insert = conn.CreateCommand())
        {
            insert.CommandText = "INSERT INTO t (id, v) VALUES (1, " + expression + ")";
            var p = insert.CreateParameter();
            p.ParameterName = "p0";
            p.Value = json;
            insert.Parameters.Add(p);
            await insert.ExecuteNonQueryAsync();
        }

        using var select = conn.CreateCommand();
        select.CommandText = "SELECT v FROM t WHERE id = 1";
        return (byte[])(await select.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task IntegerElementsAreAPresenceIndicatorAndTheValue()
    {
        var bytes = await StoreAsync("INTEGER", "[1,2,3]");

        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0)));
        Assert.Equal(new byte[] { 1, 1, 0, 0, 0 }, bytes.AsSpan(4, 5).ToArray());
        Assert.Equal(new byte[] { 1, 2, 0, 0, 0 }, bytes.AsSpan(9, 5).ToArray());
        Assert.Equal(new byte[] { 1, 3, 0, 0, 0 }, bytes.AsSpan(14, 5).ToArray());
        Assert.Equal(19, bytes.Length);
    }

    [Fact]
    public async Task ANullIntegerIsAZeroIndicatorWithNoValue()
    {
        var bytes = await StoreAsync("INTEGER", "[1,null,3]");

        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0)));
        Assert.Equal(new byte[] { 1, 1, 0, 0, 0 }, bytes.AsSpan(4, 5).ToArray());
        Assert.Equal(0, bytes[9]);
        Assert.Equal(new byte[] { 1, 3, 0, 0, 0 }, bytes.AsSpan(10, 5).ToArray());
        Assert.Equal(15, bytes.Length);
    }

    [Fact]
    public async Task BigintElementsAreTheEightByteValue()
    {
        var bytes = await StoreAsync("BIGINT", "[9007199254740993]");

        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0)));
        Assert.Equal(1, bytes[4]);
        Assert.Equal(9007199254740993L, BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(5)));
    }

    [Fact]
    public async Task DoubleElementsAreTheRawValueAndNullIsAllFF()
    {
        var bytes = await StoreAsync("DOUBLE", "[1.5,null]");

        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0)));
        Assert.Equal(1.5, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(4)));
        Assert.Equal(Enumerable.Repeat((byte)0xFF, 8), bytes.AsSpan(12, 8).ToArray());
    }

    [Fact]
    public async Task StringElementsAreLengthPrefixedCesu8()
    {
        // 😀 is U+1F600: CESU-8 sends its two UTF-16 surrogates, three bytes each (six bytes), not UTF-8's four.
        var bytes = await StoreAsync("NVARCHAR(10)", "[\"ab\",\"\\ud83d\\ude00\",null]");

        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0)));
        Assert.Equal(new byte[] { 2, (byte)'a', (byte)'b' }, bytes.AsSpan(4, 3).ToArray());
        Assert.Equal(6, bytes[7]);
        Assert.Equal(new byte[] { 0xED, 0xA0, 0xBD, 0xED, 0xB8, 0x80 }, bytes.AsSpan(8, 6).ToArray());
        Assert.Equal(0xFF, bytes[14]);
        Assert.Equal(15, bytes.Length);
    }
}
