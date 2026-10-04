using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// COR-009: a fractional value read into an integer was silently inexact, and differently per path:
/// the gateway truncated (2.7 → 2), DataReaderMapper and scalar reads rounded to even (2.7 → 3,
/// 2.5 → 2). A value an integer can't hold exactly fails like an overflow does (TYPE-008); a whole
/// value (2.0) converts.
/// </summary>
// [Collection("TypeRegistry")]: the lenient DataReaderMapper tests log through the process-global
// TypeCoercionHelper.Logger, which tests in that collection replace (with loggers they dispose).
[Collection("TypeRegistry")]
public class FractionalIntoIntegerTests
{
    [Table("t")]
    public sealed class Row
    {
        [Id] [Column("id", System.Data.DbType.Int32)] public int Id { get; set; }
        [Column("n", System.Data.DbType.Int32)] public int N { get; set; }
        [Column("big", System.Data.DbType.Int64)] public long? Big { get; set; }
    }

    public enum Mood
    {
        Sad = 1,
        Ok = 2,
        Happy = 3
    }

    [Table("t")]
    public sealed class MoodRow
    {
        [Id] [Column("id", System.Data.DbType.Int32)] public int Id { get; set; }
        [Column("mood", System.Data.DbType.Int32)] public Mood Mood { get; set; }
    }

    public sealed class Plain
    {
        public int Id { get; set; }
        public int N { get; set; }
        public long? Big { get; set; }
    }

    public static IEnumerable<object[]> Fractional() => new[]
    {
        new object[] { 2.7m }, new object[] { 2.5m }, new object[] { -0.5m }, new object[] { 2.7d },
        new object[] { 0.1f }, new object[] { double.NaN }
    };

    public static IEnumerable<object[]> Whole() => new[]
    {
        new object[] { 2.0m, 2 }, new object[] { -3m, -3 }, new object[] { 2.0d, 2 }, new object[] { 4f, 4 }
    };

