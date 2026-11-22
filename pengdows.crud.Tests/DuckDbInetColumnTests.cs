using System.Collections.Generic;
using System.Data;
using System.Net;
using System.Numerics;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

[Table("inet_doc")]
public class InetEntity
{
    [Id]
    [Column("id", DbType.Int32)]
    public int Id { get; set; }

    // An INET column (DuckDB's inet extension).
    [Column("ip", DbType.Object, NativeType = "INET")]
    public IPAddress? Ip { get; set; }
}

[Table("inet_net_doc")]
public class InetNetworkEntity
{
    [Id]
    [Column("id", DbType.Int32)]
    public int Id { get; set; }

    [Column("net", DbType.Object, NativeType = "INET")]
    public Cidr? Net { get; set; }
}

[Table("plain_ip_doc")]
public class PlainIpEntity
{
    [Id]
    [Column("id", DbType.Int32)]
    public int Id { get; set; }

    // The same property in a text column.
    [Column("ip", DbType.Object)]
    public IPAddress? Ip { get; set; }
}

/// <summary>
/// DuckDB's INET (the inet extension) rejects a VARCHAR parameter ("Conversion Error: Type VARCHAR with value '192.168.1.10' can't be
/// cast to the destination type INET", live, DuckDB 2.0.1), so a column declared <c>NativeType = "INET"</c> is written through
/// CAST(parameter AS INET). DuckDB.NET returns the column as a struct dictionary (ip_type 1 or 2, address, mask) whether or not the
/// extension is loaded on the connection, and its text form only reads as an address while it is, so the struct is decoded: an IPv4
/// address is the unsigned 32-bit value, an IPv6 address a HUGEINT offset by 2^127. Any other column, and another database, is left alone.
/// </summary>
public class DuckDbInetColumnTests
{
    private static DatabaseContext Context(SupportedDatabase database) =>
        new($"Data Source=test;EmulatedProduct={database}", new fakeDbFactory(database));

    [Fact]
    public void DuckDbWritesAnInetThroughACast()
    {
        using var context = Context(SupportedDatabase.DuckDB);
        var gateway = new TableGateway<InetEntity, int>(context);

        using var sc = gateway.BuildCreate(new InetEntity { Id = 1, Ip = IPAddress.Parse("192.168.1.10") });

        Assert.Matches(@"CAST\(\$\w+ AS INET\)", sc.Query.ToString());
    }

    [Fact]
    public void DuckDbReadsAnInetColumnAsItIs_NotThroughATextCast()
    {
        using var context = Context(SupportedDatabase.DuckDB);
        var gateway = new TableGateway<InetEntity, int>(context);

        using var sc = gateway.BuildBaseRetrieve("a");

        Assert.DoesNotContain("AS VARCHAR", sc.Query.ToString());
    }

    private static readonly BigInteger Ipv6Offset = BigInteger.Pow(2, 127);

    private static async Task<TEntity?> ReadAsync<TEntity>(string column, Dictionary<string, object> inet)
        where TEntity : class, new()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        await using var context = new DatabaseContext("Data Source=test;EmulatedProduct=DuckDB", factory);
        var connection = new fakeDbConnection { EmulatedProduct = SupportedDatabase.DuckDB };
        connection.EnqueueReaderResult(new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["id"] = 1, [column] = inet }
        })
        {
            Columns = new[]
            {
                new fakeDbColumn("id", typeof(int), "Int32"),
                new fakeDbColumn(column, typeof(Dictionary<string, object>), "Struct")
            }
        });
        factory.Connections.Add(connection);
        return await new TableGateway<TEntity, int>(context).RetrieveOneAsync(1, context);
    }

    [Fact]
    public async Task AnIPv4Struct_IsReadAsItsAddress()
    {
        var row = await ReadAsync<InetEntity>("ip", new Dictionary<string, object>
        {
            ["ip_type"] = (byte)1, ["address"] = 3232235786L, ["mask"] = (ushort)32
        });

        Assert.Equal(IPAddress.Parse("192.168.1.10"), row!.Ip);
    }

    [Fact]
    public async Task AnIPv6Struct_IsReadAsItsAddress()
    {
        // 2001:db8::1 is 0x20010db8000000000000000000000001; DuckDB keeps a HUGEINT, the unsigned value less 2^127.
        var unsigned = BigInteger.Parse("20010db8000000000000000000000001", System.Globalization.NumberStyles.HexNumber);
        var row = await ReadAsync<InetEntity>("ip", new Dictionary<string, object>
        {
            ["ip_type"] = (byte)2, ["address"] = unsigned - Ipv6Offset, ["mask"] = (ushort)128
        });

        Assert.Equal(IPAddress.Parse("2001:db8::1"), row!.Ip);
    }

    [Fact]
    public async Task AnIPv6Loopback_IsPaddedToSixteenBytes()
    {
        // ::1 is the unsigned value 1, so its 128-bit form is one byte long before padding.
        var row = await ReadAsync<InetEntity>("ip", new Dictionary<string, object>
        {
            ["ip_type"] = (byte)2, ["address"] = BigInteger.One - Ipv6Offset, ["mask"] = (ushort)128
        });

        Assert.Equal(IPAddress.IPv6Loopback, row!.Ip);
    }

    [Theory]
    [InlineData("a", 1)]
    public void AnOrdinaryStruct_IsNotAnInet(string key, int value)
    {
        var ordinary = new Dictionary<string, object?> { [key] = value };
        var wrongKind = new Dictionary<string, object?> { ["ip_type"] = (byte)3, ["address"] = 1L, ["mask"] = (ushort)1 };

        Assert.False(pengdows.crud.@internal.DuckDbInetReader.TryDecode(ordinary, out _, out _));
        Assert.False(pengdows.crud.@internal.DuckDbInetReader.TryDecode(wrongKind, out _, out _));
        Assert.False(pengdows.crud.@internal.DuckDbInetReader.TryDecode("192.168.1.10", out _, out _));
        Assert.False(pengdows.crud.@internal.DuckDbInetReader.TryDecode(null, out _, out _));
    }

    [Fact]
    public async Task AStructWithAPrefix_IsReadAsACidr()
    {
        var row = await ReadAsync<InetNetworkEntity>("net", new Dictionary<string, object>
        {
            ["ip_type"] = (byte)1, ["address"] = 167772160L, ["mask"] = (ushort)8
        });

        Assert.Equal(new Cidr(IPAddress.Parse("10.0.0.0"), 8), row!.Net);
    }

    [Fact]
    public void ADuckDbColumnWithoutTheNativeType_IsNotCast()
    {
        using var context = Context(SupportedDatabase.DuckDB);
        var gateway = new TableGateway<PlainIpEntity, int>(context);

        using var write = gateway.BuildCreate(new PlainIpEntity { Id = 1, Ip = IPAddress.Parse("192.168.1.10") });
        using var read = gateway.BuildBaseRetrieve("a");

        Assert.DoesNotContain("AS INET", write.Query.ToString());
        Assert.DoesNotContain("AS VARCHAR", read.Query.ToString());
    }

    [Fact]
    public void AnotherDatabase_WithTheSameNativeType_IsNotWrapped()
    {
        using var context = Context(SupportedDatabase.MySql);
        var gateway = new TableGateway<InetEntity, int>(context);

        using var write = gateway.BuildCreate(new InetEntity { Id = 1, Ip = IPAddress.Parse("192.168.1.10") });
        using var read = gateway.BuildBaseRetrieve("a");

        Assert.DoesNotContain("AS INET", write.Query.ToString());
        Assert.DoesNotContain("AS VARCHAR", read.Query.ToString());
    }
}
