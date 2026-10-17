using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.tenant;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Interface members added after 2.0.5 must carry a default implementation so that consumer
/// types implementing these interfaces against 2.0.5 keep compiling and loading on 2.0.x
/// (package validation enforces the binary side at pack time; these tests pin the behavior).
/// </summary>
public class SemVerCompatibilityDefaultMemberTests
{
    public static TheoryData<Type, string> MembersAddedAfter205 => new()
    {
        { typeof(ISqlDialect), "get_" + nameof(ISqlDialect.SavepointCapabilities) },
        { typeof(ISqlDialect), "get_" + nameof(ISqlDialect.PreservesTrailingWhitespace) },
        { typeof(IDatabaseContextConfiguration), "get_" + nameof(IDatabaseContextConfiguration.SessionInitializationFailureMode) },
        { typeof(IDatabaseContextConfiguration), "set_" + nameof(IDatabaseContextConfiguration.SessionInitializationFailureMode) },
        { typeof(IDatabaseContextConfiguration), "get_" + nameof(IDatabaseContextConfiguration.MaxQueuedReads) },
        { typeof(IDatabaseContextConfiguration), "set_" + nameof(IDatabaseContextConfiguration.MaxQueuedReads) },
        { typeof(IDatabaseContextConfiguration), "get_" + nameof(IDatabaseContextConfiguration.MaxQueuedWrites) },
        { typeof(IDatabaseContextConfiguration), "set_" + nameof(IDatabaseContextConfiguration.MaxQueuedWrites) },
        { typeof(ITableGateway<SemVerEntity, int>), "get_AuditCreationPolicy" },
        { typeof(ITableGateway<SemVerEntity, int>), "set_AuditCreationPolicy" },
        { typeof(IPrimaryKeyTableGateway<SemVerEntity>), "get_AuditCreationPolicy" },
        { typeof(IPrimaryKeyTableGateway<SemVerEntity>), "set_AuditCreationPolicy" },
        { typeof(ITableGateway<SemVerEntity, int>), "ColumnName" },
        { typeof(IPrimaryKeyTableGateway<SemVerEntity>), "ColumnName" },
        { typeof(ITransactionContext), nameof(ITransactionContext.ReleaseSavepointAsync) },
        { typeof(IDataSourceInformation), "get_" + nameof(IDataSourceInformation.ParsedVersion) },
        { typeof(ITenantContextRegistry), nameof(ITenantContextRegistry.AcquireLease) }
    };

    [Theory]
    [MemberData(nameof(MembersAddedAfter205))]
    public void MemberAddedAfter205_HasDefaultImplementation(Type interfaceType, string methodName)
    {
        var methods = interfaceType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == methodName)
            .ToList();

