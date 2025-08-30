#region

using Microsoft.Data.Sqlite;

#endregion

namespace pengdows.crud.Tests;

public class SqlLiteContextTestBase

{
    protected SqlLiteContextTestBase()
    {
        TypeMap = new TypeMapRegistry();
        Context = new DatabaseContext("Data Source=:memory:", SqliteFactory.Instance, TypeMap);
        AuditValueResolver = new StubAuditValueResolver("test-user");
    }

    public TypeMapRegistry TypeMap { get; }
    public IDatabaseContext Context { get; }
    public IAuditValueResolver AuditValueResolver { get; }
}