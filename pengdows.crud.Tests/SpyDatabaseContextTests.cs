using System;
using Moq;
using Xunit;

namespace pengdows.crud.Tests;

public class SpyDatabaseContextTests : SqlLiteContextTestBase
{
    [Fact]
    public void TableGateway_WithContextMissingDialectProvider_Throws()
    {
        var map = new TypeMapRegistry();
        map.Register<NullableIdEntity>();

        var mockCtx = new Mock<IDatabaseContext>();
        mockCtx.SetupGet(c => c.TypeMapRegistry).Returns(map);
        mockCtx.As<IContextIdentity>().SetupGet(i => i.RootId).Returns(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => new TableGateway<NullableIdEntity, int?>(mockCtx.Object));
    }
}