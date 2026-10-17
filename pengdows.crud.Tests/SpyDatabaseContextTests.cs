using System;
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

        var ctx = new ScriptedIdentityDatabaseContext { TypeMapRegistry = map };

        Assert.Throws<InvalidOperationException>(() => new TableGateway<NullableIdEntity, int?>(ctx));
    }
}