    private static (fakeDbFactory Factory, DatabaseContext Context) Create()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        return (factory, new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory));
    }

    private static Dictionary<string, object> Values(object n) =>
        new() { ["id"] = 1, ["n"] = n, ["big"] = n };

    [Theory]
    [MemberData(nameof(Fractional))]
    public async Task Gateway_FractionalValue_FailsNamingTheColumn(object value)
    {
        var (factory, context) = Create();
        await using var _ = context;
        var gateway = new TableGateway<Row, int>(context);
        factory.EnqueueReaderResult(new[] { Values(value) });
        await using var sc = context.CreateSqlContainer("SELECT id, n, big FROM t");

        var error = await Assert.ThrowsAsync<DataMappingException>(() => gateway.LoadSingleAsync(sc).AsTask());

        Assert.Contains("'n'", error.Message);
    }

    [Theory]
    [MemberData(nameof(Whole))]
    public async Task Gateway_WholeValue_Converts(object value, int expected)
    {
        var (factory, context) = Create();
        await using var _ = context;
        var gateway = new TableGateway<Row, int>(context);
        factory.EnqueueReaderResult(new[] { Values(value) });
        await using var sc = context.CreateSqlContainer("SELECT id, n, big FROM t");

        var row = await gateway.LoadSingleAsync(sc);

        Assert.Equal(expected, row!.N);
        Assert.Equal(expected, row.Big);
    }

    // DataReaderMapper treats it as any unconvertible value (an overflow, say): Strict fails naming the
    // column, the lenient default logs and leaves the property at its default, never 2 or 3.
    [Theory]
    [MemberData(nameof(Fractional))]
    public async Task DataReaderMapper_Strict_FractionalValue_Fails(object value)
    {
        var (factory, context) = Create();
        await using var _ = context;
        factory.EnqueueReaderResult(new[] { Values(value) });
        await using var sc = context.CreateSqlContainer("SELECT id, n, big FROM t");
        await using var reader = await sc.ExecuteReaderAsync();

        var error = await Assert.ThrowsAsync<DataMappingException>(
            () => DataReaderMapper.LoadAsync<Plain>(reader, new MapperOptions(Strict: true)).AsTask());

        Assert.Contains("'n'", error.Message);
    }

    [Theory]
    [MemberData(nameof(Fractional))]
    public async Task DataReaderMapper_Lenient_FractionalValue_LeavesTheDefault(object value)
    {
        var (factory, context) = Create();
        await using var _ = context;
        factory.EnqueueReaderResult(new[] { Values(value) });
        await using var sc = context.CreateSqlContainer("SELECT id, n, big FROM t");
        await using var reader = await sc.ExecuteReaderAsync();

        var rows = await DataReaderMapper.LoadAsync<Plain>(reader, MapperOptions.Default);

        Assert.Equal(0, rows[0].N);
        Assert.Null(rows[0].Big);
    }

    [Theory]
    [MemberData(nameof(Whole))]
    public async Task DataReaderMapper_WholeValue_Converts(object value, int expected)
    {
        var (factory, context) = Create();
        await using var _ = context;
        factory.EnqueueReaderResult(new[] { Values(value) });
        await using var sc = context.CreateSqlContainer("SELECT id, n, big FROM t");
        await using var reader = await sc.ExecuteReaderAsync();

        var rows = await DataReaderMapper.LoadAsync<Plain>(reader, MapperOptions.Default);

        Assert.Equal(expected, rows[0].N);
        Assert.Equal(expected, rows[0].Big);
    }

    [Theory]
    [MemberData(nameof(Fractional))]
    public async Task Scalar_FractionalValue_Fails(object value)
    {
        var (factory, context) = Create();
        await using var _ = context;
        factory.EnqueueReaderResult(new[] { new Dictionary<string, object> { ["v"] = value } });
        await using var sc = context.CreateSqlContainer("SELECT v");

        await Assert.ThrowsAsync<InvalidCastException>(() => sc.ExecuteScalarRequiredAsync<int>().AsTask());
    }

    [Theory]
    [MemberData(nameof(Whole))]
    public async Task Scalar_WholeValue_Converts(object value, int expected)
    {
        var (factory, context) = Create();
        await using var _ = context;
        factory.EnqueueReaderResult(new[] { new Dictionary<string, object> { ["v"] = value } });
        await using var sc = context.CreateSqlContainer("SELECT v");

        Assert.Equal(expected, await sc.ExecuteScalarRequiredAsync<int>());
    }

    // An enum stored as a number is an integer too: 2.7 is neither Ok nor Happy.
    [Theory]
    [InlineData(2.7)]
    [InlineData(2.5)]
    public async Task Gateway_FractionalValueIntoAnEnum_Fails(double stored)
    {
        var (factory, context) = Create();
        await using var _ = context;
        var gateway = new TableGateway<MoodRow, int>(context);
        factory.EnqueueReaderResult(new[] { new Dictionary<string, object> { ["id"] = 1, ["mood"] = (decimal)stored } });
        await using var sc = context.CreateSqlContainer("SELECT id, mood FROM t");

        var error = await Assert.ThrowsAsync<DataMappingException>(() => gateway.LoadSingleAsync(sc).AsTask());

        Assert.Contains("'mood'", error.Message);
    }

    [Fact]
    public async Task Gateway_WholeValueIntoAnEnum_Converts()
    {
        var (factory, context) = Create();
        await using var _ = context;
        var gateway = new TableGateway<MoodRow, int>(context);
        factory.EnqueueReaderResult(new[] { new Dictionary<string, object> { ["id"] = 1, ["mood"] = 2.0m } });
        await using var sc = context.CreateSqlContainer("SELECT id, mood FROM t");

        Assert.Equal(Mood.Ok, (await gateway.LoadSingleAsync(sc))!.Mood);
    }

    [Theory]
    [InlineData(2.7)]
    [InlineData(2.5)]
    public async Task Scalar_FractionalValueIntoAnEnum_Fails(double stored)
    {
        var (factory, context) = Create();
        await using var _ = context;
        factory.EnqueueReaderResult(new[] { new Dictionary<string, object> { ["v"] = (decimal)stored } });
        await using var sc = context.CreateSqlContainer("SELECT v");

        await Assert.ThrowsAsync<ArgumentException>(() => sc.ExecuteScalarRequiredAsync<Mood>().AsTask());
    }

    [Fact]
    public async Task Scalar_WholeValueIntoAnEnum_Converts()
    {
        var (factory, context) = Create();
        await using var _ = context;
        factory.EnqueueReaderResult(new[] { new Dictionary<string, object> { ["v"] = 3.0m } });
        await using var sc = context.CreateSqlContainer("SELECT v");

        Assert.Equal(Mood.Happy, await sc.ExecuteScalarRequiredAsync<Mood>());
    }
}
