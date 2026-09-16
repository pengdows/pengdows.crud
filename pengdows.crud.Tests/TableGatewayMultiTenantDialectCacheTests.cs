// =============================================================================
// FILE: TableGatewayMultiTenantDialectCacheTests.cs
// PURPOSE: Fast, fakeDb-driven proof that a single shared TableGateway<,> instance
//          generates version-correct SQL per IDatabaseContext, even when two contexts
//          are for the same product at genuinely different server versions.
//
// This is the deterministic counterpart to the real-Postgres/MySQL scenario in
// pengdows.crud.IntegrationTests/Core/MultiTenantDialectVersionTests.cs. It drives version
// detection through fakeDb's exact-command-text scalar override (SetScalarResultForCommand)
// rather than a real database round-trip, so it runs everywhere and in milliseconds.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.metrics;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Reproduces a real multi-tenancy bug: <see cref="TableGateway{TEntity,TRowID}"/> is a singleton
/// shared across tenant <see cref="IDatabaseContext"/> instances (per pengdows.crud/CLAUDE.md's
/// documented multi-tenancy pattern, <c>gateway.Method(entity, tenantCtx)</c>). Its SQL-template
/// caches (<c>_templatesByDialect</c>, <c>_upsertBinders</c>, etc.) key on
/// <see cref="ISqlDialect.DatabaseType"/> — the coarse enum — rather than on the dialect instance.
/// Two tenants on the same engine but different server versions collide: whichever tenant's
/// dialect builds the cached template first silently wins for every other tenant on that engine,
/// even when the dialect explicitly version-gates behavior (e.g.
/// <c>MySqlDialect.UpsertIncomingAlias</c>, gated on MySQL &gt;= 8.0.20).
/// </summary>
public class TableGatewayMultiTenantDialectCacheTests
{
    [Table("multi_tenant_cache_entity")]
    private sealed class CacheEntity
    {
        [Id]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    private static IDatabaseContext BuildPostgresContext(string versionString)
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<CacheEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var connection = new fakeDbConnection
        {
            ConnectionString = "Data Source=test;EmulatedProduct=PostgreSql"
        };
        // Overrides fakeDb's hardcoded "PostgreSQL 15.0" default for "SELECT version()", by exact
        // command text rather than FIFO order - DatabaseContext's init path runs "SELECT
        // version()" more than once against the same connection (product-family detection, then
        // dialect creation/DetectDatabaseInfoAsync), and an override keyed by command text
        // answers every one of those calls identically regardless of how many round-trips the
        // current implementation happens to make.
        connection.SetScalarResultForCommand("SELECT version()", versionString);

        // DatabaseDetectionService's PostgreSQL-family flavor probes run after "SELECT version()"
        // and treat any non-empty string result as a positive match (YugabyteDB/Aurora); without
        // these, the queued Postgres version string above would itself satisfy `is string
        // { Length: > 0 }` on both probes and misidentify this as YugabyteDB.
        connection.SetScalarResultForCommand(
            "SELECT name FROM pg_settings WHERE name = 'yb_enable_optimizer_statistics' LIMIT 1", DBNull.Value);
        connection.SetScalarResultForCommand("SELECT aurora_version()", DBNull.Value);

        factory.Connections.Add(connection);

        return new DatabaseContext(
            new DatabaseContextConfiguration
            {
                ConnectionString = "Data Source=test;EmulatedProduct=PostgreSql",
                DbMode = DbMode.SingleConnection
            },
            factory, NullLoggerFactory.Instance, typeMap);
    }

