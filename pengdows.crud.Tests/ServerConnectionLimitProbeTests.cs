using System;
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
/// One probe per engine whose limit can be read reliably; engines whose limit is unreadable or
/// meaningless (distributed or serverless ones) answer "unknown" explicitly instead of inheriting a
/// probe that does not apply. The probe never throws.
/// </summary>
public sealed class ServerConnectionLimitProbeTests
{
    private const string MaxConnections = "SELECT @@max_connections";
    private const string MaxUserConnections = "SELECT @@max_user_connections";
    private const string SqlServerUserConnections =
        "SELECT CAST(value_in_use AS int) FROM sys.configurations WHERE name = 'user connections'";

    private static Task<int?> Probe(SqlDialect dialect, Action<fakeDbConnection> arrange)
    {
        var connection = new fakeDbConnection();
        arrange(connection);
        PostgreSqlServerConnectionLimitProbeTests.ModelRealServer(connection);
        var tracked = new TrackedConnection(connection);
        return dialect.ProbeServerConnectionLimitAsync(tracked, useAsync: true);
    }

    private static MySqlDialect MySql() =>
        new(new fakeDbFactory(SupportedDatabase.MySql), NullLogger.Instance);

    // ── MySQL / MariaDB ───────────────────────────────────────────────────────

    [Fact]
    public async Task MySql_UsesMaxConnections_WhenThereIsNoPerUserLimit()
    {
        var limit = await Probe(MySql(), c =>
        {
            c.ScalarResultsByCommand[MaxConnections] = 151L;
            c.ScalarResultsByCommand[MaxUserConnections] = 0L;
        });

        Assert.Equal(151, limit);
    }

    [Fact]
    public async Task MySql_APerUserLimitBelowMaxConnections_Wins()
    {
        var limit = await Probe(MySql(), c =>
        {
            c.ScalarResultsByCommand[MaxConnections] = 151L;
            c.ScalarResultsByCommand[MaxUserConnections] = 50L;
        });

        Assert.Equal(50, limit);
    }

    [Fact]
    public async Task MySql_AnUnreadablePerUserLimit_StillUsesMaxConnections()
    {
        var limit = await Probe(MySql(), c =>
        {
            c.ScalarResultsByCommand[MaxConnections] = 151L;
            c.ScalarResultsByCommand[MaxUserConnections] = "n/a";
        });

        Assert.Equal(151, limit);
    }

    [Fact]
    public async Task MySql_AnUnreadableMaxConnections_IsUnknown()
    {
        var limit = await Probe(MySql(), c => c.SetScalarExecuteException(new InvalidOperationException("denied")));

        Assert.Null(limit);
    }

    [Fact]
    public async Task MariaDb_InheritsTheMySqlProbe()
    {
        var dialect = new MariaDbDialect(new fakeDbFactory(SupportedDatabase.MariaDb), NullLogger.Instance);

        var limit = await Probe(dialect, c =>
        {
            c.ScalarResultsByCommand[MaxConnections] = 100L;
            c.ScalarResultsByCommand[MaxUserConnections] = 0L;
        });

        Assert.Equal(100, limit);
    }

    // ── SQL Server ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SqlServer_ReadsUserConnections()
    {
        var dialect = new SqlServerDialect(new fakeDbFactory(SupportedDatabase.SqlServer), NullLogger.Instance);

        Assert.Equal(200, await Probe(dialect, c => c.ScalarResultsByCommand[SqlServerUserConnections] = 200));
    }

    [Fact]
    public async Task SqlServer_ZeroMeansUnlimited_SoItIsUnknown()
    {
        var dialect = new SqlServerDialect(new fakeDbFactory(SupportedDatabase.SqlServer), NullLogger.Instance);

        Assert.Null(await Probe(dialect, c => c.ScalarResultsByCommand[SqlServerUserConnections] = 0));
    }

