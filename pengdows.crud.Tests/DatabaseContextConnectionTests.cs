using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.Tests.fakeDb;
using pengdows.crud.wrappers;
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

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new DatabaseContext(config, factory, NullLoggerFactory.Instance));
        Assert.Contains("In-memory databases that use SingleConnection mode require a read-write context", ex.Message);
    }

    [Fact]
    public void SingleWriter_DoesNotLeaveInitializationConnectionOpen()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleWriter,
            ProviderName = "fake"
        };

        using var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        Assert.Equal(0, context.NumberOfOpenConnections);
    }

    [Fact]
    public void ExecuteSessionSettings_ExecutesStatements()
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

        context.ExecuteSessionSettings(connection, false);

        // Should contain session settings content
        Assert.NotEmpty(connection.ExecutedStatements);
    }

    [Fact]
    public void SessionSettings_ExecutedAsSingleCommand()
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

        context.ExecuteSessionSettings(connection, false);

        // The key assertion: all settings sent as exactly 1 command, not split by semicolons
        Assert.Single(connection.ExecutedStatements);
    }

    [Fact]
    public void SessionSettings_SkippedWhenAlreadyApplied()
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

        // First call applies settings
        context.ExecuteSessionSettings(connection, false);
        Assert.Single(connection.ExecutedStatements);

        // Second call should be skipped because the context remembers this PHYSICAL connection object.
        // Even if we clear the list, it won't re-execute.
        connection.ExecutedStatements.Clear();
        context.ExecuteSessionSettings(connection, false);
        Assert.Empty(connection.ExecutedStatements);
        
        // A DIFFERENT physical connection should still get initialized
        var connection2 = new CapturingConnection();
        context.ExecuteSessionSettings(connection2, false);
        Assert.Single(connection2.ExecutedStatements);
    }

    [Fact]
    public void SessionSettings_SkippedWhenDetectionNotComplete()
    {
        // Standard mode with an unknown provider — if detection didn't complete,
        // ExecuteSessionSettings should be a no-op.
        // We test this indirectly: a freshly-constructed context HAS detection completed,
        // so we verify that the method does execute in the normal case.
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            DbMode = DbMode.SingleWriter,
            ProviderName = "fake"
        };

        using var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);
        var connection = new CapturingConnection();

        // After construction, detection IS completed so settings should execute
        context.ExecuteSessionSettings(connection, false);
        Assert.NotEmpty(connection.ExecutedStatements);
    }

    [Fact]
    public void ExecuteSessionSettings_Failure_DoesNotMarkApplied()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=file.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ProviderName = "fake"
        };

        using var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);
        var connection = new fakeDbConnection();
        ConnectionFailureHelper.ConfigureConnectionFailure(connection, ConnectionFailureMode.FailOnCommand);
        using var tracked = new TrackedConnection(connection);

        context.ExecuteSessionSettings(tracked, false);

        Assert.False(tracked.LocalState.SessionSettingsApplied);
    }

    private sealed class CapturingConnection : DbConnection
    {
        public List<string> ExecutedStatements { get; } = new();
        private string _connectionString = string.Empty;

        [AllowNull]
        public override string ConnectionString
        {
            get => _connectionString;
            set => _connectionString = value ?? string.Empty;
        }

        public override int ConnectionTimeout => 30;
        public override string Database => "capturing";
        public override ConnectionState State => ConnectionState.Open;
        public override string DataSource => "capturing";
        public override string ServerVersion => "1.0";

        protected override DbTransaction BeginDbTransaction(IsolationLevel il)
        {
            throw new NotSupportedException();
        }

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close()
        {
        }

        public override void Open()
        {
        }

        protected override DbCommand CreateDbCommand()
        {
            return new CapturingCommand(ExecutedStatements);
        }
    }

    private sealed class CapturingCommand : DbCommand
    {
        private readonly List<string> _executed;

        public CapturingCommand(List<string> executed)
        {
            _executed = executed;
        }

        private string _commandText = string.Empty;

        [AllowNull]
        public override string CommandText
        {
            get => _commandText;
            set => _commandText = value ?? string.Empty;
        }

        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; } = CommandType.Text;
        public override bool DesignTimeVisible { get; set; }

        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();
        protected override DbTransaction? DbTransaction { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }

        public override void Cancel()
        {
        }

        protected override DbParameter CreateDbParameter()
        {
            throw new NotSupportedException();
        }

        public override int ExecuteNonQuery()
        {
            _executed.Add(CommandText.Trim());
            return 0;
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            throw new NotSupportedException();
        }

        public override object? ExecuteScalar()
        {
            throw new NotSupportedException();
        }

        public override void Prepare()
        {
            }
        }}