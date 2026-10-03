using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// DuckDB BIT (confirmed live, DuckDB.NET 1.5.6): it reads back as the bit string ("10110") and takes
/// one as a parameter, while a BitArray parameter is bound as its ToString() ("Conversion Error:
/// Invalid character encountered in string -> bit conversion: 'S'"). A BitArray property, as for
/// PostgreSQL BIT(n), is written as its bit string and read back from it.
/// </summary>
public sealed class DuckDbBitTypeTests
{
    [Table("bits")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("flags", DbType.Object)] public BitArray? Flags { get; set; }
    }

    public sealed class Mapped
    {
        public BitArray? Flags { get; set; }
    }

    private static readonly BitArray Sample = new(new[] { true, false, true, true, false });

    private static (DatabaseContext Context, fakeDbConnection Exec) Context(object flags)
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.DuckDB });
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.DuckDB };
        exec.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 1, ["flags"] = flags } });
        factory.Connections.Add(exec);
        return (new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.duckdb;EmulatedProduct=DuckDB",
            DbMode = DbMode.Standard
        }, factory), exec);
    }

    private static string Bits(BitArray? bits) =>
        bits == null ? "null" : string.Concat(bits.Cast<bool>().Select(b => b ? '1' : '0'));

    [Fact]
    public void BuildCreate_BitArray_BindsTheBitString()
    {
        var (context, _) = Context("10110");

        var sc = new TableGateway<Row, int>(context).BuildCreate(new Row { Id = 1, Flags = Sample });

        Assert.Equal("10110", sc.GetParameterValue("i1"));
    }

    [Fact]
    public async Task RetrieveOneAsync_BitString_ReadsTheBitArray()
    {
        var (context, _) = Context("10110");

        var row = await new TableGateway<Row, int>(context).RetrieveOneAsync(1);

        Assert.Equal("10110", Bits(row!.Flags));
    }

    [Fact]
    public async Task DataReaderMapper_BitString_ReadsTheBitArray()
    {
        var (context, _) = Context("10110");
        await using var sc = context.CreateSqlContainer("SELECT 1");
        await using var reader = await sc.ExecuteReaderAsync();

        var row = Assert.Single(await DataReaderMapper.LoadAsync<Mapped>(reader, new MapperOptions(Strict: true)));

        Assert.Equal("10110", Bits(row.Flags));
    }

    [Theory]
    [InlineData("10x10")]
    [InlineData("1 0")]
    public async Task RetrieveOneAsync_TextThatIsNotBits_FailsLoudly(string text)
    {
        var (context, _) = Context(text);

        await Assert.ThrowsAsync<DataMappingException>(() => new TableGateway<Row, int>(context).RetrieveOneAsync(1).AsTask());
    }
}