    [Fact]
    public async Task SqlServer_AFailingQuery_IsUnknown()
    {
        var dialect = new SqlServerDialect(new fakeDbFactory(SupportedDatabase.SqlServer), NullLogger.Instance);

        Assert.Null(await Probe(dialect, c => c.SetScalarExecuteException(new InvalidOperationException("denied"))));
    }

    // ── engines that must say "unknown", not inherit a probe that does not apply ──

    [Fact]
    public async Task CockroachDb_IsUnknown_EvenWithPostgreSqlAnswersAvailable()
    {
        var dialect = new CockroachDbDialect(new fakeDbFactory(SupportedDatabase.CockroachDb), NullLogger.Instance);

        Assert.Null(await Probe(dialect, c =>
        {
            c.ScalarResultsByCommand["SHOW max_connections"] = "100";
            c.ScalarResultsByCommand["SHOW superuser_reserved_connections"] = "3";
        }));
    }

    [Fact]
    public async Task Spanner_IsUnknown_EvenWithPostgreSqlAnswersAvailable()
    {
        var dialect = new SpannerDialect(new fakeDbFactory(SupportedDatabase.Spanner), NullLogger.Instance);

        Assert.Null(await Probe(dialect, c => c.ScalarResultsByCommand["SHOW max_connections"] = "100"));
    }

    [Fact]
    public async Task TiDb_IsUnknown_EvenWithMySqlAnswersAvailable()
    {
        var dialect = new TiDbDialect(new fakeDbFactory(SupportedDatabase.TiDb), NullLogger.Instance);

        Assert.Null(await Probe(dialect, c => c.ScalarResultsByCommand[MaxConnections] = 151L));
    }

    [Fact]
    public async Task YugabyteDb_UsesThePostgreSqlProbe()
    {
        var dialect = new YugabyteDbDialect(new fakeDbFactory(SupportedDatabase.YugabyteDb), NullLogger.Instance);

        Assert.Equal(97, await Probe(dialect, c =>
        {
            c.ScalarResultsByCommand["SHOW max_connections"] = "100";
            c.ScalarResultsByCommand["SHOW superuser_reserved_connections"] = "3";
        }));
    }

    // ── edge values every engine's probe must treat as "unknown" or ignore, never as a limit ──

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData("n/a")]
    public async Task MySql_ANonPositiveOrUnparseableMaxConnections_IsUnknown(object maxConnections)
    {
        var limit = await Probe(MySql(), c =>
        {
            c.ScalarResultsByCommand[MaxConnections] = maxConnections;
            c.ScalarResultsByCommand[MaxUserConnections] = 0L;
        });

        Assert.Null(limit);
    }

    [Theory]
    [InlineData(151L)]
    [InlineData(500L)]
    [InlineData(-3L)]
    public async Task MySql_APerUserLimitAtOrAboveMaxConnectionsOrNegative_IsIgnored(object perUser)
    {
        var limit = await Probe(MySql(), c =>
        {
            c.ScalarResultsByCommand[MaxConnections] = 151L;
            c.ScalarResultsByCommand[MaxUserConnections] = perUser;
        });

        Assert.Equal(151, limit);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData("n/a")]
    public async Task SqlServer_ANegativeOrUnparseableValue_IsUnknown(object value)
    {
        var dialect = new SqlServerDialect(new fakeDbFactory(SupportedDatabase.SqlServer), NullLogger.Instance);

        Assert.Null(await Probe(dialect, c => c.ScalarResultsByCommand[SqlServerUserConnections] = value));
    }

    [Fact]
    public async Task SqlServer_TheEnginesMaximumOf32767_IsAnOrdinaryLimit()
    {
        var dialect = new SqlServerDialect(new fakeDbFactory(SupportedDatabase.SqlServer), NullLogger.Instance);

        Assert.Equal(32767, await Probe(dialect, c => c.ScalarResultsByCommand[SqlServerUserConnections] = 32767));
    }
}
