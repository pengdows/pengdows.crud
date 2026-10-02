using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-002: a Guid declared DbType.Binary is stored as 16 bytes in its database's own order, and a
/// 16-byte column read into a Guid is decoded the same way. RFC 4122 big-endian by default (MySQL's
/// UUID_TO_BIN/BIN_TO_UUID, MySqlConnector GuidFormat=Binary16); .NET mixed-endian order where the
/// database's own GUID bytes use it (SQL Server uniqueidentifier, Oracle RAW(16) via ODP.NET, Sybase
/// ASE via AseClient). Before, a Guid declared Binary was refused and reads always used .NET order.
/// </summary>
public sealed class GuidBinaryByteOrderTests
{
    private static readonly Guid Sample = new("0190f3a1-7b2c-7d3e-8f40-123456789abc");

    public static TheoryData<SupportedDatabase, bool> Orders() => new()
    {
        { SupportedDatabase.MySql, true },
        { SupportedDatabase.MariaDb, true },
        { SupportedDatabase.TiDb, true },
        { SupportedDatabase.Sqlite, true },
        { SupportedDatabase.PostgreSql, true },
        { SupportedDatabase.SqlServer, false },
        { SupportedDatabase.Oracle, false },
        { SupportedDatabase.SybaseASE, false },
    };

    [Theory]
    [MemberData(nameof(Orders))]
    public void BuildCreate_GuidDeclaredBinary_BindsTheDatabasesByteOrder(SupportedDatabase product, bool bigEndian)
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", new fakeDbFactory(product));

        var sc = new TableGateway<Row, int>(context).BuildCreate(new Row { Id = 1, Key = Sample });

        Assert.Equal(Sample.ToByteArray(bigEndian), sc.GetParameterValue("i1"));
    }

    [Theory]
    [MemberData(nameof(Orders))]
    public async Task RetrieveOneAsync_SixteenByteColumn_DecodesTheDatabasesByteOrder(SupportedDatabase product, bool bigEndian)
    {
        var (context, exec) = Context(product);
        await using var _ = context;
        exec.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 1, ["k"] = Sample.ToByteArray(bigEndian) } });

        var row = await new TableGateway<Row, int>(context).RetrieveOneAsync(1);

        Assert.Equal(Sample, row!.Key);
    }

    [Theory]
    [MemberData(nameof(Orders))]
    public async Task DataReaderMapper_SixteenByteColumn_DecodesTheDatabasesByteOrder(SupportedDatabase product, bool bigEndian)
    {
        var (context, exec) = Context(product);
        await using var _ = context;
        exec.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 1, ["k"] = Sample.ToByteArray(bigEndian) } });
        await using var sc = context.CreateSqlContainer("SELECT id, k FROM guid_rows");
        await using var reader = await sc.ExecuteReaderAsync();

        var row = Assert.Single(await DataReaderMapper.LoadAsync<Row>(reader, new MapperOptions(ColumnsOnly: true)));

        Assert.Equal(Sample, row.Key);
    }

    [Fact]
    public async Task DataReaderMapper_NullableGuidFromBytes_DecodesTheDatabasesByteOrder()
    {
        var (context, exec) = Context(SupportedDatabase.MySql);
        await using var _ = context;
        exec.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 1, ["k"] = Sample.ToByteArray(true) } });
        await using var sc = context.CreateSqlContainer("SELECT id, k FROM guid_rows");
        await using var reader = await sc.ExecuteReaderAsync();

        var row = Assert.Single(await DataReaderMapper.LoadAsync<NullableRow>(reader, new MapperOptions(ColumnsOnly: true)));

        Assert.Equal(Sample, row.Key);
    }

    private static (DatabaseContext Context, fakeDbConnection Exec) Context(SupportedDatabase product)
    {
        var factory = new fakeDbFactory(product);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = product });
        var exec = new fakeDbConnection { EmulatedProduct = product };
        factory.Connections.Add(exec);
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={product}",
            DbMode = DbMode.Standard
        }, factory);
        return (context, exec);
    }

    [Table("guid_rows")]
    private sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("k", DbType.Binary)] public Guid Key { get; set; }
    }

    [Table("guid_rows")]
    private sealed class NullableRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("k", DbType.Binary)] public Guid? Key { get; set; }
    }
}
