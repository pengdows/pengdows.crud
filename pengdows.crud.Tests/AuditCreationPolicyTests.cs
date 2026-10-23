using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

public class AuditCreationPolicyTests : SqlLiteContextTestBase
{
    // A singleton gateway is shared by every tenant/request, so a request-time-settable policy
    // would let one caller change whether a resolver-supplied CreatedBy can be overridden for every
    // other caller. Set it once at construction (object initializer). Checked through the CLR's
    // A normal setter keeps the interface proxyable while preserving object-initializer configuration.
    [Fact]
    public void AuditCreationPolicy_IsSettable_OnTheGateway()
    {
        var gatewayType = typeof(BaseTableGateway<>);
        var setMethod = gatewayType.GetProperty(nameof(ITableGateway<object, int>.AuditCreationPolicy))?.SetMethod
                        ?? throw new InvalidOperationException($"{gatewayType.Name}.AuditCreationPolicy has no setter.");

        Assert.Empty(setMethod.GetParameters()[0].GetRequiredCustomModifiers());
    }

    // The interfaces expose a normal setter so callers can use the existing object-initializer contract
    // and DispatchProxy can implement the interface.
    [Theory]
    [InlineData(typeof(ITableGateway<,>))]
    [InlineData(typeof(IPrimaryKeyTableGateway<>))]
    public void AuditCreationPolicy_HasProxyableSetter_OnTheInterfaces(Type gatewayType)
    {
        var property = gatewayType.GetProperty(nameof(ITableGateway<object, int>.AuditCreationPolicy))
                       ?? throw new InvalidOperationException($"{gatewayType.Name} has no AuditCreationPolicy.");

        Assert.NotNull(property.SetMethod);
        Assert.Empty(property.SetMethod!.GetParameters()[0].GetRequiredCustomModifiers());
    }

    [Table("AuditPolicyUser")]
    private class AuditPolicyUserEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey(1)]
        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [CreatedBy]
        [Column("CreatedBy", DbType.String)]
        public string CreatedBy { get; set; } = string.Empty;
    }

    [Fact]
    public async Task Authoritative_OverwritesExplicitlySetCreatedBy()
    {
        TypeMap.Register<AuditPolicyUserEntity>();
        var gateway = new TableGateway<AuditPolicyUserEntity, int>(Context, AuditValueResolver)
        {
            AuditCreationPolicy = AuditCreationPolicy.Authoritative
        };
        await CreateAuditPolicyUserTable();

        var entity = new AuditPolicyUserEntity
        {
            Name = Guid.NewGuid().ToString(),
            CreatedBy = "attacker-supplied"
        };

        await gateway.CreateAsync(entity, Context);

        // Overwritten despite the pre-set value — Authoritative means the resolver always wins.
        Assert.Equal("test-user", entity.CreatedBy);
    }

    [Fact]
    public async Task PreserveExplicitValues_KeepsExplicitlySetCreatedBy()
    {
        TypeMap.Register<AuditPolicyUserEntity>();
        var gateway = new TableGateway<AuditPolicyUserEntity, int>(Context, AuditValueResolver);
        // Default policy, not overridden.
        Assert.Equal(AuditCreationPolicy.PreserveExplicitValues, gateway.AuditCreationPolicy);

        await CreateAuditPolicyUserTable();

        var entity = new AuditPolicyUserEntity
        {
            Name = Guid.NewGuid().ToString(),
            CreatedBy = "explicit-caller-value"
        };

        await gateway.CreateAsync(entity, Context);

        // Regression guard: today's behavior — a pre-set non-default value is preserved.
        Assert.Equal("explicit-caller-value", entity.CreatedBy);
    }

    private async Task CreateAuditPolicyUserTable()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var sql = string.Format(
            @"CREATE TABLE IF NOT EXISTS {0}AuditPolicyUser{1} (
                {0}Id{1} INTEGER PRIMARY KEY AUTOINCREMENT,
                {0}Name{1} TEXT UNIQUE NOT NULL,
                {0}CreatedBy{1} TEXT NOT NULL
            )", qp, qs);
        var container = Context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }
}
