using System;
using System.Data;
using System.Reflection;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// The gateway interfaces are what callers program to and fake. AuditCreationPolicy uses a normal
/// setter so DispatchProxy can implement the interface; an init accessor carries modreq(IsExternalInit),
/// which previously caused TypeLoadException ("Signature of the body and declaration in a method
/// implementation do not match"). Found by pengdows.hangfire's test doubles of
/// IDistributedLockGateway : ITableGateway&lt;,&gt;.
/// </summary>
public sealed class GatewayInterfaceProxyTests
{
    [Table("proxy_rows")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int64)] public long Id { get; set; }
        [PrimaryKey(1)] [Column("code", DbType.String)] public string Code { get; set; } = "";
    }

    public class Passthrough : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }

    [Fact]
    public void TableGatewayInterface_CanBeProxied()
    {
        var proxy = DispatchProxy.Create<ITableGateway<Row, long>, Passthrough>();

        Assert.NotNull(proxy);
    }

    [Fact]
    public void PrimaryKeyTableGatewayInterface_CanBeProxied()
    {
        var proxy = DispatchProxy.Create<IPrimaryKeyTableGateway<Row>, Passthrough>();

        Assert.NotNull(proxy);
    }

    [Fact]
    public void AuditCreationPolicy_IsStillSetAtConstruction()
    {
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite",
            new pengdows.crud.fakeDb.fakeDbFactory(pengdows.crud.enums.SupportedDatabase.Sqlite));
        ITableGateway<Row, long> gateway = new TableGateway<Row, long>(context)
        {
            AuditCreationPolicy = AuditCreationPolicy.Authoritative
        };

        Assert.Equal(AuditCreationPolicy.Authoritative, gateway.AuditCreationPolicy);
    }
}