    [Fact]
    public async Task SharedGateway_TwoPostgreSqlVersions_GeneratesCorrectSqlPerContext()
    {
        // PostgreSQL 15+ supports MERGE in addition to ON CONFLICT; BuildUpsert prefers MERGE
        // when the dialect supports it (checked before SupportsInsertOnConflict), so these two
        // contexts must produce genuinely different SQL statement shapes from the SAME
        // TableGateway<,> instance - exactly the multitenancy pattern in this repo's CLAUDE.md
        // (gateway.Method(entity, tenantCtx)). If the per-dialect template cache were keyed by
        // the SupportedDatabase enum (or otherwise shared across instances) instead of the
        // dialect instance, the second call below would incorrectly reuse the first context's
        // cached UpsertUpdateFragment.
        await using var oldContext = BuildPostgresContext("PostgreSQL 12.4 on x86_64-pc-linux-gnu");
        await using var newContext = BuildPostgresContext("PostgreSQL 16.1 on x86_64-pc-linux-gnu");

        Assert.False(oldContext.GetDialect().SupportsMerge, "Precondition: PostgreSQL 12.4 must not support MERGE.");
        Assert.True(newContext.GetDialect().SupportsMerge, "Precondition: PostgreSQL 16.1 must support MERGE.");

        var gateway = new TableGateway<CacheEntity, int>(oldContext);

        var oldSql = gateway.BuildUpsert(new CacheEntity { Id = 1, Name = "old-tenant" }, oldContext).Query.ToString();
        var newSql = gateway.BuildUpsert(new CacheEntity { Id = 1, Name = "new-tenant" }, newContext).Query.ToString();

        Assert.Contains("ON CONFLICT", oldSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EXCLUDED", oldSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERGE", oldSql, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("MERGE", newSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(" = s.", newSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EXCLUDED", newSql, StringComparison.OrdinalIgnoreCase);
    }

    [Table("widgets")]
    private class Widget
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public async Task BuildUpsert_TwoTenantsOnDifferentMySqlVersions_EachGetsCorrectSyntax()
    {
        var typeMap = new TypeMapRegistry();
        var baseFactory = new fakeDbFactory(SupportedDatabase.MySql);
        await using var baseContext = new DatabaseContext("Data Source=test;EmulatedProduct=MySql", baseFactory, typeMap);

        var gateway = new TableGateway<Widget, int>(baseContext);

        var legacyDialect = await BuildMySqlDialect("8.0.19");
        var modernDialect = await BuildMySqlDialect("8.0.33");

        var legacyTenant = new TenantDialectOverrideContext(baseContext, legacyDialect);
        var modernTenant = new TenantDialectOverrideContext(baseContext, modernDialect);

        var entity = new Widget { Id = 1, Name = "gadget" };

        // Whichever tenant calls first must not poison the shared gateway's cache for the other.
        using var legacySql = gateway.BuildUpsert(entity, legacyTenant);
        using var modernSql = gateway.BuildUpsert(entity, modernTenant);

        Assert.Contains("VALUES(", legacySql.Query.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("incoming", legacySql.Query.ToString(), StringComparison.OrdinalIgnoreCase);

        Assert.Contains("incoming", modernSql.Query.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("VALUES(", modernSql.Query.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildUpsert_ModernTenantFirst_LegacyTenantStillGetsCorrectSyntax()
    {
        // Same as above with call order reversed — the bug is order-dependent (whichever
        // dialect instance populates the cache first wins), so both orderings must be covered.
        var typeMap = new TypeMapRegistry();
        var baseFactory = new fakeDbFactory(SupportedDatabase.MySql);
        await using var baseContext = new DatabaseContext("Data Source=test;EmulatedProduct=MySql", baseFactory, typeMap);

        var gateway = new TableGateway<Widget, int>(baseContext);

        var legacyDialect = await BuildMySqlDialect("8.0.19");
        var modernDialect = await BuildMySqlDialect("8.0.33");

        var legacyTenant = new TenantDialectOverrideContext(baseContext, legacyDialect);
        var modernTenant = new TenantDialectOverrideContext(baseContext, modernDialect);

        var entity = new Widget { Id = 1, Name = "gadget" };

        using var modernSql = gateway.BuildUpsert(entity, modernTenant);
        using var legacySql = gateway.BuildUpsert(entity, legacyTenant);

        Assert.Contains("incoming", modernSql.Query.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VALUES(", legacySql.Query.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("incoming", legacySql.Query.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // Note: this codebase's template cache is keyed by dialect INSTANCE (ConditionalWeakTable),
    // not by a DatabaseType+version fingerprint string — see BaseTableGateway.Core.cs's
    // _queryCache/_templatesByDialect comments. That means two distinct dialect instances on the
    // same effective version each get their own cache entry rather than sharing one; this is a
    // deliberate simplicity/GC-lifetime tradeoff (entries are reclaimed automatically as tenant
    // contexts are disposed), not a bug, so there is no "same-version tenants share one entry"
    // test here.

    private static async Task<ISqlDialect> BuildMySqlDialect(string serverVersion)
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var connection = new fakeDbConnection();
        connection.EmulatedProduct = SupportedDatabase.MySql;
        connection.SetServerVersion(serverVersion);
        connection.SetScalarResultForCommand("SELECT VERSION()", serverVersion);

        var tracked = new TrackedConnection(connection);
        tracked.Open();

        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance);
        await dialect.DetectDatabaseInfoAsync(tracked);
        return dialect;
    }

    /// <summary>
    /// Decorates a real, fully-functional <see cref="IDatabaseContext"/> so that everything not
    /// tied to dialect identity (connection pooling, transactions, parameter creation machinery,
    /// metrics, etc.) behaves normally, while dialect-derived members
    /// (<see cref="Dialect"/>, <see cref="DataSourceInfo"/>, <see cref="Product"/>, quoting,
    /// parameter naming) reflect a specific tenant's own detected server version — exactly what a
    /// real per-tenant <c>DatabaseContext</c> would report, without needing a second live
    /// connection/detection round-trip per test.
    /// </summary>
    private sealed class TenantDialectOverrideContext : IDatabaseContext
    {
        private readonly IDatabaseContext _inner;
        private readonly ISqlDialect _dialect;
        private readonly IDataSourceInformation _dataSourceInfo;

        public TenantDialectOverrideContext(IDatabaseContext inner, ISqlDialect dialect)
        {
            _inner = inner;
            _dialect = dialect;
            _dataSourceInfo = new DataSourceInformation(dialect);
        }

        public ISqlDialect Dialect => _dialect;
        public DbDataSource? DataSource => _inner.DataSource;
        public IDataSourceInformation DataSourceInfo => _dataSourceInfo;
        public SupportedDatabase Product => _dataSourceInfo.Product;
        public bool SupportsInsertReturning => _dialect.SupportsInsertReturning;
        public string QuotePrefix => _dialect.QuotePrefix;
        public string QuoteSuffix => _dialect.QuoteSuffix;
        public string CompositeIdentifierSeparator => _dataSourceInfo.CompositeIdentifierSeparator;
        public string WrapObjectName(string name) => _dialect.WrapObjectName(name);
        public string MakeParameterName(DbParameter dbParameter) => _dialect.MakeParameterName(dbParameter);
        public string MakeParameterName(string parameterName) => _dialect.MakeParameterName(parameterName);

        public DbMode ConnectionMode => _inner.ConnectionMode;
        public Guid RootId => _inner.RootId;
        public ReadWriteMode ReadWriteMode => _inner.ReadWriteMode;
        public string ConnectionString => _inner.ConnectionString;
        public string Name => _inner.Name;
        public TimeSpan? ModeLockTimeout => _inner.ModeLockTimeout;
        public ProcWrappingStyle ProcWrappingStyle => _inner.ProcWrappingStyle;
        public int MaxParameterLimit => _inner.MaxParameterLimit;
        public int MaxOutputParameters => _inner.MaxOutputParameters;
        public long NumberOfOpenConnections => _inner.NumberOfOpenConnections;
        public DatabaseMetrics Metrics => _inner.Metrics;
        public long PeakOpenConnections => _inner.PeakOpenConnections;
        public CommandPrepareMode PrepareMode => _inner.PrepareMode;
        public bool IsReadOnlyConnection => _inner.IsReadOnlyConnection;
        public bool RCSIEnabled => _inner.RCSIEnabled;
        public bool SnapshotIsolationEnabled => _inner.SnapshotIsolationEnabled;
        public bool IsDisposed => _inner.IsDisposed;

        public event EventHandler<DatabaseMetrics> MetricsUpdated
        {
            add => _inner.MetricsUpdated += value;
            remove => _inner.MetricsUpdated -= value;
        }

        public string GetBaseSessionSettings() => _inner.GetBaseSessionSettings();
        public string GetReadOnlySessionSettings() => _inner.GetReadOnlySessionSettings();
        public IReadOnlySet<IsolationLevel> GetSupportedIsolationLevels() => _inner.GetSupportedIsolationLevels();
        public ISqlContainer CreateSqlContainer(string? query = null) => _inner.CreateSqlContainer(query);

        public DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
            => _inner.CreateDbParameter(name, type, value);

        public DbParameter CreateDbParameter<T>(string? name, DbType type, T value, ParameterDirection direction)
            => _inner.CreateDbParameter(name, type, value, direction);

        public DbParameter CreateDbParameter<T>(DbType type, T value) => _inner.CreateDbParameter(type, value);

        public ITransactionContext BeginTransaction(
            IsolationLevel? isolationLevel = null,
            ExecutionType executionType = ExecutionType.Write)
            => _inner.BeginTransaction(isolationLevel, executionType);

        public ITransactionContext BeginTransaction(
            IsolationProfile isolationProfile,
            ExecutionType executionType = ExecutionType.Write)
            => _inner.BeginTransaction(isolationProfile, executionType);

        public ValueTask<ITransactionContext> BeginTransactionAsync(
            IsolationLevel? isolationLevel = null,
            ExecutionType executionType = ExecutionType.Write,
            CancellationToken cancellationToken = default)
            => _inner.BeginTransactionAsync(isolationLevel, executionType, cancellationToken);

        public ValueTask<ITransactionContext> BeginTransactionAsync(
            IsolationProfile isolationProfile,
            ExecutionType executionType = ExecutionType.Write,
            CancellationToken cancellationToken = default)
            => _inner.BeginTransactionAsync(isolationProfile, executionType, cancellationToken);

        public string GenerateParameterName() => _inner.GenerateParameterName();

        public string GenerateRandomName(int length = 5, int parameterNameMaxLength = 30)
            => _inner.GenerateRandomName(length, parameterNameMaxLength);

        public void Dispose() => _inner.Dispose();
        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }
}
