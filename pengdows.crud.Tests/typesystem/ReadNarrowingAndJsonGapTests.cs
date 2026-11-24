using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// Read-side rules the centralization work must hold on every route: a JSON column stored as bytes
/// reads into every JSON type, a narrowing read keeps the exact value when it fits and names the
/// value when it doesn't, a double keeps the digits it carries when it becomes a decimal, and a
/// decimal that loses digits on the way to a double is reported as a warning.
/// </summary>
public class ReadNarrowingAndJsonGapTests
{
    [Table("json_gap")]
    public sealed class ElementRow
    {
        [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("doc", DbType.Object)] public JsonElement Doc { get; set; }
    }

    [Table("json_gap")]
    public sealed class ValueRow
    {
        [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("doc", DbType.Object)] public pengdows.crud.types.valueobjects.JsonValue Doc { get; set; }
    }

    [Table("json_gap")]
    public sealed class PocoRow
    {
        [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Json] [Column("doc", DbType.Object)] public Payload Doc { get; set; } = new();
    }

    public sealed class Payload
    {
        public int A { get; set; }
    }

    [Table("narrow_gap")]
    public sealed class ShortRow
    {
        [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("n", DbType.Int16)] public short N { get; set; }
    }

    [Table("narrow_gap")]
    public sealed class DoubleRow
    {
        [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("n", DbType.Double)] public double N { get; set; }
    }

