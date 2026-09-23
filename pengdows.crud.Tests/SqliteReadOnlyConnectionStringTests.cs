#region

using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class SqliteReadOnlyConnectionStringTests
{
    private sealed class Conn : IDbConnection
    {
        public string ConnectionString { get; set; } = string.Empty;
        public int ConnectionTimeout => 0; public string Database => string.Empty; public ConnectionState State => ConnectionState.Closed; public string DataSource => string.Empty;
        public void ChangeDatabase(string databaseName) { } public void Close() { } public IDbTransaction BeginTransaction() => null!; public IDbTransaction BeginTransaction(IsolationLevel il) => null!; public void Open() { } public void Dispose() { }
        public IDbCommand CreateCommand() => new fakeDbCommand();
    }

    [Fact]
    public void ApplyConnectionSettings_ReadOnly_AddsModeReadOnly_ForFileDb()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new SqliteDialect(factory, NullLogger<SqliteDialect>.Instance);
        using var ctx = new DatabaseContext("Data Source=file.db;EmulatedProduct=Sqlite", factory);
        var conn = new Conn();

        dialect.ApplyConnectionSettings(conn, ctx, readOnly: true);

        Assert.Contains("Mode=ReadOnly", conn.ConnectionString);
    }

    [Fact]
    public void GetConnectionSessionSettings_FileDb_ReadWrite_IncludesWal()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new SqliteDialect(factory, NullLogger<SqliteDialect>.Instance);
        using var ctx = new DatabaseContext("Data Source=file.db;EmulatedProduct=Sqlite", factory);

        var settings = dialect.GetConnectionSessionSettings(ctx, readOnly: false);

        Assert.Contains("PRAGMA foreign_keys = ON;", settings);
        Assert.Contains("PRAGMA journal_mode = WAL;", settings);
    }

    [Fact]
    public void GetConnectionSessionSettings_InMemory_ReadWrite_DoesNotIncludeWal()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new SqliteDialect(factory, NullLogger<SqliteDialect>.Instance);
        using var ctx = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory);

        var settings = dialect.GetConnectionSessionSettings(ctx, readOnly: false);

        Assert.Contains("PRAGMA foreign_keys = ON;", settings);
        Assert.DoesNotContain("PRAGMA journal_mode = WAL;", settings);
    }

    [Fact]
    public void GetConnectionSessionSettings_FileDb_ReadOnly_DoesNotIncludeWal()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new SqliteDialect(factory, NullLogger<SqliteDialect>.Instance);
        using var ctx = new DatabaseContext("Data Source=file.db;EmulatedProduct=Sqlite", factory);

        var settings = dialect.GetConnectionSessionSettings(ctx, readOnly: true);

        Assert.Contains("PRAGMA foreign_keys = ON;", settings);
        Assert.Contains("PRAGMA query_only = ON;", settings);
        Assert.DoesNotContain("PRAGMA journal_mode = WAL;", settings);
    }
}
