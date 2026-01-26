using System;
using Moq;
using pengdows.crud.enums;
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
    public void TableGateway_ThrowsWhenContextMissingDialect()
    {
        var context = new Mock<IDatabaseContext>(MockBehavior.Strict);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new TableGateway<TestEntity, int>(context.Object));

        Assert.Contains("IDatabaseContext must implement ISqlDialectProvider", exception.Message);
    }
}
