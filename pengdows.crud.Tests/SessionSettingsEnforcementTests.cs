using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Data;
using System.Data.Common;
using System.Reflection;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class SessionSettingsEnforcementTests
{
    private static readonly string[] PostgresSessionStatements =
    {
        "SET standard_conforming_strings = on",
        "SET client_min_messages = warning"
    };

    [Fact]
    public void DatabaseContext_FirstOpenHandler_ExecutesDialectSettings_WhenDialectAvailable()
    {
        var factory = new FakeDbProviderFactory("PostgreSQL");
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        using var context = new DatabaseContext(config, factory);
        var tracked = (TrackedConnection)context.GetConnection(ExecutionType.Read);
        tracked.Open();

        var inner = GetInnerConnection(tracked);
        Assert.Equal(PostgresSessionStatements, inner.CommandLog);
    }

    [Fact]
    public void DatabaseContext_SplitsMultiStatementSettings_Correctly()
    {
        var factory = new FakeDbProviderFactory("PostgreSQL");
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        using var context = new DatabaseContext(config, factory);
        var tracked = (TrackedConnection)context.GetConnection(ExecutionType.Read);
        tracked.Open();

        var inner = GetInnerConnection(tracked);
        Assert.Equal(2, inner.CommandLog.Count);
        Assert.Equal("SET standard_conforming_strings = on", inner.CommandLog[0]);
        Assert.Equal("SET client_min_messages = warning", inner.CommandLog[1]);
    }

    [Fact]
    public void DatabaseContext_FirstOpenHandler_ExecutesHeuristicSettings_WhenDialectNull()
    {
        var factory = new FakeDbProviderFactory("SQLite");
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        using var context = new DatabaseContext(config, factory);
        var tracked = (TrackedConnection)context.PersistentConnection!;
        var inner = GetInnerConnection(tracked);

        Assert.Contains("PRAGMA foreign_keys = ON", inner.CommandLog);
    }

    [Theory]
    [InlineData(DbMode.Standard)]
    [InlineData(DbMode.KeepAlive)]
    [InlineData(DbMode.SingleConnection)]
    [InlineData(DbMode.SingleWriter)]
    public void DatabaseContext_SessionSettings_AppliedOnOpen_AcrossStrategies(DbMode mode)
    {
        var factory = new FakeDbProviderFactory("PostgreSQL");
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;",
            DbMode = mode,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        using var context = new DatabaseContext(config, factory);
        var tracked = (TrackedConnection)context.GetConnection(ExecutionType.Read);
        tracked.Open();

        var inner = GetInnerConnection(tracked);
        Assert.Contains("SET standard_conforming_strings = on", inner.CommandLog);
        Assert.Contains("SET client_min_messages = warning", inner.CommandLog);
    }

    [Fact]
    public void DatabaseContext_FirstOpenHandler_ExecutesOncePerConnection()
    {
        var factory = new FakeDbProviderFactory("PostgreSQL");
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        using var context = new DatabaseContext(config, factory);
        var tracked = (TrackedConnection)context.GetConnection(ExecutionType.Read);
        tracked.Open();

        var inner = GetInnerConnection(tracked);
        var before = inner.CommandLog.Count;

        tracked.Open();

        Assert.Equal(before, inner.CommandLog.Count);
    }

    private static FakeDbConnection GetInnerConnection(TrackedConnection tracked)
    {
        var field = typeof(TrackedConnection).GetField("_connection", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (FakeDbConnection)field.GetValue(tracked)!;
    }

    private sealed class FakeDbProviderFactory : DbProviderFactory
    {
        private readonly string _productName;

        public FakeDbProviderFactory(string productName)
        {
            _productName = productName;
        }

        public override DbConnection CreateConnection()
        {
            return new FakeDbConnection(_productName);
        }

        public override DbCommand CreateCommand()
        {
            return new FakeDbCommand();
        }

        public override DbParameter CreateParameter()
        {
            return new FakeDbParameter();
        }

        public override DbConnectionStringBuilder CreateConnectionStringBuilder()
        {
            return new DbConnectionStringBuilder();
        }
    }

    private sealed class FakeDbConnection : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;
        private readonly string _productName;

        public FakeDbConnection(string productName)
        {
            _productName = productName;
        }

        public List<string> CommandLog { get; } = new();

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => _productName;
        public override string DataSource => "FakeSource";
        public override string ServerVersion => "1.0";
        public override int ConnectionTimeout => 0;
        public override ConnectionState State => _state;

        public override void Open()
        {
            if (_state == ConnectionState.Open)
            {
                return;
            }
            var original = _state;
            _state = ConnectionState.Open;
            OnStateChange(new StateChangeEventArgs(original, _state));
        }

        public override void Close()
        {
            if (_state == ConnectionState.Closed)
            {
                return;
            }
            var original = _state;
            _state = ConnectionState.Closed;
            OnStateChange(new StateChangeEventArgs(original, _state));
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            throw new NotSupportedException();
        }

        public override void ChangeDatabase(string databaseName)
        {
            throw new NotSupportedException();
        }

        protected override DbCommand CreateDbCommand()
        {
            return new FakeDbCommand(this);
        }

        public override DataTable GetSchema()
        {
            return BuildDataSourceInformationTable();
        }

        public override DataTable GetSchema(string collectionName)
        {
            return BuildDataSourceInformationTable();
        }

        private DataTable BuildDataSourceInformationTable()
        {
            var table = new DataTable();
            table.Columns.Add("DataSourceProductName", typeof(string));
            var row = table.NewRow();
            row["DataSourceProductName"] = _productName;
            table.Rows.Add(row);
            return table;
        }
    }

    private sealed class FakeDbCommand : DbCommand
    {
        private readonly FakeDbConnection? _connection;
        private readonly FakeDbParameterCollection _parameters = new();

        public FakeDbCommand()
        {
        }

        public FakeDbCommand(FakeDbConnection connection)
        {
            _connection = connection;
            DbConnection = connection;
        }

        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery()
        {
            if (_connection != null)
            {
                _connection.CommandLog.Add(CommandText);
            }
            return 0;
        }

        public override object? ExecuteScalar()
        {
            return null;
        }

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter()
        {
            return new FakeDbParameter();
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        [AllowNull]
        public override string ParameterName { get; set; } = string.Empty;
        [AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;
        public override bool SourceColumnNullMapping { get; set; }
        public override object? Value { get; set; }
        public override int Size { get; set; }

        public override void ResetDbType()
        {
        }
    }

    private sealed class FakeDbParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _items = new();

        public override int Count => _items.Count;
        public override object SyncRoot => ((ICollection)_items).SyncRoot;

        public override int Add(object value)
        {
            _items.Add((DbParameter)value);
            return _items.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values)
            {
                Add(value);
            }
        }

        public override void Clear()
        {
            _items.Clear();
        }

        public override bool Contains(object value)
        {
            return _items.Contains((DbParameter)value);
        }

        public override bool Contains(string value)
        {
            return _items.Exists(item => item.ParameterName == value);
        }

        public override void CopyTo(Array array, int index)
        {
            ((ICollection)_items).CopyTo(array, index);
        }

        public override IEnumerator GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        public override int IndexOf(object value)
        {
            return _items.IndexOf((DbParameter)value);
        }

        public override int IndexOf(string parameterName)
        {
            return _items.FindIndex(item => item.ParameterName == parameterName);
        }

        public override void Insert(int index, object value)
        {
            _items.Insert(index, (DbParameter)value);
        }

        public override void Remove(object value)
        {
            _items.Remove((DbParameter)value);
        }

        public override void RemoveAt(int index)
        {
            _items.RemoveAt(index);
        }

        public override void RemoveAt(string parameterName)
        {
            var index = IndexOf(parameterName);
            if (index >= 0)
            {
                _items.RemoveAt(index);
            }
        }

        protected override DbParameter GetParameter(int index)
        {
            return _items[index];
        }

        protected override DbParameter GetParameter(string parameterName)
        {
            var index = IndexOf(parameterName);
            return index >= 0 ? _items[index] : new FakeDbParameter();
        }

        protected override void SetParameter(int index, DbParameter value)
        {
            _items[index] = value;
        }

        protected override void SetParameter(string parameterName, DbParameter value)
        {
            var index = IndexOf(parameterName);
            if (index >= 0)
            {
                _items[index] = value;
            }
        }
    }
}
