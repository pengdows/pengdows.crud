using System;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

/// <summary>
/// pengdows.flatfile 0.2.1-preview.1 (published) raises FlatFileException with standard SQLSTATEs for
/// read-only violations (25006), write-lock / consistent-read timeouts (HYT00), a missing database
/// location (08001) and a writer refused because another process holds the database (08004). The
/// FlatFile dialect/translator must classify them like any other provider's DbException.
/// </summary>
public class FlatFileErrorClassificationTests
{
    private static DatabaseException Translate(string sqlState)
    {
        var dialect = SqlDialectFactory.CreateDialectForType(SupportedDatabase.FlatFile,
            new fakeDbFactory(SupportedDatabase.FlatFile), NullLogger.Instance);
        return new FlatFileExceptionTranslator().Translate(dialect, new SqlStateException(sqlState),
            DbOperationKind.Insert);
    }

    [Fact]
    public void ReadOnlySqlTransaction_25006_IsReadOnlyViolation() =>
        Assert.IsType<ReadOnlyViolationException>(Translate("25006"));

    [Fact]
    public void TimeoutExpired_HYT00_IsTransientCommandTimeout()
    {
        var ex = Assert.IsType<CommandTimeoutException>(Translate("HYT00"));
        Assert.True(ex.IsTransient);
    }

    [Theory]
    [InlineData("08001")]
    [InlineData("08004")]
    public void ConnectionClass08_IsConnectionException(string sqlState) =>
        Assert.IsType<ConnectionException>(Translate(sqlState));

    private sealed class SqlStateException : DbException
    {
        public SqlStateException(string sqlState) : base("flatfile error " + sqlState) => SqlState = sqlState;

        public override string SqlState { get; }
    }
}
