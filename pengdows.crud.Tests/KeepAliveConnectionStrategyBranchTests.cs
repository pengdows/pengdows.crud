using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.strategies.connection;
using Xunit;

namespace pengdows.crud.Tests;

public class KeepAliveConnectionStrategyBranchTests
{
    [Fact]
    public void GetConnection_OpenFailure_Rethrows_WhenDisposalAlsoFails()
    {
        var factory = new ToggleFailureDbFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=keepalive-branch.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        using var context = new DatabaseContext(config, factory);
        var strategy = new KeepAliveConnectionStrategy(context);

        factory.FailOpen = true;
        factory.ThrowOnDispose = true;

        var ex = Assert.Throws<InvalidOperationException>(() => strategy.GetConnection(ExecutionType.Read, false));
        Assert.Contains("open-fail", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HandleDialectDetection_WithoutInitOrPersistent_CreatesOwnedConnection()
    {
        var factory = new ToggleFailureDbFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=keepalive-owned.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        using var context = new DatabaseContext(config, factory);
        var strategy = new KeepAliveConnectionStrategy(context);

        var before = factory.CreatedCount;
        var result = strategy.HandleDialectDetection(null, null, NullLoggerFactory.Instance);

        Assert.Null(result.dialect);
        Assert.Null(result.dataSourceInfo);
        Assert.True(factory.CreatedCount > before);
        Assert.True(factory.DisposeCount > 0);
    }

    [Fact]
    public void HandleDialectDetection_WhenOwnedConnectionOpenFails_ReturnsNull()
    {
        var factory = new ToggleFailureDbFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=keepalive-catch.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        using var context = new DatabaseContext(config, factory);
        var strategy = new KeepAliveConnectionStrategy(context);

        factory.FailOpen = true;
        var result = strategy.HandleDialectDetection(null, factory, NullLoggerFactory.Instance);

        Assert.Null(result.dialect);
        Assert.Null(result.dataSourceInfo);
    }

    [Fact]
    public void HandleDialectDetection_WhenOwnedConnectionDisposeFails_ReturnsNullWithoutThrowing()
    {
        var factory = new ToggleFailureDbFactory
        {
            ThrowOnDispose = true
        };
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=keepalive-dispose-fail.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        using var context = new DatabaseContext(config, factory);
        var strategy = new KeepAliveConnectionStrategy(context);

        var result = strategy.HandleDialectDetection(null, null, NullLoggerFactory.Instance);

        Assert.Null(result.dialect);
        Assert.Null(result.dataSourceInfo);
        Assert.True(factory.DisposeCount > 0);
    }

    private sealed class ToggleFailureDbFactory : DbProviderFactory
    {
        public bool FailOpen { get; set; }
        public bool ThrowOnDispose { get; set; }
        public int CreatedCount { get; private set; }
        public int DisposeCount { get; private set; }

        internal void OnDisposed()
        {
            DisposeCount++;
        }

        public override DbConnection CreateConnection()
        {
            CreatedCount++;
            return new ToggleFailureDbConnection(this);
        }

        public override DbCommand CreateCommand()
        {
            return new NoOpDbCommand();
        }

        public override DbConnectionStringBuilder CreateConnectionStringBuilder()
        {
            return new DbConnectionStringBuilder();
        }
    }

    private sealed class ToggleFailureDbConnection : DbConnection
    {
        private readonly ToggleFailureDbFactory _owner;
        private string _connectionString = string.Empty;
        private ConnectionState _state = ConnectionState.Closed;

        public ToggleFailureDbConnection(ToggleFailureDbFactory owner)
        {
            _owner = owner;
        }

        [AllowNull]
        public override string ConnectionString
        {
            get => _connectionString;
            set => _connectionString = value ?? string.Empty;
        }

        public override string Database => "keepalive";
        public override string DataSource => "keepalive";
        public override string ServerVersion => "1.0";
        public override int ConnectionTimeout => 0;
        public override ConnectionState State => _state;

        public override void Open()
        {
            if (_owner.FailOpen)
            {
                throw new InvalidOperationException("open-fail");
            }

            _state = ConnectionState.Open;
        }

        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            Open();
            return Task.CompletedTask;
        }

        public override void Close()
        {
            _state = ConnectionState.Closed;
        }

        protected override DbCommand CreateDbCommand()
        {
            return new NoOpDbCommand();
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            throw new NotSupportedException();
        }

        public override void ChangeDatabase(string databaseName)
        {
            throw new NotSupportedException();
        }

        public override DataTable GetSchema()
        {
            var table = new DataTable();
            table.Columns.Add("DataSourceProductName", typeof(string));
            var row = table.NewRow();
            row["DataSourceProductName"] = "Sqlite";
            table.Rows.Add(row);
            return table;
        }

        protected override void Dispose(bool disposing)
        {
            _owner.OnDisposed();
            if (disposing && _owner.ThrowOnDispose)
            {
                throw new InvalidOperationException("dispose-fail");
            }

            base.Dispose(disposing);
        }
    }

    private sealed class NoOpDbCommand : DbCommand
    {
        private sealed class EmptyParameterCollection : DbParameterCollection
        {
            public override int Count => 0;
            public override object SyncRoot => this;
            public override int Add(object value) => 0;
            public override void AddRange(Array values) { }
            public override void Clear() { }
            public override bool Contains(object value) => false;
            public override bool Contains(string value) => false;
            public override void CopyTo(Array array, int index) { }
            public override System.Collections.IEnumerator GetEnumerator() => Array.Empty<object>().GetEnumerator();
            public override int IndexOf(object value) => -1;
            public override int IndexOf(string parameterName) => -1;
            public override void Insert(int index, object value) { }
            public override void Remove(object value) { }
            public override void RemoveAt(int index) { }
            public override void RemoveAt(string parameterName) { }
            protected override DbParameter GetParameter(int index) => throw new IndexOutOfRangeException();
            protected override DbParameter GetParameter(string parameterName) => throw new IndexOutOfRangeException();
            protected override void SetParameter(int index, DbParameter value) { }
            protected override void SetParameter(string parameterName, DbParameter value) { }
        }

        private readonly DbParameterCollection _parameters = new EmptyParameterCollection();

        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction? DbTransaction { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        public override void Cancel() { }
        public override int ExecuteNonQuery() => 0;
        public override object? ExecuteScalar() => null;
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            throw new NotSupportedException();
        }
    }
}
