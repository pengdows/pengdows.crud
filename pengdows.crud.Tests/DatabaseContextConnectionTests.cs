using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class DatabaseContextConnectionTests
{
    [Fact]
    public void SingleConnectionReadOnlyMemoryContext_Throws()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadOnly,
            ProviderName = "fake"
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new DatabaseContext(config, factory, NullLoggerFactory.Instance));
        Assert.Contains("In-memory databases that use SingleConnection mode require a read-write context", ex.Message);
    }

    [Fact]
    public void ApplyPersistentConnectionSessionSettings_ExecutesStatements()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            DbMode = DbMode.SingleWriter,
            ProviderName = "fake"
        };

        using var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);
        var connection = new CapturingConnection();

        context.ApplyPersistentConnectionSessionSettings(connection);

        Assert.Contains("PRAGMA foreign_keys = ON", connection.ExecutedStatements);
    }

    private sealed class CapturingConnection : IDbConnection
    {
        public List<string> ExecutedStatements { get; } = new();
        private string _connectionString = string.Empty;
        [AllowNull]
        public string ConnectionString
        {
            get => _connectionString;
            set => _connectionString = value ?? string.Empty;
        }
        public int ConnectionTimeout => 30;
        public string Database => "capturing";
        public ConnectionState State => ConnectionState.Open;
        public string DataSource => "capturing";
        public IDbTransaction BeginTransaction() => throw new NotSupportedException();
        public IDbTransaction BeginTransaction(IsolationLevel il) => throw new NotSupportedException();
        public void ChangeDatabase(string databaseName) { }
        public void Close() { }
        public void Open() { }
        public IDbCommand CreateCommand() => new CapturingCommand(ExecutedStatements);
        public void Dispose() { }
    }

    private sealed class CapturingCommand : IDbCommand
    {
        private readonly List<string> _executed;

        public CapturingCommand(List<string> executed)
        {
            _executed = executed;
        }

        private string _commandText = string.Empty;
        [AllowNull]
        public string CommandText
        {
            get => _commandText;
            set => _commandText = value ?? string.Empty;
        }
        public int CommandTimeout { get; set; }
        public CommandType CommandType { get; set; } = CommandType.Text;
        public IDbConnection? Connection { get; set; }
        public IDataParameterCollection Parameters => throw new NotSupportedException();
        public IDbTransaction? Transaction { get; set; }
        public UpdateRowSource UpdatedRowSource { get; set; }
        public void Cancel() { }
        public IDbDataParameter CreateParameter() => throw new NotSupportedException();
        public void Dispose() { }
        public int ExecuteNonQuery()
        {
            _executed.Add(CommandText.Trim());
            return 0;
        }

        public IDataReader ExecuteReader() => throw new NotSupportedException();
        public IDataReader ExecuteReader(CommandBehavior behavior) => throw new NotSupportedException();
        public object ExecuteScalar() => throw new NotSupportedException();
        public void Prepare() { }
    }
}
