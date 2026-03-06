using System;
using System.Reflection;
using Moq;
using pengdows.crud.@internal;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public class TableGatewayCompatibilityTests
{
    [Fact]
    public void TableGateway_ImplementsITableGateway()
    {
        var typeMap = new TypeMapRegistry();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory, typeMap);

        var gateway = new TableGateway<TestEntity, int>(context);

        Assert.IsAssignableFrom<ITableGateway<TestEntity, int>>(gateway);
        Assert.Contains("Test", gateway.WrappedTableName, StringComparison.Ordinal);
    }

    [Fact]
    public void TableGateway_DoesNotContain_ResolveUpsertKey_MOVED()
    {
        // ResolveUpsertKey() was moved to TableGateway.Upsert.cs.
        // The _MOVED-suffixed dead-code copy in TableGateway.Core.cs must not exist.
        var method = typeof(TableGateway<TestEntity, int>).GetMethod(
            "ResolveUpsertKey_MOVED",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.Null(method);
    }

    [Fact]
    public void TableGateway_ThrowsWhenContextDialectIsNull()
    {
        var typeMap = new TypeMapRegistry();
        var context = new Mock<IDatabaseContext>(MockBehavior.Strict);
        context.As<ITypeMapAccessor>().SetupGet(a => a.TypeMapRegistry).Returns(typeMap);
        context.As<ISqlDialectProvider>().SetupGet(c => c.Dialect).Returns((ISqlDialect?)null!);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new TableGateway<TestEntity, int>(context.Object));

        Assert.Contains("IDatabaseContext must expose a non-null Dialect", exception.Message);
    }
}
