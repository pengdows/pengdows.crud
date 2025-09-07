using System.Collections.Generic;
using System.Data.Common;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class PostgreSqlSessionSettingsTests
{
    private sealed class RecordingConnection : fakeDbConnection
    {
        public List<string> ExecutedCommands { get; } = new();
        protected override DbCommand CreateDbCommand() => new RecordingCommand(this, ExecutedCommands);
    }

    private sealed class RecordingCommand : fakeDbCommand
    {
        private readonly List<string> _record;
        public RecordingCommand(fakeDbConnection connection, List<string> record) : base(connection) => _record = record;
        public override int ExecuteNonQuery()
        {
            _record.Add(CommandText);
            return base.ExecuteNonQuery();
        }
    }

    private sealed class RecordingFactory : DbProviderFactory
    {
        public List<RecordingConnection> Connections { get; } = new();
        public override DbConnection CreateConnection()
        {
            var conn = new RecordingConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
            Connections.Add(conn);
            return conn;
        }

        public override DbCommand CreateCommand() => new fakeDbCommand();
        public override DbParameter CreateParameter() => new fakeDbParameter();
    }

    private static DatabaseContext CreateContext(RecordingFactory factory)
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=:memory:;EmulatedProduct={SupportedDatabase.PostgreSql}",
            ProviderName = SupportedDatabase.PostgreSql.ToString(),
            DbMode = DbMode.Standard
        };
        return new DatabaseContext(config, factory);
    }

    [Fact]
    public void NoSessionSettingsAppliedByDefault()
    {
        var factory = new RecordingFactory();
        using var ctx = CreateContext(factory);
        Assert.Equal(string.Empty, ctx.SessionSettingsPreamble);
        var startIndex = factory.Connections.Count;

        using var conn = ctx.GetConnection(ExecutionType.Read);
        conn.Open();
        ctx.CloseAndDisposeConnection(conn);

        var operational = factory.Connections[startIndex];
        Assert.Empty(operational.ExecutedCommands);
    }

    [Fact]
    public void NoSessionSettingsAppliedOnSubsequentConnections()
    {
        var factory = new RecordingFactory();
        using var ctx = CreateContext(factory);
        var startIndex = factory.Connections.Count;

        using (var conn = ctx.GetConnection(ExecutionType.Read))
        {
            conn.Open();
            ctx.CloseAndDisposeConnection(conn);
        }

        using (var conn = ctx.GetConnection(ExecutionType.Read))
        {
            conn.Open();
            ctx.CloseAndDisposeConnection(conn);
        }

        var first = factory.Connections[startIndex];
        var second = factory.Connections[startIndex + 1];
        Assert.Empty(first.ExecutedCommands);
        Assert.Empty(second.ExecutedCommands);
    }

    [Fact]
    public void NoSessionSettingsAppliedInReadOnlyContext()
    {
        var factory = new RecordingFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=:memory:;EmulatedProduct={SupportedDatabase.PostgreSql}",
            ProviderName = SupportedDatabase.PostgreSql.ToString(),
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadOnly
        };
        using var ctx = new DatabaseContext(config, factory);
        Assert.Equal(string.Empty, ctx.SessionSettingsPreamble);
        var startIndex = factory.Connections.Count;

        using var conn = ctx.GetConnection(ExecutionType.Read);
        conn.Open();
        ctx.CloseAndDisposeConnection(conn);

        var operational = factory.Connections[startIndex];
        Assert.Empty(operational.ExecutedCommands);
    }
}
