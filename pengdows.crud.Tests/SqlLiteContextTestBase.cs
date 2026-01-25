#region

using System;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

[Collection("SqliteSerial")]
public class SqlLiteContextTestBase : IAsyncLifetime

{
    protected SqlLiteContextTestBase()
    {
        TypeMap = new TypeMapRegistry();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.EnableDataPersistence = true;
        Context = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory, TypeMap);
        AuditValueResolver = new StubAuditValueResolver("test-user");
    }

    public TypeMapRegistry TypeMap { get; }
    public IDatabaseContext Context { get; }
    public IAuditValueResolver AuditValueResolver { get; }

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (Context is IAsyncDisposable asyncDisp)
        {
            await asyncDisp.DisposeAsync();
        }
        else if (Context is IDisposable disp)
        {
            disp.Dispose();
        }
    }
}