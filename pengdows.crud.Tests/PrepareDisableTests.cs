#region

using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
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
        public override DbConnection CreateConnection()
        {
            return new ThrowOnPrepareConnection();
        }
    }

    private sealed class ThrowOnPrepareConnection : DbConnection
    {
        private string _cs = string.Empty;
        private ConnectionState _state = ConnectionState.Closed;

        [AllowNull]
        public override string ConnectionString
        {
            get => _cs;
            set => _cs = value ?? string.Empty;
        }

        public override string Database => "test";
        public override string DataSource => "test";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close()
        {
            _state = ConnectionState.Closed;
        }

        public override void Open()
        {
            _state = ConnectionState.Open;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            return null!;
        }

        protected override DbCommand CreateDbCommand()
        {
            return new ThrowOnPrepareCommand(this);
        }
    }

    private sealed class ThrowOnPrepareCommand : DbCommand
    {
        private readonly DbConnection _conn;

        public ThrowOnPrepareCommand(DbConnection conn)
        {
            _conn = conn;
        }

        [AllowNull] public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; } = CommandType.Text;

        [AllowNull]
        protected override DbConnection DbConnection
        {
            get => _conn;
            set { }
        }

        protected override DbParameterCollection DbParameterCollection { get; } = new DummyParams();
        protected override DbTransaction? DbTransaction { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery()
        {
            return -1;
        }

        public override object ExecuteScalar()
        {
            return 1;
        }

        public override void Prepare()
        {
            throw new InvalidOperationException("Prepare not supported");
        }

        protected override DbParameter CreateDbParameter()
        {
            return new DummyParam();
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            throw new NotSupportedException();
        }

        private sealed class DummyParams : DbParameterCollection
        {
            public override int Count => 0;
            public override object SyncRoot => this;

            public override int Add(object value)
            {
                return 0;
            }

            public override void AddRange(Array values)
            {
            }

            public override void Clear()
            {
            }

            public override bool Contains(object value)
            {
                return false;
            }

            public override bool Contains(string value)
            {
                return false;
            }

            public override void CopyTo(Array array, int index)
            {
            }

            public override IEnumerator GetEnumerator()
            {
                return Array.Empty<object>().GetEnumerator();
            }

            protected override DbParameter GetParameter(int index)
            {
                return null!;
            }

            protected override DbParameter GetParameter(string parameterName)
            {
                return null!;
            }

            public override int IndexOf(object value)
            {
                return -1;
            }

            public override int IndexOf(string parameterName)
            {
                return -1;
            }

            public override void Insert(int index, object value)
            {
            }

            public override void Remove(object value)
            {
            }

            public override void RemoveAt(int index)
            {
            }

            public override void RemoveAt(string parameterName)
            {
            }

            protected override void SetParameter(int index, DbParameter value)
            {
            }

            protected override void SetParameter(string parameterName, DbParameter value)
            {
            }
        }

        private sealed class DummyParam : DbParameter
        {
            private string _parameterName = string.Empty;
            private string _sourceColumn = string.Empty;

            public override DbType DbType { get; set; }
            public override ParameterDirection Direction { get; set; }
            public override bool IsNullable { get; set; }

            [AllowNull]
            public override string ParameterName
            {
                get => _parameterName;
                set => _parameterName = value ?? string.Empty;
            }

            [AllowNull]
            public override string SourceColumn
            {
                get => _sourceColumn;
                set => _sourceColumn = value ?? string.Empty;
            }

            [AllowNull] public override object Value { get; set; } = DBNull.Value;
            public override bool SourceColumnNullMapping { get; set; }
            public override int Size { get; set; }

            public override void ResetDbType()
            {
            }
        }
    }

    [Fact]
    public async Task PrepareFailure_DisablesPrepare_And_LogsOnce()
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
            await sc.ExecuteNonQueryAsync();
        }

        // Second attempt should not try Prepare again (same persistent connection), so no additional debug logs
        using (var sc = ctx.CreateSqlContainer("SELECT 1"))
        {
            await sc.ExecuteNonQueryAsync();
        }

        var disabledLogs = provider.Entries
            .Where(e => e.Level == LogLevel.Debug &&
                        e.Message.Contains("Disabled prepare for connection due to provider exception"))
            .ToList();
        Assert.Single(disabledLogs);
    }

    [Fact]
    public async Task DisablePrepare_True_DoesNotAttemptPrepare()
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
        await sc.ExecuteNonQueryAsync();
        // No debug log about disabling because prepare was never attempted
        Assert.DoesNotContain(provider.Entries, e => e.Message.Contains("Disabled prepare"));
    }

    [Fact]
    public async Task StandardMode_TwoCommands_DisablePrepare_OncePerConnection()
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
            await sc.ExecuteNonQueryAsync();
        }

        using (var sc = ctx.CreateSqlContainer("SELECT 1"))
        {
            await sc.ExecuteNonQueryAsync();
        }

        var disabledLogs = provider.Entries
            .Where(e => e.Level == LogLevel.Debug &&
                        e.Message.Contains("Disabled prepare for connection due to provider exception"))
            .ToList();
        Assert.True(disabledLogs.Count >= 2);
    }
}