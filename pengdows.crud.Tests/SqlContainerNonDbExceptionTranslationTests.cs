using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Some ADO.NET providers (e.g. AdoNetCore.AseClient's AseException, used for Sybase ASE) do not
/// derive from <see cref="System.Data.Common.DbException"/> at all and expose error info only via
/// a public "Errors" collection. SqlContainer's exception-handling previously gated translation on
/// <c>ex is DbException</c>, so any such provider's exceptions (including a plain unique-constraint
/// violation) propagated to the caller as the raw, untranslated exception instead of the typed
/// <see cref="DatabaseException"/> hierarchy every other provider gets. Confirmed live: this exact
/// gap made the Sybase testbed's error-mapping check ("duplicate key insert must surface as
/// DatabaseException") fail with the raw AseException escaping a `catch (DatabaseException)`.
/// </summary>
public class SqlContainerNonDbExceptionTranslationTests
{
    /// <summary>Simulates AdoNetCore.AseClient's AseException shape: not a DbException, error
    /// info only in an "Errors" collection.</summary>
    private sealed class ErrorsCollectionOnlyException : Exception
    {
        public ErrorsCollectionRecord[] Errors { get; }

        public ErrorsCollectionOnlyException(int messageNumber, string sqlState, string message)
            : base(message)
        {
            Errors = new[] { new ErrorsCollectionRecord(messageNumber, sqlState) };
        }
    }

    private sealed class ErrorsCollectionRecord
    {
        public int MessageNumber { get; }
        public string SqlState { get; }

        public ErrorsCollectionRecord(int messageNumber, string sqlState)
        {
            MessageNumber = messageNumber;
            SqlState = sqlState;
        }
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_NonDbExceptionWithErrorsCollection_IsTranslatedToDatabaseException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var failingConnection = new fakeDbConnection();
        failingConnection.SetCommandFailure(
            "INSERT INTO t VALUES (1)",
            new ErrorsCollectionOnlyException(2601, "23000", "duplicate key"));
        factory.Connections.Add(failingConnection);

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection
        };
        using var ctx = new DatabaseContext(cfg, factory, NullLoggerFactory.Instance);

        // Re-assert the failure after initialization probes to ensure the test command hits it.
        failingConnection.SetCommandFailure(
            "INSERT INTO t VALUES (1)",
            new ErrorsCollectionOnlyException(2601, "23000", "duplicate key"));

        var container = ctx.CreateSqlContainer("INSERT INTO t VALUES (1)");

        var ex = await Record.ExceptionAsync(async () => await container.ExecuteNonQueryAsync());

        Assert.IsAssignableFrom<DatabaseException>(ex);
    }
}
