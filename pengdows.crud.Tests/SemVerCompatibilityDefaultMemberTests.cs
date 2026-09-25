using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Moq;
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
        { typeof(ISqlDialect), "get_" + nameof(ISqlDialect.IsClientServerDatabase) },
        { typeof(ISqlDialect), "get_" + nameof(ISqlDialect.IsEmbeddedSingleWriterEngine) },
        { typeof(ISqlDialect), nameof(ISqlDialect.DetectInMemoryKind) },
        { typeof(ISqlDialect), nameof(ISqlDialect.CoerceConnectionMode) },
        { typeof(ISqlDialect), "get_" + nameof(ISqlDialect.SavepointCapabilities) },
        { typeof(ISqlDialect), nameof(ISqlDialect.GetReleaseSavepointSql) },
        { typeof(ISqlDialect), "get_" + nameof(ISqlDialect.SupportsSemicolonStatementSeparator) },
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
    public void ISqlDialect_Defaults_MatchSqlDialectBase()
    {
        var dialect = new Mock<ISqlDialect> { CallBase = true };
        dialect.Setup(d => d.WrapObjectName(It.IsAny<string>())).Returns<string>(n => "\"" + n + "\"");

        Assert.True(dialect.Object.IsClientServerDatabase);
        Assert.False(dialect.Object.IsEmbeddedSingleWriterEngine);
        Assert.Equal(InMemoryKind.None, dialect.Object.DetectInMemoryKind("Data Source=:memory:"));
        Assert.True(dialect.Object.SupportsSemicolonStatementSeparator);
        Assert.Equal("RELEASE SAVEPOINT \"sp1\"", dialect.Object.GetReleaseSavepointSql("sp1"));
    }

    [Fact]
    public void ISqlDialect_CoerceConnectionMode_Default_ResolvesBestToStandardAndHonorsExplicit()
    {
        var dialect = new Mock<ISqlDialect> { CallBase = true };

        Assert.Equal(DbMode.Standard, dialect.Object.CoerceConnectionMode(DbMode.Best, null, false).Mode);
        Assert.Equal(DbMode.SingleWriter, dialect.Object.CoerceConnectionMode(DbMode.SingleWriter, null, false).Mode);
    }

    [Theory]
    [InlineData(true, SavepointCapabilities.Create | SavepointCapabilities.Rollback | SavepointCapabilities.Release)]
    [InlineData(false, SavepointCapabilities.None)]
    public void ISqlDialect_SavepointCapabilities_Default_FollowsSupportsSavepoints(bool supports,
        SavepointCapabilities expected)
    {
        var dialect = new Mock<ISqlDialect> { CallBase = true };
        dialect.SetupGet(d => d.SupportsSavepoints).Returns(supports);

        Assert.Equal(expected, dialect.Object.SavepointCapabilities);
    }

    [Fact]
    public async Task ITransactionContext_ReleaseSavepointAsync_Default_ThrowsNotSupported()
    {
        var txn = new Mock<ITransactionContext> { CallBase = true };

        await Assert.ThrowsAsync<NotSupportedException>(async () => await txn.Object.ReleaseSavepointAsync("sp1"));
    }

    [Theory]
    [InlineData("15.2", "15.2")]
    [InlineData("not a version", null)]
    public void IDataSourceInformation_ParsedVersion_Default_ParsesProductVersion(string productVersion,
        string? expected)
    {
        var info = new Mock<IDataSourceInformation> { CallBase = true };
        info.SetupGet(i => i.DatabaseProductVersion).Returns(productVersion);

        Assert.Equal(expected == null ? null : Version.Parse(expected), info.Object.ParsedVersion);
    }

    [Fact]
    public void IDatabaseContextConfiguration_NewMembers_Default_ReturnDefaultsAndAcceptOnlyDefaults()
    {
        var config = new Mock<IDatabaseContextConfiguration> { CallBase = true }.Object;

        Assert.Equal(SessionInitializationFailureMode.BestEffort, config.SessionInitializationFailureMode);
        Assert.Null(config.MaxQueuedReads);
        Assert.Null(config.MaxQueuedWrites);

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
        var gateway = new Mock<ITableGateway<SemVerEntity, int>> { CallBase = true }.Object;

        Assert.Equal(AuditCreationPolicy.PreserveExplicitValues, gateway.AuditCreationPolicy);
        gateway.AuditCreationPolicy = AuditCreationPolicy.PreserveExplicitValues;
        Assert.Throws<NotSupportedException>(() => gateway.AuditCreationPolicy = AuditCreationPolicy.Authoritative);
    }

    [Fact]
    public void IPrimaryKeyTableGateway_AuditCreationPolicy_Default_PreservesAndAcceptsOnlyDefault()
    {
        var gateway = new Mock<IPrimaryKeyTableGateway<SemVerEntity>> { CallBase = true }.Object;

        Assert.Equal(AuditCreationPolicy.PreserveExplicitValues, gateway.AuditCreationPolicy);
        gateway.AuditCreationPolicy = AuditCreationPolicy.PreserveExplicitValues;
        Assert.Throws<NotSupportedException>(() => gateway.AuditCreationPolicy = AuditCreationPolicy.Authoritative);
    }

    [Fact]
    public async Task ITenantContextRegistry_AcquireLease_Default_WrapsGetContextWithoutDisposingIt()
    {
        var context = new Mock<IDatabaseContext>();
        var registry = new Mock<ITenantContextRegistry> { CallBase = true };
        registry.Setup(r => r.GetContext("t1")).Returns(context.Object);

        using (var lease = registry.Object.AcquireLease("t1"))
        {
            Assert.Same(context.Object, lease.Context);
        }

        await using (var asyncLease = registry.Object.AcquireLease("t1"))
        {
            Assert.Same(context.Object, asyncLease.Context);
        }

        context.Verify(c => c.Dispose(), Times.Never);
        context.Verify(c => c.DisposeAsync(), Times.Never);
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
