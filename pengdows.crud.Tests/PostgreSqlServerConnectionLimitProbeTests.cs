using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// The usable connection limit on PostgreSQL is max_connections minus the slots the server holds
/// back for superusers (superuser_reserved_connections): an ordinary role is refused once it takes
/// the last of the rest, and the operator's emergency access is meant to survive. A probe that cannot
/// answer must say "unknown" (null), never throw — startup must not depend on it.
/// </summary>
public sealed class PostgreSqlServerConnectionLimitProbeTests
{
    private static async Task<int?> ProbeAsync(Action<fakeDbConnection> arrange)
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new PostgreSqlDialect(factory, NullLogger.Instance);
        var connection = new fakeDbConnection();
        arrange(connection);
        ModelRealServer(connection);
        using var tracked = new TrackedConnection(connection);
        return await dialect.ProbeServerConnectionLimitAsync(tracked, useAsync: true);
    }

    // A real server answers SHOW for a setting it knows and raises an error for one it does not
    // (reserved_connections does not exist before PostgreSQL 16); the fake's default for an
    // unconfigured command is not that, so unknown settings must fail here too.
    internal static void ModelRealServer(fakeDbConnection connection)
    {
        var answers = new Dictionary<string, object?>(connection.ScalarResultsByCommand);
        connection.ScalarResolver = command => answers.TryGetValue(command, out var value)
            ? value
            : throw new InvalidOperationException($"unrecognized configuration parameter: {command}");
    }

    [Fact]
    public async Task SubtractsTheSuperuserReservedSlots()
    {
        var limit = await ProbeAsync(c =>
        {
            c.ScalarResultsByCommand["SHOW max_connections"] = "25";
            c.ScalarResultsByCommand["SHOW superuser_reserved_connections"] = "3";
        });

        Assert.Equal(22, limit);
    }

    [Fact]
    public async Task NoReservedSlots_IsTheWholeLimit()
    {
        var limit = await ProbeAsync(c =>
        {
            c.ScalarResultsByCommand["SHOW max_connections"] = "100";
            c.ScalarResultsByCommand["SHOW superuser_reserved_connections"] = "0";
        });

        Assert.Equal(100, limit);
    }

    [Fact]
    public async Task ReservedAtLeastTheWholeLimit_FloorsAtOne()
    {
        var limit = await ProbeAsync(c =>
        {
            c.ScalarResultsByCommand["SHOW max_connections"] = "3";
            c.ScalarResultsByCommand["SHOW superuser_reserved_connections"] = "3";
        });

        Assert.Equal(1, limit);
    }

    [Fact]
    public async Task AnUnreadableMaxConnections_IsUnknown()
    {
        var limit = await ProbeAsync(c =>
        {
            c.ScalarResultsByCommand["SHOW max_connections"] = "not-a-number";
            c.ScalarResultsByCommand["SHOW superuser_reserved_connections"] = "3";
        });

        Assert.Null(limit);
    }

    [Fact]
    public async Task AFailingQuery_IsUnknown_NotAnException()
    {
        var limit = await ProbeAsync(c => c.SetScalarExecuteException(new InvalidOperationException("permission denied")));

        Assert.Null(limit);
    }

    [Fact]
    public async Task PostgreSql16ReservedConnections_AreSubtractedToo()
    {
        // PG 16 added reserved_connections (for pg_use_reserved_connections members) on top of the
        // superuser reserve; an ordinary client gets max - reserved_connections - superuser_reserved.
        var limit = await ProbeAsync(c =>
        {
            c.ScalarResultsByCommand["SHOW max_connections"] = "100";
            c.ScalarResultsByCommand["SHOW reserved_connections"] = "5";
            c.ScalarResultsByCommand["SHOW superuser_reserved_connections"] = "3";
        });

        Assert.Equal(92, limit);
    }

    [Fact]
    public async Task AnUnreadableSuperuserReserve_AssumesPostgreSqlsDefaultOfThree_NotZero()
    {
        // A failed safety probe must not become permission to over-admit.
        var limit = await ProbeAsync(c =>
        {
            c.ScalarResultsByCommand["SHOW max_connections"] = "25";
            c.ScalarResultsByCommand["SHOW reserved_connections"] = "0";
            c.ScalarResultsByCommand["SHOW superuser_reserved_connections"] = "n/a";
        });

        Assert.Equal(22, limit);
    }

    [Fact]
    public async Task AnUnreadableReservedConnections_AssumesItsDocumentedDefaultOfZero()
    {
        // The parameter does not exist before PostgreSQL 16, where the effective value is 0.
        var limit = await ProbeAsync(c =>
        {
            c.ScalarResultsByCommand["SHOW max_connections"] = "25";
            c.ScalarResultsByCommand["SHOW reserved_connections"] = "n/a";
            c.ScalarResultsByCommand["SHOW superuser_reserved_connections"] = "3";
        });

        Assert.Equal(22, limit);
    }

    [Fact]
    public async Task ADialectWithNoProbe_ReportsUnknown()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new SqliteDialect(factory, NullLogger.Instance);
        using var tracked = new TrackedConnection(new fakeDbConnection());

        Assert.Null(await dialect.ProbeServerConnectionLimitAsync(tracked, useAsync: true));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("2147483648")]
    [InlineData("abc")]
    [InlineData("")]
    public async Task ANonPositiveOrUnparseableMaxConnections_IsUnknown(string maxConnections)
    {
        var limit = await ProbeAsync(c =>
        {
            c.ScalarResultsByCommand["SHOW max_connections"] = maxConnections;
            c.ScalarResultsByCommand["SHOW superuser_reserved_connections"] = "3";
        });

        Assert.Null(limit);
    }

    [Fact]
    public async Task ANegativeReserve_IsIgnored_NotAddedToTheLimit()
    {
        var limit = await ProbeAsync(c =>
        {
            c.ScalarResultsByCommand["SHOW max_connections"] = "25";
            c.ScalarResultsByCommand["SHOW superuser_reserved_connections"] = "-5";
            c.ScalarResultsByCommand["SHOW reserved_connections"] = "-2";
        });

        Assert.Equal(25, limit);
    }

    [Fact]
    public async Task ValuesWithSurroundingWhitespace_AreStillRead()
    {
        var limit = await ProbeAsync(c =>
        {
            c.ScalarResultsByCommand["SHOW max_connections"] = " 25 ";
            c.ScalarResultsByCommand["SHOW superuser_reserved_connections"] = " 3 ";
        });

        Assert.Equal(22, limit);
    }
}
