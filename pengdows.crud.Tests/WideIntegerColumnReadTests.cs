using System;
using System.Collections.Generic;
using System.Data;
using System.Numerics;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Found live on Snowflake (2026-10-02): Snowflake.Data reports every scale-0 NUMBER as Int64, and
/// GetValue/GetInt64/GetFieldValue throw OverflowException for a value beyond Int64 ("Use GetString()
/// to handle very large values"), so a ulong at ulong.MaxValue was written but could not be read. A
/// column reported as Int64 whose value overflows Int64 is read from its text, then converted with
/// the usual checked casts: a property that can't hold it still throws DataMappingException.
/// </summary>
public sealed class WideIntegerColumnReadTests
{
    private const string ULongMax = "18446744073709551615";
    private const string Nines38 = "99999999999999999999999999999999999999";

    [Fact]
    public void FakeDbReader_EmulatesSnowflakeScaleZeroNumber()
    {
        var reader = Reader(ULongMax, "42");

        Assert.True(reader.Read());
        Assert.Equal(typeof(long), reader.GetFieldType(1));
        Assert.False(reader.IsDBNull(1));
        var overflow = Assert.Throws<OverflowException>(() => reader.GetValue(1));
        Assert.Contains("GetString()", overflow.Message);
        Assert.Throws<OverflowException>(() => reader.GetInt64(1));
        Assert.Throws<OverflowException>(() => reader.GetFieldValue<long>(1));
        Assert.Equal(ULongMax, reader.GetString(1));
        Assert.Equal(18446744073709551615m, reader.GetDecimal(1));
        Assert.Equal(42L, reader.GetValue(2));
        Assert.Equal(42L, reader.GetInt64(2));
    }

    [Fact]
    public async Task RetrieveOneAsync_ValuesBeyondInt64_HydrateWideProperties()
    {
        var (context, exec) = Context();
        await using var _ = context;
        exec.EnqueueReaderResult(Reader(ULongMax, "42", Nines38, ULongMax, ULongMax));

        var row = await new TableGateway<WideRow, int>(context).RetrieveOneAsync(1);

        Assert.Equal(ulong.MaxValue, row!.ULong);
        Assert.Equal(42L, row.Long);
        Assert.Equal(BigInteger.Parse(Nines38), row.Big);
        Assert.Equal(18446744073709551615m, row.Dec);
        Assert.Equal((UInt128)ulong.MaxValue, row.U128);
    }

    [Fact]
    public async Task RetrieveOneAsync_ValueBeyondTheProperty_ThrowsDataMappingException()
    {
        var (context, exec) = Context();
        await using var _ = context;
        exec.EnqueueReaderResult(Reader("1", ULongMax, "1", "1", "1"));

        var ex = await Assert.ThrowsAsync<DataMappingException>(async () =>
            await new TableGateway<WideRow, int>(context).RetrieveOneAsync(1));

        Assert.Contains("long_value", ex.Message);
    }

    [Fact]
    public async Task DataReaderMapper_ValuesBeyondInt64_HydrateWideProperties()
    {
        var (context, exec) = Context();
        await using var _ = context;
        exec.EnqueueReaderResult(Reader(ULongMax, "42", Nines38, ULongMax, ULongMax));
        await using var sc = context.CreateSqlContainer("SELECT * FROM wide_rows");
        await using var reader = await sc.ExecuteReaderAsync();

        var row = Assert.Single(await DataReaderMapper.LoadAsync<WideRow>(reader, new MapperOptions(Strict: true, ColumnsOnly: true)));

        Assert.Equal(ulong.MaxValue, row.ULong);
        Assert.Equal(42L, row.Long);
        Assert.Equal(BigInteger.Parse(Nines38), row.Big);
        Assert.Equal(18446744073709551615m, row.Dec);
        Assert.Equal((UInt128)ulong.MaxValue, row.U128);
    }

    [Fact]
    public async Task TrackedReader_GetValue_ValueBeyondInt64_ReturnsBigInteger()
    {
        var (context, exec) = Context();
        await using var _ = context;
        exec.EnqueueReaderResult(Reader(ULongMax, "42"));
        await using var sc = context.CreateSqlContainer("SELECT * FROM wide_rows");
        await using var reader = await sc.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(BigInteger.Parse(ULongMax), reader.GetValue(1));
        Assert.Equal(42L, reader.GetValue(2));
    }

    [Theory]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(decimal?))]
    [InlineData(typeof(double))]
    [InlineData(typeof(ulong))]
    public void ResolvedCoercer_BigIntegerIntoARegisteredNumericTarget_ConvertsExactly(Type target)
    {
        var coerce = TypeCoercionHelper.ResolveCoercer(typeof(object), target, EnumParseFailureMode.Throw);

        var result = coerce(BigInteger.Parse(ULongMax));

        Assert.Equal(Convert.ChangeType(18446744073709551615m, Nullable.GetUnderlyingType(target) ?? target),
            result);
    }

    [Fact]
    public void ResolvedCoercer_BigIntegerBeyondTheTarget_ThrowsOverflow()
    {
        var coerce = TypeCoercionHelper.ResolveCoercer(typeof(object), typeof(decimal), EnumParseFailureMode.Throw);

        Assert.Throws<OverflowException>(() => coerce(BigInteger.Parse(Nines38)));
    }

    private static fakeDbDataReader Reader(string ulongValue, string longValue, string big = "1",
        string dec = "1", string u128 = "1") =>
        new(new[]
        {
            new Dictionary<string, object>
            {
                ["id"] = 1, ["ulong_value"] = ulongValue, ["long_value"] = longValue, ["big_value"] = big,
                ["dec_value"] = dec, ["u128_value"] = u128
            }
        })
        {
            Int64TextColumns = new HashSet<string> { "ulong_value", "long_value", "big_value", "dec_value", "u128_value" }
        };

    private static (DatabaseContext Context, fakeDbConnection Exec) Context()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Snowflake);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Snowflake });
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Snowflake };
        factory.Connections.Add(exec);
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "account=x;user=y;EmulatedProduct=Snowflake",
            DbMode = DbMode.Standard
        }, factory);
        return (context, exec);
    }

    [Table("wide_rows")]
    private sealed class WideRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("ulong_value", DbType.UInt64)] public ulong ULong { get; set; }
        [Column("long_value", DbType.Int64)] public long Long { get; set; }
        [Column("big_value", DbType.Object)] public BigInteger Big { get; set; }
        [Column("dec_value", DbType.Decimal)] public decimal Dec { get; set; }
        [Column("u128_value", DbType.Object)] public UInt128 U128 { get; set; }
    }
}
