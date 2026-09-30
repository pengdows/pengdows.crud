using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using FirebirdSql.Data.Types;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-005, probed live 2026-09-30. DuckDB.NET and FirebirdClient read HUGEINT/UHUGEINT/INT128 as
/// BigInteger, DuckDB.NET reads a LIST as List&lt;T&gt;, and FirebirdClient reads DECFLOAT as its own
/// FbDecFloat; none reached Int128/UInt128/T[]/decimal. Both providers want BigInteger for a
/// 128-bit parameter. A MySQL zero date (MySqlConversionException) and a SQL Server hierarchyid
/// without Microsoft.SqlServer.Types (GetFieldType null, GetValue FileNotFoundException) escaped
/// as raw provider/runtime exceptions.
/// </summary>
public sealed class ExoticStoredValueMappingTests
{
    private static readonly BigInteger Int128Max = BigInteger.Parse("170141183460469231731687303715884105727");
    private static readonly BigInteger UInt128Max = BigInteger.Parse("340282366920938463463374607431768211455");

    [Fact]
    public void BigInteger_CoercesExactlyToEveryIntegralTypeThatHoldsIt()
    {
        Assert.Equal(Int128.MaxValue, TypeCoercionHelper.Coerce(Int128Max, typeof(BigInteger), typeof(Int128)));
        Assert.Equal(Int128.MinValue, TypeCoercionHelper.Coerce(-Int128Max - 1, typeof(BigInteger), typeof(Int128)));
        Assert.Equal(UInt128.MaxValue, TypeCoercionHelper.Coerce(UInt128Max, typeof(BigInteger), typeof(UInt128?)));
        Assert.Equal(long.MaxValue, TypeCoercionHelper.Coerce(new BigInteger(long.MaxValue), typeof(BigInteger), typeof(long)));
        Assert.Equal(42, TypeCoercionHelper.Coerce(new BigInteger(42), typeof(BigInteger), typeof(int)));
        Assert.Equal(79228162514264337593543950335m, TypeCoercionHelper.Coerce(new BigInteger(decimal.MaxValue), typeof(BigInteger), typeof(decimal)));
        Assert.Equal(Int128Max.ToString(), TypeCoercionHelper.Coerce(Int128Max, typeof(BigInteger), typeof(string)));
    }

    [Fact]
    public void BigInteger_OutsideTheTargetRange_Throws()
    {
        Assert.Throws<OverflowException>(() => TypeCoercionHelper.Coerce(Int128Max + 1, typeof(BigInteger), typeof(Int128)));
        Assert.Throws<OverflowException>(() => TypeCoercionHelper.Coerce(BigInteger.MinusOne, typeof(BigInteger), typeof(UInt128)));
        Assert.Throws<OverflowException>(() => TypeCoercionHelper.Coerce(new BigInteger(long.MaxValue) + 1, typeof(BigInteger), typeof(long)));
    }

    [Fact]
    public void WideIntegers_CoerceToBigIntegerAndBetweenEachOther()
    {
        Assert.Equal(Int128Max, TypeCoercionHelper.Coerce(Int128.MaxValue, typeof(Int128), typeof(BigInteger)));
        Assert.Equal(new BigInteger(7), TypeCoercionHelper.Coerce(7L, typeof(long), typeof(BigInteger)));
        Assert.Equal((Int128)5, TypeCoercionHelper.Coerce(5L, typeof(long), typeof(Int128)));
        Assert.Equal((UInt128)5, TypeCoercionHelper.Coerce(5m, typeof(decimal), typeof(UInt128)));
    }

    [Fact]
    public void Sequences_CoerceElementWiseToArraysAndLists()
    {
        Assert.Equal(new[] { 1, 2, 3 }, TypeCoercionHelper.Coerce(new List<int> { 1, 2, 3 }, typeof(List<int>), typeof(int[])));
        Assert.Equal(new List<string> { "a", "b" }, TypeCoercionHelper.Coerce(new[] { "a", "b" }, typeof(string[]), typeof(List<string>)));
        Assert.Equal(new long[] { 1, 2 }, TypeCoercionHelper.Coerce(new List<object> { 1, 2L }, typeof(List<object>), typeof(long[])));
        Assert.Equal(new List<int?> { 1, null }, TypeCoercionHelper.Coerce(new List<object?> { 1, null }, typeof(List<object>), typeof(List<int?>)));
        Assert.Throws<InvalidCastException>(() => TypeCoercionHelper.Coerce(new List<object> { "x" }, typeof(List<object>), typeof(int[])));
    }

