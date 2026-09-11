using System;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

public class AuditCreationPolicyTests : SqlLiteContextTestBase
{
    [Theory]
    [InlineData(typeof(ITableGateway<,>))]
    [InlineData(typeof(IPrimaryKeyTableGateway<>))]
    public void AuditCreationPolicy_IsInitOnly_NotRequestTimeMutable(Type gatewayInterfaceType)
    {
        // A singleton gateway is shared across every tenant/request that constructs it; a
        // request-time-settable AuditCreationPolicy (the old `{ get; set; }`) would let one
        // caller flip whether a resolver-forged CreatedBy can be overridden by untrusted input
        // for every other concurrent caller of the same shared gateway. Verified via the CLR's
        // own IsExternalInit modreq (how `init` accessors are actually encoded in IL) rather
        // than just trusting the C# keyword at the source level — this is what genuinely
        // prevents `gateway.AuditCreationPolicy = x;` from compiling anywhere outside the
        // declaring type's own constructor/object-initializer expression.
        var property = gatewayInterfaceType.GetProperty(nameof(ITableGateway<object, int>.AuditCreationPolicy))
                       ?? throw new InvalidOperationException(
                           $"{gatewayInterfaceType.Name} has no AuditCreationPolicy property.");
        var setMethod = property.SetMethod
                         ?? throw new InvalidOperationException("AuditCreationPolicy has no setter.");

        var isInitOnly = setMethod.ReturnParameter
            .GetRequiredCustomModifiers()
            .Any(m => m == typeof(System.Runtime.CompilerServices.IsExternalInit));

        Assert.True(isInitOnly,
            "AuditCreationPolicy must be an init-only property so a shared/singleton gateway's " +
            "policy cannot be mutated after construction by one caller and observed by another.");
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
