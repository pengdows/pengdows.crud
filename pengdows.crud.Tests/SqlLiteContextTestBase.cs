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
    }

    public TypeMapRegistry TypeMap { get; }
    public IDatabaseContext Context { get; }
}