    [Fact]
    public void FirebirdDecFloat_CoercesExactlyToDecimalAndDouble()
    {
        // DataReaderMapper coerces with default options, so this must not depend on the provider.
        var firebird = TypeCoercionOptions.Default;
        Assert.Equal(12345.6789m, TypeCoercionHelper.Coerce((FbDecFloat)12345.6789m, typeof(FbDecFloat), typeof(decimal), firebird));
        Assert.Equal(decimal.MaxValue, TypeCoercionHelper.Coerce(new FbDecFloat(new BigInteger(decimal.MaxValue), 0), typeof(FbDecFloat), typeof(decimal), firebird));
        Assert.Equal(100m, TypeCoercionHelper.Coerce(new FbDecFloat(1, 2), typeof(FbDecFloat), typeof(decimal), firebird));
        Assert.Equal(1.5m, TypeCoercionHelper.Coerce(new FbDecFloat(BigInteger.Parse("15000000000000000000000000000000000"), -34), typeof(FbDecFloat), typeof(decimal), firebird));
        Assert.Equal(double.NaN, TypeCoercionHelper.Coerce(FbDecFloat.PositiveNaN, typeof(FbDecFloat), typeof(double), firebird));
        Assert.Equal(double.NegativeInfinity, TypeCoercionHelper.Coerce(FbDecFloat.NegativeInfinity, typeof(FbDecFloat), typeof(double), firebird));
        // 34 significant digits: decimal would round it, so it is refused rather than altered.
        Assert.Throws<OverflowException>(() => TypeCoercionHelper.Coerce(new FbDecFloat(BigInteger.Parse("1234567890123456789012345678901234"), -4), typeof(FbDecFloat), typeof(decimal), firebird));
        Assert.Equal(-0.5d, TypeCoercionHelper.Coerce((FbDecFloat)(-0.5m), typeof(FbDecFloat), typeof(double), firebird));
        Assert.ThrowsAny<Exception>(() => TypeCoercionHelper.Coerce(FbDecFloat.PositiveNaN, typeof(FbDecFloat), typeof(decimal), firebird));
        Assert.Throws<OverflowException>(() => TypeCoercionHelper.Coerce(new FbDecFloat(1, 6000), typeof(FbDecFloat), typeof(decimal), firebird));
    }

    [Theory]
    [InlineData(SupportedDatabase.Firebird)]
    [InlineData(SupportedDatabase.PostgreSql)]
    public void Int128AndUInt128Parameters_AreBoundAsBigInteger(SupportedDatabase database)
    {
        using var context = new DatabaseContext($"Data Source=x;EmulatedProduct={database}", new fakeDbFactory(database));
        Assert.Equal(Int128Max, context.CreateDbParameter("p", DbType.Object, Int128.MaxValue).Value);
        Assert.Equal(UInt128Max, context.CreateDbParameter("p", DbType.Object, (UInt128?)UInt128.MaxValue).Value);
    }

    // DuckDB.NET binds BigInteger only within ±(2^127 - 1): Int128.MinValue and UInt128 above
    // Int128.MaxValue throw ArgumentOutOfRangeException. DuckDB casts exact text to HUGEINT/UHUGEINT.
    [Fact]
    public void Int128AndUInt128Parameters_OnDuckDb_AreBoundAsExactText()
    {
        using var context = new DatabaseContext("Data Source=x;EmulatedProduct=DuckDB", new fakeDbFactory(SupportedDatabase.DuckDB));
        var min = context.CreateDbParameter("p", DbType.Object, Int128.MinValue);
        Assert.Equal("-170141183460469231731687303715884105728", min.Value);
        Assert.Equal(DbType.String, min.DbType);
        Assert.Equal(UInt128Max.ToString(), context.CreateDbParameter("p", DbType.Object, UInt128.MaxValue).Value);
    }

    // FirebirdClient binds a DECFLOAT parameter only from FbDecFloat (decimal, double and string all
    // throw InvalidCastException) and rejects FbDecFloat for NUMERIC, so a DECFLOAT column is
    // declared DbType.VarNumeric, which FirebirdClient itself doesn't accept ("Invalid data type").
    [Fact]
    public void VarNumericParameter_OnFirebird_IsBoundAsFbDecFloat()
    {
        using var context = new DatabaseContext("Data Source=x;EmulatedProduct=Firebird", new fakeDbFactory(SupportedDatabase.Firebird));

        var parameter = context.CreateDbParameter("p", DbType.VarNumeric, 12345.6789m);

        Assert.Equal(new FbDecFloat(123456789, -4), Assert.IsType<FbDecFloat>(parameter.Value));
        // FirebirdClient's own (FbDecFloat)decimal throws for integral values such as 100m.
        Assert.Equal(new FbDecFloat(100, 0), context.CreateDbParameter("p", DbType.VarNumeric, 100m).Value);
        Assert.Equal(new FbDecFloat(-15, -1), context.CreateDbParameter("p", DbType.VarNumeric, -1.5d).Value);
        Assert.Equal(new FbDecFloat(BigInteger.Parse("-79228162514264337593543950335"), 0),
            context.CreateDbParameter("p", DbType.VarNumeric, decimal.MinValue).Value);
        Assert.Equal(DbType.Object, parameter.DbType);
        Assert.Equal(DbType.Decimal, context.CreateDbParameter("p", DbType.Decimal, 1m).DbType);
    }

