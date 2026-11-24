using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.types.coercion;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// The edges of the numeric and JSON read rules: a decimal into a float, the boundaries of the
/// digit warning, NaN and out-of-range doubles, and a NULL into a non-nullable JSON value type.
/// </summary>
public class ReadNarrowingEdgeTests
{
    private sealed class CountingLogger : ILogger
    {
        public int PrecisionWarnings;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning && formatter(state, exception).Contains("precision", StringComparison.Ordinal))
            {
                System.Threading.Interlocked.Increment(ref PrecisionWarnings);
            }
        }
    }

    private static int WarningsFor(Func<object?> read)
    {
        var logger = new CountingLogger();
        var original = TypeCoercionHelper.Logger;
        TypeCoercionHelper.Logger = logger;
        try
        {
            read();
            return logger.PrecisionWarnings;
        }
        finally
        {
            TypeCoercionHelper.Logger = original;
        }
    }

    [Fact]
    public void DecimalToFloat_LosingDigits_LogsAPrecisionWarningAndConverts()
    {
        object? value = null;
        var warnings = WarningsFor(() => value = TypeCoercionHelper.Coerce(1.23456789m, typeof(decimal), typeof(float)));

        Assert.Equal(1.23456789f, (float)value!);
        Assert.Equal(1, warnings);
    }

    [Fact]
    public void DecimalToFloat_ExactlyRepresentable_LogsNothing()
    {
        var warnings = WarningsFor(() => TypeCoercionHelper.Coerce(0.25m, typeof(decimal), typeof(float)));

        Assert.Equal(0, warnings);
    }

    // A double keeps 15 significant digits: a decimal that has no more than that survives, so it is not reported.
    [Fact]
    public void DecimalToDouble_WithFifteenSignificantDigits_LogsNothing()
    {
        var warnings = WarningsFor(() => TypeCoercionHelper.Coerce(1.23456789012345m, typeof(decimal), typeof(double)));

        Assert.Equal(0, warnings);
    }

    [Fact]
    public void DoubleToDecimal_NaN_IsRefused()
    {
        Assert.Throws<InvalidCastException>(() => ConverterResolution.ConvertWithCache(double.NaN, typeof(decimal)));
    }

    [Fact]
    public void DoubleToDecimal_BeyondDecimalRange_IsRefusedNamingTheValue()
    {
        var error = Assert.Throws<InvalidCastException>(() => ConverterResolution.ConvertWithCache(1e30d, typeof(decimal)));

        Assert.Contains("1E+30", error.Message);
    }

    [Table("null_json")]
    public sealed class ValueRow
    {
        [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("doc", DbType.Object)] public JsonValue Doc { get; set; }
    }

    // A NULL into a non-nullable value type fails loudly, naming the column, on the gateway as on DataReaderMapper.
    [Fact]
    public async Task JsonValueColumn_Null_FailsNamingTheColumn()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        await using var context = new DatabaseContext("Data Source=test;EmulatedProduct=PostgreSql", factory);
        var gateway = new TableGateway<ValueRow, int>(context);
        factory.EnqueueReaderResult(new[] { new Dictionary<string, object> { ["id"] = 1, ["doc"] = DBNull.Value } });
        await using var sc = context.CreateSqlContainer("SELECT id, doc FROM null_json");

        var error = await Assert.ThrowsAsync<DataMappingException>(() => gateway.LoadSingleAsync(sc).AsTask());

        Assert.Contains("'doc'", error.Message);
    }
}
