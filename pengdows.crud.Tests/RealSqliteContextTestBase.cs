#region

using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

[Collection("SqliteSerial")]
public class RealSqliteContextTestBase : IAsyncLifetime
{
    protected RealSqliteContextTestBase()
    {
        TypeMap = new TypeMapRegistry();
        Context = new DatabaseContext("Data Source=:memory:", SqliteFactory.Instance, TypeMap);
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