    private static (fakeDbFactory Factory, DatabaseContext Context) Postgres()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        return (factory, new DatabaseContext("Data Source=test;EmulatedProduct=PostgreSql", factory));
    }

    private static Dictionary<string, object> Row(params (string Name, object Value)[] columns)
    {
        var row = new Dictionary<string, object>();
        foreach (var (name, value) in columns)
        {
            row[name] = value;
        }

        return row;
    }

    // ---- JSON value types read from a byte-valued column (CompiledMapperFactory's GetString branch) ----

    [Fact]
    public async Task JsonElementColumn_StoredAsUtf8Bytes_ReadsTheJson()
    {
        var (factory, context) = Postgres();
        await using var _ = context;
        var gateway = new TableGateway<ElementRow, int>(context);
        factory.EnqueueReaderResult(new[] { Row(("id", 1), ("doc", Encoding.UTF8.GetBytes("{\"a\":1}"))) });
        await using var sc = context.CreateSqlContainer("SELECT id, doc FROM json_gap");

        var row = await gateway.LoadSingleAsync(sc);

        Assert.Equal("{\"a\":1}", row!.Doc.GetRawText());
    }

    [Fact]
    public async Task JsonValueColumn_StoredAsUtf8Bytes_HoldsTheJsonText()
    {
        var (factory, context) = Postgres();
        await using var _ = context;
        var gateway = new TableGateway<ValueRow, int>(context);
        factory.EnqueueReaderResult(new[] { Row(("id", 1), ("doc", Encoding.UTF8.GetBytes("{\"a\":1}"))) });
        await using var sc = context.CreateSqlContainer("SELECT id, doc FROM json_gap");

        var row = await gateway.LoadSingleAsync(sc);

        Assert.Equal("{\"a\":1}", row!.Doc.AsString());
    }

    [Fact]
    public async Task ComplexJsonColumn_StoredAsUtf8Bytes_DeserializesIntoTheType()
    {
        var (factory, context) = Postgres();
        await using var _ = context;
        var gateway = new TableGateway<PocoRow, int>(context);
        factory.EnqueueReaderResult(new[] { Row(("id", 1), ("doc", Encoding.UTF8.GetBytes("{\"a\":7}"))) });
        await using var sc = context.CreateSqlContainer("SELECT id, doc FROM json_gap");

        var row = await gateway.LoadSingleAsync(sc);

        Assert.Equal(7, row!.Doc.A);
    }

    // A JSONB binary payload (SQLite's BLOB form, a PostgreSQL binary result) is not UTF-8 text. It must
    // fail naming the column, never decode into a wrong value.
    [Fact]
    public async Task ComplexJsonColumn_StoredAsBinaryJsonb_FailsNamingTheColumn()
    {
        var (factory, context) = Postgres();
        await using var _ = context;
        var gateway = new TableGateway<PocoRow, int>(context);
        factory.EnqueueReaderResult(new[] { Row(("id", 1), ("doc", new byte[] { 0x0C, 0x00, 0x00, 0x02, 0xFF })) });
        await using var sc = context.CreateSqlContainer("SELECT id, doc FROM json_gap");

        var error = await Assert.ThrowsAsync<DataMappingException>(() => gateway.LoadSingleAsync(sc).AsTask());

        Assert.Contains("'doc'", error.Message);
    }

    // ---- narrowing: a value that fits keeps its exact value; one that doesn't names itself ----

    [Fact]
    public async Task ShortColumn_FromDoubleWithinRange_KeepsTheExactValue()
    {
        var (factory, context) = Postgres();
        await using var _ = context;
        var gateway = new TableGateway<ShortRow, int>(context);
        factory.EnqueueReaderResult(new[] { Row(("id", 1), ("n", 400.0d)) });
        await using var sc = context.CreateSqlContainer("SELECT id, n FROM narrow_gap");

        var row = await gateway.LoadSingleAsync(sc);

        Assert.Equal((short)400, row!.N);
    }

    [Fact]
    public async Task ShortColumn_FromLongWithinRange_KeepsTheExactValue()
    {
        var (factory, context) = Postgres();
        await using var _ = context;
        var gateway = new TableGateway<ShortRow, int>(context);
        factory.EnqueueReaderResult(new[] { Row(("id", 1), ("n", 400L)) });
        await using var sc = context.CreateSqlContainer("SELECT id, n FROM narrow_gap");

        var row = await gateway.LoadSingleAsync(sc);

        Assert.Equal((short)400, row!.N);
    }

    [Table("dec_gap")]
    public sealed class DecimalRow
    {
        [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("n", DbType.Decimal)] public decimal N { get; set; }
    }

    // A numeric column reads through the typed numeric route, not the coercion, on the gateway: the same rules must hold there.
    [Fact]
    public async Task DecimalColumn_StoredAsDouble_KeepsTheDigitsOnTheGateway()
    {
        var (factory, context) = Postgres();
        await using var _ = context;
        var gateway = new TableGateway<DecimalRow, int>(context);
        factory.EnqueueReaderResult(new[] { Row(("id", 1), ("n", 1.2345678901234567d)) });
        await using var sc = context.CreateSqlContainer("SELECT id, n FROM dec_gap");

        var row = await gateway.LoadSingleAsync(sc);

        Assert.Equal(1.2345678901234567m, row!.N);
    }

    [Fact]
    public async Task DoubleColumn_StoredAsDecimalLosingDigits_LogsAPrecisionWarningOnTheGateway()
    {
        var (factory, context) = Postgres();
        await using var _ = context;
        var logger = new CountingLogger();
        var original = TypeCoercionHelper.Logger;
        TypeCoercionHelper.Logger = logger;
        try
        {
            var gateway = new TableGateway<DoubleRow, int>(context);
            factory.EnqueueReaderResult(new[] { Row(("id", 1), ("n", 1.23456789012345678901234567m)) });
            await using var sc = context.CreateSqlContainer("SELECT id, n FROM narrow_gap");

            var row = await gateway.LoadSingleAsync(sc);

            Assert.Equal(1.2345678901234568d, row!.N);
            Assert.Equal(1, logger.PrecisionWarnings);
        }
        finally
        {
            TypeCoercionHelper.Logger = original;
        }
    }

    [Fact]
    public async Task ShortColumn_OutOfRange_NamesTheValueAndTheTarget()
    {
        var (factory, context) = Postgres();
        await using var _ = context;
        var gateway = new TableGateway<ShortRow, int>(context);
        factory.EnqueueReaderResult(new[] { Row(("id", 1), ("n", 70000L)) });
        await using var sc = context.CreateSqlContainer("SELECT id, n FROM narrow_gap");

        var error = await Assert.ThrowsAsync<DataMappingException>(() => gateway.LoadSingleAsync(sc).AsTask());

        Assert.Contains("70000", error.Message + " " + error.InnerException?.Message);
        Assert.Contains("Int16", error.Message + " " + error.InnerException?.Message);
    }

    // ---- double -> decimal keeps the digits the double carries ----

    [Fact]
    public void DecimalFromDouble_KeepsTheDigitsTheDoubleCarries()
    {
        var result = ConverterResolution.ConvertWithCache(1.2345678901234567d, typeof(decimal));

        Assert.Equal(1.2345678901234567m, result);
    }

    [Fact]
    public void DecimalColumn_FromDouble_KeepsTheDigitsTheDoubleCarries()
    {
        var coercion = new DecimalCoercion();

        Assert.True(coercion.TryRead(new DbValue(1.2345678901234567d), out var value));
        Assert.Equal(1.2345678901234567m, value);
    }

    // ---- decimal -> double: losing digits is a warning, not silence or failure ----

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

    [Fact]
    public void DecimalToDouble_LosingDigits_LogsAPrecisionWarningAndConverts()
    {
        var logger = new CountingLogger();
        var original = TypeCoercionHelper.Logger;
        TypeCoercionHelper.Logger = logger;
        try
        {
            var value = TypeCoercionHelper.Coerce(1.23456789012345678901234567m, typeof(decimal), typeof(double));

            Assert.Equal(1.2345678901234568d, (double)value!);
            Assert.Equal(1, logger.PrecisionWarnings);
        }
        finally
        {
            TypeCoercionHelper.Logger = original;
        }
    }

    [Fact]
    public void DecimalToDouble_ExactlyRepresentable_LogsNothing()
    {
        var logger = new CountingLogger();
        var original = TypeCoercionHelper.Logger;
        TypeCoercionHelper.Logger = logger;
        try
        {
            var value = TypeCoercionHelper.Coerce(0.5m, typeof(decimal), typeof(double));

            Assert.Equal(0.5d, (double)value!);
            Assert.Equal(0, logger.PrecisionWarnings);
        }
        finally
        {
            TypeCoercionHelper.Logger = original;
        }
    }
}
