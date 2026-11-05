using System.Collections;
using System.Collections.Generic;
using System.Data;
using pengdows.crud.dialects;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.@internal;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

[Table("bit_field")]
public class BitFieldEntity
{
    [Id]
    [Column("id", System.Data.DbType.Int32)]
    public int Id { get; set; }

    // A fixed-width BIT(3): the width is part of the column, so the value reads back with its three bits.
    [Column("flags", System.Data.DbType.Object, NativeType = "BIT(3)")]
    public BitArray Flags { get; set; } = new BitArray(0);
}

/// <summary>
/// A fixed-width BIT(n) column holds an integer of n bits. A BitArray written to it is its integer, and read back it is
/// n bits wide: the declared width is what makes the round trip exact (a value of another width would read as 8 bits).
/// </summary>
public class BitFieldTests
{
    [Fact]
    public void AnIntegerReadsAsTheDeclaredNumberOfBitsFirstBitFirst()
    {
        // 5 is 101 in three bits.
        var bits = BitStringFormat.FromBitField(5UL, 3);

        Assert.Equal("101", BitStringFormat.Format(bits));
    }

    [Fact]
    public void AnIntegerWiderThanTheDeclaredWidthIsRefused()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => BitStringFormat.FromBitField(9UL, 3));
    }

    [Theory]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    [InlineData(SupportedDatabase.TiDb)]
    [InlineData(SupportedDatabase.SingleStore)]
    public void MySqlFamilyStoresTheBitStringAsItsIntegerInTheDeclaredColumn(SupportedDatabase product)
    {
        using var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", new fakeDbFactory(product));
        var gateway = new TableGateway<BitFieldEntity, int>(context);

        using var sc = gateway.BuildCreate(new BitFieldEntity { Id = 1, Flags = new BitArray(new[] { true, false, true }) });

        // CONV reads the bit text as base 2 and the sum makes it the number BIT(3) stores.
        Assert.Contains("CONV(", sc.Query.ToString());
        Assert.Contains("2, 10) + 0", sc.Query.ToString());
    }

    [Fact]
    public void TheDeclaredWidthIsReadFromTheNativeTypeName()
    {
        Assert.Equal(3, BitFieldReader.WidthOf("BIT(3)"));
        Assert.Equal(8, BitFieldReader.WidthOf("bit(8)"));
        Assert.Null(BitFieldReader.WidthOf("VARBIT"));
        Assert.Null(BitFieldReader.WidthOf("xml"));
    }

    [Fact]
    public void AnIntegerFromTheDriverReadsAsTheDeclaredBits()
    {
        // MySqlConnector returns BIT(n) as an integer; MySql.Data returns the bytes, most significant first.
        Assert.Equal("101", BitStringFormat.Format(BitFieldReader.Read(5UL, 3)!));
        Assert.Equal("101", BitStringFormat.Format(BitFieldReader.Read(new byte[] { 0x05 }, 3)!));
    }

    [Fact]
    public void ANullBitFieldReadsAsNull()
    {
        Assert.Null(BitFieldReader.Read(System.DBNull.Value, 3));
    }

    [Fact]
    public async System.Threading.Tasks.Task AFixedWidthColumnReadsBackWithItsDeclaredBits()
    {
        // The driver returns the BIT(3) column as the integer 5; the mapper reads it with the declared width.
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.MySql });
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.MySql };
        factory.Connections.Add(exec);
        await using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Server=x;Database=y;EmulatedProduct=MySql",
            DbMode = DbMode.Standard
        }, factory);
        exec.EnqueueReaderResult(new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["id"] = 1, ["flags"] = 5L }
        }));
        var gateway = new TableGateway<BitFieldEntity, int>(context);

        var row = await gateway.RetrieveOneAsync(1);

        Assert.NotNull(row);
        Assert.Equal("101", BitStringFormat.Format(row!.Flags));
    }

    [Theory]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    [InlineData(SupportedDatabase.TiDb)]
    [InlineData(SupportedDatabase.SingleStore)]
    public void MySqlFamilyBindsABitArrayAsItsBitText(SupportedDatabase product)
    {
        // MySqlConnector refuses a BitArray parameter, so the bits go across as their 0/1 text, which CONV reads.
        var dialect = (SqlDialect)SqlDialectFactory.CreateDialectForType(product, new fakeDbFactory(product), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var parameter = dialect.CreateDbParameter("p", DbType.Object, new BitArray(new[] { true, false, true }));

        Assert.Equal("101", parameter.Value);
    }

    [Fact]
    public async System.Threading.Tasks.Task ABitColumnReturnedAsBytesByTheDriverReadsWithItsDeclaredWidth()
    {
        // MySql.Data returns BIT(n) as its bytes, most significant first; the declared width still applies.
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.MySql });
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.MySql };
        factory.Connections.Add(exec);
        await using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Server=x;Database=y;EmulatedProduct=MySql",
            DbMode = DbMode.Standard
        }, factory);
        exec.EnqueueReaderResult(new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["id"] = 1, ["flags"] = new byte[] { 0x05 } }
        }));
        var gateway = new TableGateway<BitFieldEntity, int>(context);

        var row = await gateway.RetrieveOneAsync(1);

        Assert.NotNull(row);
        Assert.Equal("101", BitStringFormat.Format(row!.Flags));
    }

    [Theory]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    [InlineData(SupportedDatabase.TiDb)]
    [InlineData(SupportedDatabase.SingleStore)]
    public void ABitArrayWithoutADeclaredWidthIsRefusedNotStoredAsBytes(SupportedDatabase product)
    {
        // Without a width the bit text is stored as the characters of "101" (wrong data, reported as a success), so the
        // write refuses the value and asks for the declared width.
        using var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", new fakeDbFactory(product));
        var gateway = new TableGateway<BitUndeclaredEntity, int>(context);

        var ex = Assert.Throws<System.NotSupportedException>(() =>
            gateway.BuildCreate(new BitUndeclaredEntity { Id = 1, Flags = new BitArray(new[] { true, false, true }) }));

        Assert.Contains("BIT(n)", ex.Message);
    }
}

[Table("bit_undeclared")]
public class BitUndeclaredEntity
{
    [Id]
    [Column("id", System.Data.DbType.Int32)]
    public int Id { get; set; }

    [Column("flags", System.Data.DbType.Object)]
    public BitArray Flags { get; set; } = new BitArray(0);
}
