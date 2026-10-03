using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-008, probed live 2026-09-30 with 9e28 (above decimal.MaxValue) in each database's widest
/// exact numeric column, read into a decimal property: no provider truncated, but each failed
/// differently — Npgsql/MySqlConnector/FirebirdClient/SQLite/AseClient OverflowException, ODP.NET
/// InvalidCastException, SingleStore (MySqlConnector) FormatException, and Informix.Net.Core
/// returned null from GetValue (IsDBNull throws, GetDecimal throws NullReferenceException). Gateway
/// hydration now reports every value-conversion failure as DataMappingException naming the column,
/// and Informix's null is turned into an OverflowException instead of a silent NULL/default.
/// </summary>
public sealed class ValueOutOfRangeMappingTests
{
    public static TheoryData<Exception> ProviderConversionFailures() => new()
    {
        new OverflowException("Value was either too large or too small for a Decimal."),
        new InvalidCastException("Specified cast is not valid."),
        new FormatException("One of the identified items was in an invalid format.")
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

    [Fact]
    public void FakeDbReader_CanEmulateColumnReadFailures()
    {
        var failing = new fakeDbDataReader(new[] { new Dictionary<string, object> { ["amount"] = 1m } })
        {
            ColumnReadExceptions = new Dictionary<string, Exception> { ["amount"] = new OverflowException("x") }
        };
        Assert.True(failing.Read());
        Assert.Equal(typeof(decimal), failing.GetFieldType(0)); // metadata never fails
        Assert.Throws<OverflowException>(() => failing.GetValue(0));
        Assert.Throws<OverflowException>(() => failing.GetDecimal(0));

        var informix = new fakeDbDataReader(new[] { new Dictionary<string, object> { ["amount"] = 1m } })
        {
            OutOfRangeReturnsNullColumns = new HashSet<string> { "amount" }
        };
        Assert.True(informix.Read());
        Assert.Null(informix.GetValue(0));
        Assert.Throws<OverflowException>(() => informix.IsDBNull(0));
        Assert.Throws<NullReferenceException>(() => informix.GetDecimal(0));
    }

    [Theory]
    [MemberData(nameof(ProviderConversionFailures))]
    public async Task RetrieveOneAsync_ValueTheProviderCantConvert_ThrowsDataMappingExceptionNamingTheColumn(Exception failure)
    {
        var (context, exec) = Context(SupportedDatabase.PostgreSql);
        await using var _ = context;
        exec.EnqueueReaderResult(new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["id"] = 1, ["amount"] = 1m }
        })
        {
            ColumnReadExceptions = new Dictionary<string, Exception> { ["amount"] = failure }
        });
        var gateway = new TableGateway<Account, int>(context);

        var ex = await Assert.ThrowsAsync<DataMappingException>(async () => await gateway.RetrieveOneAsync(1));

        Assert.Same(failure, ex.InnerException);
        Assert.Contains("amount", ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(Account.Amount), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetrieveOneAsync_OnInformix_OutOfRangeDecimal_ThrowsDataMappingExceptionNotADefault()
    {
        var (context, exec) = Context(SupportedDatabase.Informix);
        await using var _ = context;
        exec.EnqueueReaderResult(new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["id"] = 1, ["amount"] = 1m }
        })
        {
            OutOfRangeReturnsNullColumns = new HashSet<string> { "amount" }
        });
        var gateway = new TableGateway<Account, int>(context);

        var ex = await Assert.ThrowsAsync<DataMappingException>(async () => await gateway.RetrieveOneAsync(1));

        Assert.IsType<OverflowException>(ex.InnerException);
        Assert.Contains("amount", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetrieveOneAsync_NonConversionReaderFailure_IsNotReportedAsAMappingProblem()
    {
        var (context, exec) = Context(SupportedDatabase.PostgreSql);
        await using var _ = context;
        exec.EnqueueReaderResult(new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["id"] = 1, ["amount"] = 1m }
        })
        {
            ColumnReadExceptions = new Dictionary<string, Exception> { ["amount"] = new ObjectDisposedException("reader") }
        });
        var gateway = new TableGateway<Account, int>(context);

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await gateway.RetrieveOneAsync(1));
    }

    // FirebirdClient and AdoNetCore.AseClient decode the whole row inside Read/ReadAsync, so an
    // out-of-range value fails there, before any mapping (confirmed live 2026-09-30).
    [Theory]
    [MemberData(nameof(ProviderConversionFailures))]
    public async Task LoadListAsync_ProviderFailsToDecodeTheRowDuringRead_ThrowsDataMappingException(Exception failure)
    {
        var (context, exec) = Context(SupportedDatabase.Firebird);
        await using var _ = context;
        exec.EnqueueReaderResult(new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["id"] = 1, ["amount"] = 1m }
        })
        {
            FailAfterReadCount = 0,
            FailException = failure
        });
        var gateway = new TableGateway<Account, int>(context);
        await using var sc = gateway.BuildBaseRetrieve("a");

        var ex = await Assert.ThrowsAsync<DataMappingException>(async () => await gateway.LoadListAsync(sc));

        Assert.Same(failure, ex.InnerException);
    }

    // AdoNetCore.AseClient decodes every result row while executing the command, so an out-of-range
    // value fails inside ExecuteReader (confirmed live 2026-09-30); Sybase declares it.
    [Fact]
    public async Task LoadListAsync_OnSybase_RowDecodeOverflowDuringExecute_ThrowsDataMappingException()
    {
        var (context, exec) = Context(SupportedDatabase.SybaseASE);
        await using var _ = context;
        var gateway = new TableGateway<Account, int>(context);
        await using var sc = gateway.BuildBaseRetrieve("a");
        var failure = new OverflowException("Value was either too large or too small for a Decimal.");
        exec.SetCommandFailure(sc.Query.ToString(), failure);

        var ex = await Assert.ThrowsAsync<DataMappingException>(async () => await gateway.LoadListAsync(sc));

        Assert.Same(failure, ex.InnerException);
    }

    [Fact]
    public async Task ExecuteOverflow_OnADialectThatDoesNotDecodeRowsAtExecute_IsNotReportedAsMapping()
    {
        var (context, exec) = Context(SupportedDatabase.SqlServer);
        await using var _ = context;
        var gateway = new TableGateway<Account, int>(context);
        await using var sc = gateway.BuildBaseRetrieve("a");
        exec.SetCommandFailure(sc.Query.ToString(), new OverflowException("SqlDbType.Time overflow."));

        var ex = await Record.ExceptionAsync(async () => await gateway.LoadListAsync(sc));

        Assert.NotNull(ex);
        Assert.IsNotType<DataMappingException>(ex);
    }

    // REV-034: array text that isn't an array surfaced as a raw JsonException. (A [Json] column
    // holding invalid JSON still reads as null by 2.0 design; REV-042 asks whether to change that.)
    [Theory]
    [InlineData("nums", "garbage")]
    [InlineData("nums", "{\"a\":1}")]
    public async Task RetrieveOneAsync_TextThatDoesNotParse_ThrowsDataMappingExceptionNamingTheColumn(
        string column, string stored)
    {
        var (context, exec) = Context(SupportedDatabase.Sqlite);
        await using var _ = context;
        var row = new Dictionary<string, object?> { ["id"] = 1, ["nums"] = "[1]", ["doc"] = "{}" };
        row[column] = stored;
        exec.EnqueueReaderResult(new[] { row });
        var gateway = new TableGateway<Parsed, int>(context);

        var ex = await Assert.ThrowsAsync<DataMappingException>(async () => await gateway.RetrieveOneAsync(1));

        Assert.Contains(column, ex.Message, StringComparison.Ordinal);
    }

    [Table("parsed")]
    private sealed class Parsed
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("nums", DbType.Object)] public int[]? Nums { get; set; }
        [Json] [Column("doc", DbType.String)] public Dictionary<string, int>? Doc { get; set; }
    }

    [Table("accounts")]
    private sealed class Account
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("amount", DbType.Decimal)] public decimal Amount { get; set; }
    }
}
