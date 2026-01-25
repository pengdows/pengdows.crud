#region

using System.Data;
using System.Diagnostics.CodeAnalysis;
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
        private string _connectionString = string.Empty;

        [AllowNull]
        public string ConnectionString
        {
            get => _connectionString;
            set => _connectionString = value ?? string.Empty;
        }

        public int ConnectionTimeout => 0;
        public string Database => string.Empty;
        public ConnectionState State => ConnectionState.Closed;
        public string DataSource => string.Empty;

        public void ChangeDatabase(string databaseName)
        {
        }

        public void Close()
        {
        }

        public IDbTransaction BeginTransaction()
        {
            return null!;
        }

        public IDbTransaction BeginTransaction(IsolationLevel il)
        {
            return null!;
        }

        public void Open()
        {
        }

        public void Dispose()
        {
        }

        public IDbCommand CreateCommand()
        {
            return new fakeDbCommand();
        }
    }

    [Fact]
    public void ApplyConnectionSettings_ReadOnly_AddsModeReadOnly_ForFileDb()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new SqliteDialect(factory, NullLogger<SqliteDialect>.Instance);
        using var ctx = new DatabaseContext("Data Source=file.db;EmulatedProduct=Sqlite", factory);
        var conn = new Conn();

        dialect.ApplyConnectionSettings(conn, ctx, true);

        Assert.Contains("Mode=ReadOnly", conn.ConnectionString);
    }
}