        Assert.NotEmpty(methods);
        Assert.All(methods, m => Assert.False(m.IsAbstract,
            $"{interfaceType.Name}.{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))}) " +
            "was added after 2.0.5 and must have a default implementation to stay binary compatible."));
    }

    [Fact]
    public void ADialectThatIsNotAnInternalOne_FallsBackToTheAnsiDefaults()
    {
        ISqlDialect dialect = new ScriptedSqlDialect { WrapObjectNameFunc = n => "\"" + n + "\"" };

        Assert.True(dialect.IsClientServerDatabase());
        Assert.False(dialect.IsEmbeddedSingleWriterEngine());
        Assert.Equal(InMemoryKind.None, dialect.DetectInMemoryKind("Data Source=:memory:"));
        Assert.True(dialect.SupportsSemicolonStatementSeparator());
        Assert.True(dialect.PreservesTrailingWhitespace);
        Assert.Equal("RELEASE SAVEPOINT \"sp1\"", dialect.GetReleaseSavepointSql("sp1"));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void ADialectThatIsNotAnInternalOne_SupportsPaging_FollowsThePagingSyntaxFlags(bool offsetFetch, bool limitOffset,
        bool expected)
    {
        ISqlDialect dialect = new ScriptedSqlDialect { SupportsOffsetFetch = offsetFetch, SupportsLimitOffset = limitOffset };

        Assert.Equal(expected, dialect.SupportsPaging());
    }

    [Fact]
    public void ADialectThatIsNotAnInternalOne_ResolvesBestToStandardAndHonorsAnExplicitMode()
    {
        ISqlDialect dialect = new ScriptedSqlDialect();

        Assert.Equal(DbMode.Standard, dialect.CoerceConnectionMode(DbMode.Best, null, false).Mode);
        Assert.Equal(DbMode.SingleWriter, dialect.CoerceConnectionMode(DbMode.SingleWriter, null, false).Mode);
    }

    [Theory]
    [InlineData(true, SavepointCapabilities.Create | SavepointCapabilities.Rollback | SavepointCapabilities.Release)]
    [InlineData(false, SavepointCapabilities.None)]
    public void ISqlDialect_SavepointCapabilities_Default_FollowsSupportsSavepoints(bool supports,
        SavepointCapabilities expected)
    {
        ISqlDialect dialect = new ScriptedSqlDialect { SupportsSavepoints = supports };

        Assert.Equal(expected, dialect.SavepointCapabilities);
    }

    [Fact]
    public async Task ITransactionContext_ReleaseSavepointAsync_Default_ThrowsNotSupported()
    {
        ITransactionContext txn = new ScriptedTransactionContext();

        await Assert.ThrowsAsync<NotSupportedException>(async () => await txn.ReleaseSavepointAsync("sp1"));
    }

    [Theory]
    [InlineData("15.2", "15.2")]
    [InlineData("not a version", null)]
    public void IDataSourceInformation_ParsedVersion_Default_ParsesProductVersion(string productVersion,
        string? expected)
    {
        IDataSourceInformation info = new ScriptedDataSourceInformation { DatabaseProductVersion = productVersion };

        Assert.Equal(expected == null ? null : Version.Parse(expected), info.ParsedVersion);
    }

    [Fact]
    public void IDatabaseContextConfiguration_NewMembers_Default_ReturnDefaultsAndAcceptOnlyDefaults()
    {
        IDatabaseContextConfiguration config = new ScriptedDatabaseContextConfiguration();

        Assert.Null(config.SessionInitializationFailureMode);
        Assert.Null(config.MaxQueuedReads);
        Assert.Null(config.MaxQueuedWrites);

        config.SessionInitializationFailureMode = null;
        config.SessionInitializationFailureMode = SessionInitializationFailureMode.BestEffort;
        config.MaxQueuedReads = null;
        config.MaxQueuedWrites = null;

        Assert.Throws<NotSupportedException>(() =>
            config.SessionInitializationFailureMode = SessionInitializationFailureMode.FailClosed);
        Assert.Throws<NotSupportedException>(() => config.MaxQueuedReads = 5);
        Assert.Throws<NotSupportedException>(() => config.MaxQueuedWrites = 5);
    }

    [Fact]
    public void ITableGateway_AuditCreationPolicy_Default_PreservesAndAcceptsOnlyDefault()
    {
        ITableGateway<SemVerEntity, int> gateway = new ScriptedTableGateway<SemVerEntity, int>();
        // init-only: invoke the default init accessor directly, as an object initializer would.
        var init = typeof(ITableGateway<SemVerEntity, int>).GetProperty("AuditCreationPolicy")!.SetMethod!;

        Assert.Equal(AuditCreationPolicy.PreserveExplicitValues, gateway.AuditCreationPolicy);
        init.Invoke(gateway, new object[] { AuditCreationPolicy.PreserveExplicitValues });
        var ex = Assert.Throws<TargetInvocationException>(() =>
            init.Invoke(gateway, new object[] { AuditCreationPolicy.Authoritative }));
        Assert.IsType<NotSupportedException>(ex.InnerException);
    }

    [Fact]
    public void ITableGateway_ColumnName_Default_IsNotSupported()
    {
        ITableGateway<SemVerEntity, int> gateway = new ScriptedTableGateway<SemVerEntity, int>();

        Assert.Throws<NotSupportedException>(() => gateway.ColumnName("Id"));
    }

    [Fact]
    public void IPrimaryKeyTableGateway_ColumnName_Default_IsNotSupported()
    {
        IPrimaryKeyTableGateway<SemVerEntity> gateway = new ScriptedPrimaryKeyTableGateway<SemVerEntity>();

        Assert.Throws<NotSupportedException>(() => gateway.ColumnName("Id"));
    }

    [Fact]
    public void IPrimaryKeyTableGateway_AuditCreationPolicy_Default_PreservesAndAcceptsOnlyDefault()
    {
        IPrimaryKeyTableGateway<SemVerEntity> gateway = new ScriptedPrimaryKeyTableGateway<SemVerEntity>();
        // init-only: invoke the default init accessor directly, as an object initializer would.
        var init = typeof(IPrimaryKeyTableGateway<SemVerEntity>).GetProperty("AuditCreationPolicy")!.SetMethod!;

        Assert.Equal(AuditCreationPolicy.PreserveExplicitValues, gateway.AuditCreationPolicy);
        init.Invoke(gateway, new object[] { AuditCreationPolicy.PreserveExplicitValues });
        var ex = Assert.Throws<TargetInvocationException>(() =>
            init.Invoke(gateway, new object[] { AuditCreationPolicy.Authoritative }));
        Assert.IsType<NotSupportedException>(ex.InnerException);
    }

    [Fact]
    public async Task ITenantContextRegistry_AcquireLease_Default_WrapsGetContextWithoutDisposingIt()
    {
        var context = new ScriptedDatabaseContext();
        ITenantContextRegistry registry = new ScriptedTenantContextRegistry
        {
            ContextFactory = tenant => tenant == "t1" ? context : throw new KeyNotFoundException(tenant)
        };

        using (var lease = registry.AcquireLease("t1"))
        {
            Assert.Same(context, lease.Context);
        }

        await using (var asyncLease = registry.AcquireLease("t1"))
        {
            Assert.Same(context, asyncLease.Context);
        }

        Assert.False(context.IsDisposed);
        Assert.Equal(0, context.DisposeCount);
        Assert.Equal(0, context.DisposeAsyncCount);
    }

    [pengdows.crud.attributes.Table("semver_entity")]
    public class SemVerEntity
    {
        [pengdows.crud.attributes.Id]
        [pengdows.crud.attributes.Column("id", System.Data.DbType.Int32)]
        public int Id { get; set; }

        [pengdows.crud.attributes.PrimaryKey(1)]
        [pengdows.crud.attributes.Column("name", System.Data.DbType.String)]
        public string Name { get; set; } = string.Empty;
    }
}
