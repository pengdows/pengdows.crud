using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class TableGatewayCompatibilityTests
{
    [Fact]
    public void EntityHelper_ImplementsITableGateway()
    {
        Assert.True(
            typeof(ITableGateway<TestEntitySimple, int>).IsAssignableFrom(typeof(EntityHelper<TestEntitySimple, int>)));
    }

    [Fact]
    public void IEntityHelper_IsAssignableToITableGateway()
    {
#pragma warning disable CS0618
        Assert.True(
            typeof(ITableGateway<TestEntitySimple, int>).IsAssignableFrom(
                typeof(IEntityHelper<TestEntitySimple, int>)));
#pragma warning restore CS0618
    }

    [Fact]
    public void BuildCreate_QueryMatchesBetweenInterfaces()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<TestEntitySimple>();

        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite",
            new fakeDbFactory(SupportedDatabase.Sqlite), typeMap);
        var helper = new EntityHelper<TestEntitySimple, int>(context, new StubAuditValueResolver("user"));
        var tableGateway = (ITableGateway<TestEntitySimple, int>)helper;
#pragma warning disable CS0618
        var legacyInterface = (IEntityHelper<TestEntitySimple, int>)tableGateway;
#pragma warning restore CS0618

        var entity = new TestEntitySimple { Id = 1, Name = "value" };

        var gatewaySql = tableGateway.BuildCreate(entity, context).Query.ToString();
        var legacySql = legacyInterface.BuildCreate(entity, context).Query.ToString();

        Assert.Equal(legacySql, gatewaySql);
    }
}