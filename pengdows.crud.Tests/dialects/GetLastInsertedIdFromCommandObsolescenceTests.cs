using System;
using System.Reflection;
using pengdows.crud.dialects;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// ISqlDialect.GetLastInsertedIdFromCommand takes a raw DbCommand, and DbCommand.Connection leads straight to a
/// connection: nothing public may expose one. 3.0 makes it internal; on 2.0.x it is obsolete (the library still uses it
/// internally for generated keys).
/// </summary>
public class GetLastInsertedIdFromCommandObsolescenceTests
{
    [Fact]
    public void TheInterfaceMember_IsObsolete_RemovedFromThePublicSurfaceIn3_0()
    {
        var method = typeof(ISqlDialect).GetMethod("GetLastInsertedIdFromCommand");
        Assert.NotNull(method);

        var obsolete = method!.GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotNull(obsolete);
        Assert.False(obsolete!.IsError);
        Assert.Contains("3.0", obsolete.Message, StringComparison.Ordinal);
        Assert.Contains("connection", obsolete.Message, StringComparison.OrdinalIgnoreCase);
    }
}
