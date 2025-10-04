#region

using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Linq;
using Microsoft.Extensions.Logging;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.Tests.Logging;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class PrepareDisableTests
{
    private sealed class ThrowOnPrepareFactory : DbProviderFactory
    {
        public override DbConnection CreateConnection() => new ThrowOnPrepareConnection();
    }

    private sealed class ThrowOnPrepareConnection : DbConnection
    {
        private string _cs = string.Empty;
        private ConnectionState _state = ConnectionState.Closed;
        public override string ConnectionString { get => _cs; set => _cs = value; }
        public override string Database => "test";
        public override string DataSource => "test";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => _state;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() => _state = ConnectionState.Closed;
        public override void Open() => _state = ConnectionState.Open;
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => null!;
        protected override DbCommand CreateDbCommand() => new ThrowOnPrepareCommand(this);
    }

    private sealed class ThrowOnPrepareCommand : DbCommand
    {
        private readonly DbConnection _conn;
        public ThrowOnPrepareCommand(DbConnection conn) { _conn = conn; }
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; } = CommandType.Text;
        protected override DbConnection DbConnection { get => _conn; set { } }
        protected override DbParameterCollection DbParameterCollection { get; } = new DummyParams();
        protected override DbTransaction DbTransaction { get; set; } = null!;
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        public override void Cancel() { }
        public override int ExecuteNonQuery() => -1;
        public override object ExecuteScalar() => 1;
        public override void Prepare() => throw new InvalidOperationException("Prepare not supported");
        protected override DbParameter CreateDbParameter() => new DummyParam();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        private sealed class DummyParams : DbParameterCollection
        {
            public override int Count => 0; public override object SyncRoot => this;
            public override int Add(object value) => 0; public override void AddRange(Array values) { }
            public override void Clear() { } public override bool Contains(object value) => false;
            public override bool Contains(string value) => false; public override void CopyTo(Array array, int index) { }
            public override IEnumerator GetEnumerator() => Array.Empty<object>().GetEnumerator();
            protected override DbParameter GetParameter(int index) => null!;
            protected override DbParameter GetParameter(string parameterName) => null!;
            public override int IndexOf(object value) => -1; public override int IndexOf(string parameterName) => -1;
            public override void Insert(int index, object value) { } public override void Remove(object value) { }
            public override void RemoveAt(int index) { } public override void RemoveAt(string parameterName) { }
            protected override void SetParameter(int index, DbParameter value) { }
            protected override void SetParameter(string parameterName, DbParameter value) { }
        }
        private sealed class DummyParam : DbParameter
        {
            public override DbType DbType { get; set; }
            public override ParameterDirection Direction { get; set; }
            public override bool IsNullable { get; set; }
            public override string ParameterName { get; set; } = string.Empty;
            public override string SourceColumn { get; set; } = string.Empty;
            public override object? Value { get; set; }
            public override bool SourceColumnNullMapping { get; set; }
            public override int Size { get; set; }
            public override void ResetDbType() { }
        }
    }

    [Fact]
    public void PrepareFailure_DisablesPrepare_And_LogsOnce()
    {
        var provider = new ListLoggerProvider();
        using var lf = new LoggerFactory(new[] { provider });
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=prepare;",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            ForceManualPrepare = true
        };
        using var ctx = new DatabaseContext(cfg, new ThrowOnPrepareFactory(), lf);

        // First attempt will try Prepare() and disable it due to exception
        using (var sc = ctx.CreateSqlContainer("SELECT 1"))
        {
            sc.ExecuteNonQueryAsync().GetAwaiter().GetResult();
        }

        // Second attempt should not try Prepare again (same persistent connection), so no additional debug logs
        using (var sc = ctx.CreateSqlContainer("SELECT 1"))
        {
            sc.ExecuteNonQueryAsync().GetAwaiter().GetResult();
        }

        var disabledLogs = provider.Entries
            .Where(e => e.Level == LogLevel.Debug && e.Message.Contains("Disabled prepare for connection due to provider exception"))
            .ToList();
        Assert.Single(disabledLogs);
    }

    [Fact]
    public void DisablePrepare_True_DoesNotAttemptPrepare()
    {
        var provider = new ListLoggerProvider();
        using var lf = new LoggerFactory(new[] { provider });
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=prepare;",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DisablePrepare = true
        };
        using var ctx = new DatabaseContext(cfg, new ThrowOnPrepareFactory(), lf);
        using var sc = ctx.CreateSqlContainer("SELECT 1");
        sc.ExecuteNonQueryAsync().GetAwaiter().GetResult();
        // No debug log about disabling because prepare was never attempted
        Assert.DoesNotContain(provider.Entries, e => e.Message.Contains("Disabled prepare"));
    }

    [Fact]
    public void StandardMode_TwoCommands_DisablePrepare_OncePerConnection()
    {
        var provider = new ListLoggerProvider();
        using var lf = new LoggerFactory(new[] { provider });
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=prepare;",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            ForceManualPrepare = true
        };
        using var ctx = new DatabaseContext(cfg, new ThrowOnPrepareFactory(), lf);

        // Standard mode uses ephemeral connections; each attempt will disable prepare for its own connection
        using (var sc = ctx.CreateSqlContainer("SELECT 1"))
        {
            sc.ExecuteNonQueryAsync().GetAwaiter().GetResult();
        }
        using (var sc = ctx.CreateSqlContainer("SELECT 1"))
        {
            sc.ExecuteNonQueryAsync().GetAwaiter().GetResult();
        }

        var disabledLogs = provider.Entries
            .Where(e => e.Level == LogLevel.Debug && e.Message.Contains("Disabled prepare for connection due to provider exception"))
            .ToList();
        Assert.True(disabledLogs.Count >= 2);
    }
}
