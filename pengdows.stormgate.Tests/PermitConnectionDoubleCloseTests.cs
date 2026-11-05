using System;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.stormgate.Tests;

// A provider may throw when Close is called on a connection that is already closed. The wrapper
// must never make that call, so the misbehaving provider cannot take the caller down through a
// later Close, Dispose, CloseAsync or DisposeAsync.
public class PermitConnectionDoubleCloseTests
{
    private const string ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite";


    [Fact]
    public async Task Close_AfterClose_DoesNotCallTheProviderClose()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var inner = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite };
        factory.Connections.Add(inner);
        using var gate = new StormGate(new FakeDbDataSource(ConnectionString, factory), 1, TimeSpan.FromMilliseconds(50));

        var conn = await gate.OpenAsync();
        conn.Close();
        var closesBefore = inner.CloseCount;
        inner.SetFailOnClose(new InvalidOperationException("provider throws on close of a closed connection"));

        var ex = Record.Exception(() => conn.Close());

        Assert.Null(ex);
        Assert.Equal(closesBefore, inner.CloseCount);
    }

    [Fact]
    public async Task Dispose_AfterClose_DoesNotCallTheProviderClose()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var inner = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite };
        factory.Connections.Add(inner);
        using var gate = new StormGate(new FakeDbDataSource(ConnectionString, factory), 1, TimeSpan.FromMilliseconds(50));

        var conn = await gate.OpenAsync();
        conn.Close();
        var closesBefore = inner.CloseCount;
        inner.SetFailOnClose(new InvalidOperationException("provider throws on close of a closed connection"));

        var ex = Record.Exception(() => conn.Dispose());

        Assert.Null(ex);
        Assert.Equal(closesBefore, inner.CloseCount);
    }

    [Fact]
    public async Task CloseAsync_AfterClose_DoesNotCallTheProviderClose()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var inner = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite };
        factory.Connections.Add(inner);
        using var gate = new StormGate(new FakeDbDataSource(ConnectionString, factory), 1, TimeSpan.FromMilliseconds(50));

        var conn = await gate.OpenAsync();
        conn.Close();
        var closesBefore = inner.CloseCount;
        inner.SetFailOnClose(new InvalidOperationException("provider throws on close of a closed connection"));

        var ex = await Record.ExceptionAsync(() => conn.CloseAsync());

        Assert.Null(ex);
        Assert.Equal(closesBefore, inner.CloseCount);
    }

    [Fact]
    public async Task DisposeAsync_AfterClose_DoesNotCallTheProviderClose()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var inner = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite };
        factory.Connections.Add(inner);
        using var gate = new StormGate(new FakeDbDataSource(ConnectionString, factory), 1, TimeSpan.FromMilliseconds(50));

        var conn = await gate.OpenAsync();
        conn.Close();
        var closesBefore = inner.CloseCount;
        inner.SetFailOnClose(new InvalidOperationException("provider throws on close of a closed connection"));

        var ex = await Record.ExceptionAsync(async () => await conn.DisposeAsync());

        Assert.Null(ex);
        Assert.Equal(closesBefore, inner.CloseCount);
    }
}
