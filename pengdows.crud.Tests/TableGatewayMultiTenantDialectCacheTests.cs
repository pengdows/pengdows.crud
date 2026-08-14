using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
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

    /// <summary>
    /// Correctness (above) only requires that same-version tenants never collide with
    /// different-version ones. It doesn't require space efficiency when many tenants share the
    /// identical version — a common real-world case (e.g. a managed fleet standardized on one
    /// engine version, with an occasional un-upgraded or newer outlier). This proves the
    /// complementary property: two tenants on the SAME version must reuse one cache entry, not
    /// silently accumulate one per tenant.
    /// </summary>
    [Fact]
    public async Task BuildUpsert_TwoTenantsOnSameMySqlVersion_ShareOneCacheEntry()
    {
        var typeMap = new TypeMapRegistry();
        var baseFactory = new fakeDbFactory(SupportedDatabase.MySql);
        await using var baseContext = new DatabaseContext("Data Source=test;EmulatedProduct=MySql", baseFactory, typeMap);

        var gateway = new TableGateway<Widget, int>(baseContext);

        var tenantADialect = await BuildMySqlDialect("8.0.33");
        var tenantBDialect = await BuildMySqlDialect("8.0.33");
        Assert.NotSame(tenantADialect, tenantBDialect);

        var tenantA = new TenantDialectOverrideContext(baseContext, tenantADialect);
        var tenantB = new TenantDialectOverrideContext(baseContext, tenantBDialect);

        var entity = new Widget { Id = 1, Name = "gadget" };

        using var sqlA = gateway.BuildUpsert(entity, tenantA);
        using var sqlB = gateway.BuildUpsert(entity, tenantB);

        var field = typeof(TableGateway<Widget, int>).GetField("_templatesByDialect",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var cacheEntryCount = ((IEnumerable)field.GetValue(gateway)!).Cast<object>().Count();

        Assert.Equal(1, cacheEntryCount);
    }

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
