using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class PreventDatabaseUnloadTests
{
    [Fact]
    public void KeepAlive_IsCompatibilityAlias()
    {
#pragma warning disable CS0618 // KeepAlive is the obsolete alias under test
        Assert.Equal(DbMode.PreventDatabaseUnload, DbMode.KeepAlive);
#pragma warning restore CS0618
        Assert.Equal(1, (int)DbMode.PreventDatabaseUnload);
    }
}
