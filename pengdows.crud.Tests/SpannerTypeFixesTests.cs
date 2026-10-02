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
/// TYPE-002, found live on the Spanner emulator (PostgreSQL dialect, Npgsql 9):
/// - a uuid column refuses a text parameter ("column is of type uuid but expression is of type text")
///   and Npgsql can't type it as uuid either; an untyped parameter works, so a Guid is sent untyped;
/// - Npgsql can't read a uuid (no type name), but GetBytes returns its 16 bytes (RFC 4122 order);
/// - Npgsql refuses to read an array as non-nullable elements ("returned array contains nulls"); it
///   is read with nullable elements and converted.
/// </summary>
public sealed class SpannerTypeFixesTests
{
    private static readonly Guid Sample = new("0190f3a1-7b2c-7d3e-8f40-123456789abc");

    [Table("sp_rows")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("u", DbType.Guid)] public Guid U { get; set; }
        [Column("a", DbType.Object)] public long[]? A { get; set; }
    }

    private static (DatabaseContext Context, fakeDbConnection Exec) Context(bool npgsqlMetadata = false)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Spanner) { EmulatesNpgsqlParameterMetadata = npgsqlMetadata };
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Spanner });
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Spanner };
        factory.Connections.Add(exec);
        return (new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Host=x;EmulatedProduct=Spanner",
            DbMode = DbMode.Standard
        }, factory), exec);
    }

    private static fakeDbDataReader Reader() =>
        new(new[] { new Dictionary<string, object> { ["id"] = 1L, ["u"] = Sample.ToByteArray(bigEndian: true), ["a"] = new long?[] { 1, 2 } } })
        {
            UnknownTypeColumns = new HashSet<string> { "u" },
            NullableElementArrayColumns = new HashSet<string> { "a" }
        };

    [Fact]
    public void CreateDbParameter_Guid_IsSentUntyped()
    {
        var (context, _) = Context(npgsqlMetadata: true);
        using var __ = context;

        var parameter = Assert.IsType<fakeDbNpgsqlParameter>(context.CreateDbParameter("p", DbType.Guid, Sample));

        Assert.Equal(fakeNpgsqlDbType.Unknown, parameter.NpgsqlDbType);
        Assert.Equal(Sample.ToString("D"), parameter.Value);
    }

    [Fact]
    public async Task RetrieveOneAsync_UuidAndArray_AreRead()
    {
        var (context, exec) = Context();
        await using var _ = context;
        exec.EnqueueReaderResult(Reader());

        var row = await new TableGateway<Row, long>(context).RetrieveOneAsync(1);

        Assert.Equal(Sample, row!.U);
        Assert.Equal(new long[] { 1, 2 }, row.A);
    }

    [Fact]
    public async Task DataReaderMapper_UuidAndArray_AreRead()
    {
        var (context, exec) = Context();
        await using var _ = context;
        exec.EnqueueReaderResult(Reader());
        await using var sc = context.CreateSqlContainer("SELECT id, u, a FROM sp_rows");
        await using var reader = await sc.ExecuteReaderAsync();

        var row = Assert.Single(await DataReaderMapper.LoadAsync<Row>(reader, new MapperOptions(Strict: true, ColumnsOnly: true)));

        Assert.Equal(Sample, row.U);
        Assert.Equal(new long[] { 1, 2 }, row.A);
    }
}
