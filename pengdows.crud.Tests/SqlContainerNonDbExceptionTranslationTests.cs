using System;
using System.Reflection;
using System.Threading.Tasks;
using AdoNetCore.AseClient;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// AdoNetCore.AseClient's AseException (used for Sybase ASE) does not derive from
/// <see cref="System.Data.Common.DbException"/> and exposes error info only via its "Errors"
/// collection. SqlContainer's exception-handling previously gated translation on
/// <c>ex is DbException</c>, so a plain unique-constraint violation propagated to the caller as the
/// raw, untranslated exception instead of the typed <see cref="DatabaseException"/> hierarchy
/// every other provider gets. Confirmed live: this exact gap made the Sybase testbed's
/// error-mapping check ("duplicate key insert must surface as DatabaseException") fail with the raw
/// AseException escaping a catch (DatabaseException). SqlContainer now recognizes that exception
/// type by name, not by shape — see SqlContainerProviderExceptionClassificationTests.
/// </summary>
public class SqlContainerNonDbExceptionTranslationTests
{
    // AseError's MessageNumber/Message setters are non-public; mirrors SybaseTranslatorTests.cs.
    private static AseException Ase(int messageNumber, string message)
    {
        var error = new AseError();
        SetProperty(error, nameof(AseError.MessageNumber), messageNumber);
        SetProperty(error, nameof(AseError.Message), message);
        return new AseException(new[] { error });
    }

    private static void SetProperty(object target, string propertyName, object? value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        property!.GetSetMethod(nonPublic: true)!.Invoke(target, new[] { value });
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_SingleConnection_AseException_IsTranslatedToDatabaseException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SybaseASE);
        var failingConnection = new fakeDbConnection();
        factory.Connections.Add(failingConnection);

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SybaseASE",
            DbMode = DbMode.SingleConnection
        };
        using var ctx = new DatabaseContext(cfg, factory, NullLoggerFactory.Instance);

        // Set after initialization probes so the test command is the one that hits it.
        failingConnection.SetCommandFailure("INSERT INTO t VALUES (1)",
            Ase(2601, "Attempt to insert duplicate key row"));

        var container = ctx.CreateSqlContainer("INSERT INTO t VALUES (1)");

        var ex = await Record.ExceptionAsync(async () => await container.ExecuteNonQueryAsync());

        Assert.IsAssignableFrom<DatabaseException>(ex);
    }
}
