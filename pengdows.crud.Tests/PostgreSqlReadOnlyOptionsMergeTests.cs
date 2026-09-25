using System;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// CONFIRMED live (CockroachDB, during the BP-118 backport): the read-only connection string was
/// built by appending <c>;Options='-c default_transaction_read_only=on'</c>. A connection string
/// keeps only the last value of a repeated key, so a caller's own <c>Options</c> (for example
/// <c>-c lock_timeout=5s</c>) never reached read connections. The read-only setting must be merged
/// into the caller's Options instead.
/// </summary>
public class PostgreSqlReadOnlyOptionsMergeTests
{
    private const string CallerConnectionString =
        "Host=localhost;Database=db;Username=u;Password=p;Options=-c lock_timeout=5s";

    private static string OptionsOf(string connectionString)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        return builder.TryGetValue("Options", out var value) ? value as string ?? string.Empty : string.Empty;
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.CockroachDb)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    public void GetReadOnlyConnectionString_KeepsCallerOptions(SupportedDatabase database)
    {
        var dialect = (SqlDialect)SqlDialectFactory.CreateDialectForType(database, new fakeDbFactory(database),
            NullLogger.Instance);

        var options = OptionsOf(dialect.GetReadOnlyConnectionString(CallerConnectionString));

        Assert.Contains("lock_timeout=5s", options, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("default_transaction_read_only=on", options, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReaderDataSourceConnectionString_KeepsCallerOptions()
    {
        var dialect = new PostgreSqlDialect(new fakeDbFactory(SupportedDatabase.PostgreSql),
            NullLogger<PostgreSqlDialect>.Instance);

        var reader = dialect.PrepareConnectionStringForDataSource(
            dialect.GetReadOnlyConnectionString(CallerConnectionString), readOnly: true);

        var options = OptionsOf(reader);
        Assert.Contains("lock_timeout=5s", options, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("default_transaction_read_only=on", options, StringComparison.OrdinalIgnoreCase);
    }

    // The writer's baked startup Options carry default_transaction_read_only=off; the reader
    // derived from them must flip it to on, not keep the writer's value.
    [Fact]
    public void GetReadOnlyConnectionString_WriterOptionsWithReadOnlyOff_FlipsToOn()
    {
        var dialect = new PostgreSqlDialect(new fakeDbFactory(SupportedDatabase.PostgreSql),
            NullLogger<PostgreSqlDialect>.Instance);

        var options = OptionsOf(dialect.GetReadOnlyConnectionString(
            "Host=localhost;Options='-c lock_timeout=5s -c default_transaction_read_only=off'"));

        Assert.Contains("lock_timeout=5s", options, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("default_transaction_read_only=on", options, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("default_transaction_read_only=off", options, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetReadOnlyConnectionString_WithoutCallerOptions_AddsReadOnlyOption()
    {
        var dialect = new PostgreSqlDialect(new fakeDbFactory(SupportedDatabase.PostgreSql),
            NullLogger<PostgreSqlDialect>.Instance);

        var options = OptionsOf(dialect.GetReadOnlyConnectionString("Host=localhost;Database=db"));

        Assert.Contains("default_transaction_read_only=on", options, StringComparison.OrdinalIgnoreCase);
    }
}
