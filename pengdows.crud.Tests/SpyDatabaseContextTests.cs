using System;
using Moq;
using pengdows.crud.@internal;
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
        mockCtx.As<ITypeMapAccessor>().SetupGet(a => a.TypeMapRegistry).Returns(map);
        mockCtx.As<IContextIdentity>().SetupGet(i => i.RootId).Returns(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => new TableGateway<NullableIdEntity, int?>(mockCtx.Object));
    }
}
