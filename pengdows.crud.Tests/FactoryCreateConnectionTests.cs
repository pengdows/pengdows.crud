using System;
using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud;
using pengdows.crud.enums;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class FactoryCreateConnectionTests
{
    [Fact]
    public void OpenThenClose_UpdatesConnectionCount()
    {
        var factory = new BreakableDbFactory();
        var context = new DatabaseContext("Data Source=test;", factory);
        var tracked = (TrackedConnection)context.GetConnection(ExecutionType.Read);
        tracked.Open();
        Assert.Equal(1, context.NumberOfOpenConnections);
        tracked.Close();
        Assert.Equal(0, context.NumberOfOpenConnections);
        tracked.Dispose();
    }

    [Fact]
    public void BrokenThenClosed_DoesNotDoubleDecrement()
    {
        var factory = new BreakableDbFactory();
        var context = new DatabaseContext("Data Source=test;", factory);
        var tracked = (TrackedConnection)context.GetConnection(ExecutionType.Read);
        tracked.Open();
        Assert.Equal(1, context.NumberOfOpenConnections);

        var inner = GetInnerConnection(tracked);
        inner.Break();
        Assert.Equal(0, context.NumberOfOpenConnections);

        tracked.Close();
        Assert.Equal(0, context.NumberOfOpenConnections);
        tracked.Dispose();
    }

    private static BreakableDbConnection GetInnerConnection(TrackedConnection tracked)
    {
        var field = typeof(TrackedConnection).GetField("_connection", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (BreakableDbConnection)field.GetValue(tracked)!;
    }

    private sealed class BreakableDbFactory : DbProviderFactory
    {
        public override DbConnection CreateConnection()
        {
            return new BreakableDbConnection();
        }

        public override DbConnectionStringBuilder CreateConnectionStringBuilder()
        {
            return new DbConnectionStringBuilder();
        }
    }

    private sealed class BreakableDbConnection : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "Breakable";
        public override string DataSource => "Breakable";
        public override string ServerVersion => "1.0";
        public override int ConnectionTimeout => 0;
        public override ConnectionState State => _state;

        public void Break()
        {
            var original = _state;
            _state = ConnectionState.Broken;
            OnStateChange(new StateChangeEventArgs(original, _state));
        }

        public override void Open()
        {
            var original = _state;
            _state = ConnectionState.Open;
            OnStateChange(new StateChangeEventArgs(original, _state));
        }

        public override void Close()
        {
            var original = _state;
            _state = ConnectionState.Closed;
            OnStateChange(new StateChangeEventArgs(original, _state));
        }

        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            Open();
            return Task.CompletedTask;
        }

        public override Task CloseAsync()
        {
            Close();
            return Task.CompletedTask;
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
            throw new NotSupportedException();
        }

        public override DataTable GetSchema()
        {
            var table = new DataTable();
            table.Columns.Add("DataSourceProductName");
            var row = table.NewRow();
            row["DataSourceProductName"] = "Breakable";
            table.Rows.Add(row);
            return table;
        }

        public override DataTable GetSchema(string collectionName)
        {
            return GetSchema();
        }
    }
}