    // Stands in for MySql.Data's / MySqlConnector's MySqlConversionException (zero dates).
    private sealed class MySqlConversionException(string message) : Exception(message);

    public static TheoryData<SupportedDatabase, Exception> UnreadableValues() => new()
    {
        { SupportedDatabase.MySql, new MySqlConversionException("Unable to convert MySQL date/time value to System.DateTime") },
        { SupportedDatabase.MariaDb, new MySqlConversionException("Unable to convert MySQL date/time value to System.DateTime") },
        { SupportedDatabase.SqlServer, new FileNotFoundException("Could not load file or assembly 'Microsoft.SqlServer.Types, Version=10.0.0.0'.", "Microsoft.SqlServer.Types, Version=10.0.0.0, Culture=neutral, PublicKeyToken=89845dcd8080cc91") },
    };

    private static (DatabaseContext Context, fakeDbConnection Exec) Context(SupportedDatabase database)
    {
        var factory = new fakeDbFactory(database);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = database });
        var exec = new fakeDbConnection { EmulatedProduct = database };
        factory.Connections.Add(exec);
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = $"Server=x;Database=y;EmulatedProduct={database}",
            DbMode = DbMode.Standard
        }, factory);
        return (context, exec);
    }

    private static fakeDbDataReader UnreadableReader(Exception failure) =>
        new(new[] { new Dictionary<string, object> { ["id"] = 1, ["v"] = "stored" } })
        {
            ColumnReadExceptions = new Dictionary<string, Exception> { ["v"] = failure },
            UnresolvedFieldTypeColumns = new HashSet<string> { "v" }
        };

    [Fact]
    public void FakeDbReader_CanEmulateAColumnWhoseFieldTypeIsUnresolved()
    {
        var reader = UnreadableReader(new InvalidOperationException());
        Assert.True(reader.Read());
        Assert.Null(reader.GetFieldType(1));
        Assert.Equal(typeof(int), reader.GetFieldType(0));
    }

    [Theory]
    [MemberData(nameof(UnreadableValues))]
    public async Task RetrieveOneAsync_StoredValueWithNoDotNetRepresentation_ThrowsDataMappingException(
        SupportedDatabase database, Exception failure)
    {
        var (context, exec) = Context(database);
        await using var _ = context;
        exec.EnqueueReaderResult(UnreadableReader(failure));
        var gateway = new TableGateway<Row, int>(context);

        var ex = await Assert.ThrowsAsync<DataMappingException>(async () => await gateway.RetrieveOneAsync(1));

        Assert.Same(failure, ex.InnerException);
        Assert.Contains("'v'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetrieveOneAsync_ThatFailureOnADatabaseThatDoesNotRaiseIt_IsNotReportedAsAMappingProblem()
    {
        var (context, exec) = Context(SupportedDatabase.PostgreSql);
        await using var _ = context;
        exec.EnqueueReaderResult(UnreadableReader(new MySqlConversionException("x")));
        var gateway = new TableGateway<Row, int>(context);

        await Assert.ThrowsAsync<MySqlConversionException>(async () => await gateway.RetrieveOneAsync(1));
    }

    [Theory]
    [MemberData(nameof(UnreadableValues))]
    public async Task StrictDataReaderMapper_StoredValueWithNoDotNetRepresentation_ThrowsDataMappingException(
        SupportedDatabase database, Exception failure)
    {
        var (context, exec) = Context(database);
        await using var _ = context;
        exec.EnqueueReaderResult(UnreadableReader(failure));
        await using var sc = context.CreateSqlContainer("SELECT id, v FROM t");
        await using var reader = await sc.ExecuteReaderAsync();

        await Assert.ThrowsAsync<DataMappingException>(async () =>
            await DataReaderMapper.LoadAsync<Row>(reader, new MapperOptions(Strict: true)));
    }

    // FbDecFloat conversion is keyed on the value's type, not the provider, so it holds on every
    // read path, DataReaderMapper (no provider options) included.
    [Fact]
    public async Task RetrieveOneAsync_FirebirdDecFloatColumn_HydratesADecimalProperty()
    {
        var (context, exec) = Context(SupportedDatabase.Firebird);
        await using var _ = context;
        exec.EnqueueReaderResult(new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["id"] = 1, ["amount"] = new FbDecFloat(123456789, -4) }
        }));
        var gateway = new TableGateway<DecFloatRow, int>(context);

        var row = await gateway.RetrieveOneAsync(1);

        Assert.Equal(12345.6789m, row!.Amount);
    }

    [Table("t")]
    private sealed class DecFloatRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("amount", DbType.VarNumeric)] public decimal Amount { get; set; }
    }

    [Table("t")]
    private sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.String)] public string? V { get; set; }
    }
}
