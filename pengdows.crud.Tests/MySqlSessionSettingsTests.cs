using System.Collections.Generic;
using System.Linq;
using System.Data.Common;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class MySqlSessionSettingsTests
{
    private sealed class RecordingConnection : fakeDbConnection
    {
        public List<string> ExecutedCommands { get; } = new();

        protected override DbCommand CreateDbCommand()
        {
            return new RecordingCommand(this, ExecutedCommands);
        }
    }

    private sealed class RecordingCommand : fakeDbCommand
    {
        private readonly List<string> _record;

        public RecordingCommand(fakeDbConnection connection, List<string> record) : base(connection)
        {
            _record = record;
        }

        public override int ExecuteNonQuery()
        {
            _record.Add(CommandText);
            return base.ExecuteNonQuery();
        }
    }

    private sealed class RecordingFactory : DbProviderFactory
    {
        public RecordingConnection Connection { get; } = new();

        public override DbConnection CreateConnection()
        {
            Connection.EmulatedProduct = SupportedDatabase.MySql;
            return Connection;
        }

        public override DbCommand CreateCommand()
        {
            return new fakeDbCommand();
        }

        public override DbParameter CreateParameter()
        {
            return new fakeDbParameter();
        }
    }

    [Fact]
    public void SessionSettingsAppliedOnceInSingleConnectionMode()
    {
        var factory = new RecordingFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=:memory:;EmulatedProduct={SupportedDatabase.MySql}",
            ProviderName = SupportedDatabase.MySql.ToString(),
            DbMode = DbMode.SingleConnection
        };

        using var ctx = new DatabaseContext(config, factory);
        var count = factory.Connection.ExecutedCommands.Count(c => c.StartsWith("SET SESSION sql_mode"));
        Assert.Equal(1, count);
    }

    [Fact]
    public void SessionSettingsNotAppliedDuringConstructionInStandardMode()
    {
        var factory = new RecordingFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=:memory:;EmulatedProduct={SupportedDatabase.MySql}",
            ProviderName = SupportedDatabase.MySql.ToString(),
            DbMode = DbMode.Standard
        };

        using var ctx = new DatabaseContext(config, factory);
        var count = factory.Connection.ExecutedCommands.Count(c => c.StartsWith("SET SESSION sql_mode"));
        Assert.Equal(0, count);
    }
}
