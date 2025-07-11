using System;
using System.Collections.Generic;
using System.Reflection;
using System.Data;
using System.Data.Common;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class CheckForSqlServerSettingsTests
{
    private static MethodInfo GetMethod()
        => typeof(DatabaseContext).GetMethod("CheckForSqlServerSettings", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static FieldInfo GetSettingsField()
        => typeof(DatabaseContext).GetField("_connectionSessionSettings", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static DatabaseContext CreateContext()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=:memory:;EmulatedProduct={SupportedDatabase.SqlServer}",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.SingleConnection
        };
        var factory = new FakeDbFactory(SupportedDatabase.SqlServer);
        return new DatabaseContext(config, factory);
    }

    private static void Invoke(DatabaseContext ctx, ITrackedConnection conn)
    {
        GetMethod().Invoke(ctx, new object[] { conn });
    }

    private static string GetSessionSettings(DatabaseContext ctx)
        => (string)GetSettingsField().GetValue(ctx)!;

    private static void SetSessionSettings(DatabaseContext ctx, string value)
        => GetSettingsField().SetValue(ctx, value);

    private static void ForceSqlServer(DatabaseContext ctx)
    {
        var prop = typeof(DataSourceInformation)
            .GetProperty("DatabaseProductName", BindingFlags.Instance | BindingFlags.Public)!;
        prop.SetValue(ctx.DataSourceInfo, "Microsoft SQL Server");
    }

    private sealed class UserOptionsCommand : DbCommand
    {
        private readonly DbConnection _connection;
        private readonly FakeDbDataReader _reader;

        public UserOptionsCommand(DbConnection connection, FakeDbDataReader reader)
        {
            _connection = connection;
            _reader = reader;
        }

        public override string CommandText { get; set; }
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection DbConnection
        {
            get => _connection;
            set { }
        }

        protected override DbParameterCollection DbParameterCollection { get; } = new FakeParameterCollection();

        protected override DbTransaction DbTransaction { get; set; }

        public override void Cancel() { }
        public override int ExecuteNonQuery() => 0;
        public override object ExecuteScalar() => null;
        public override void Prepare() { }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => _reader;

        protected override DbParameter CreateDbParameter() => new FakeDbParameter();
    }

    private sealed class UserOptionsConnection : FakeDbConnection
    {
        private readonly FakeDbDataReader _reader;

        public UserOptionsConnection(IEnumerable<Dictionary<string, object>> rows)
        {
            EmulatedProduct = SupportedDatabase.SqlServer;
            _reader = new FakeDbDataReader(rows);
        }

        protected override DbCommand CreateDbCommand()
            => new UserOptionsCommand(this, _reader);
    }

    private static ITrackedConnection BuildConnection(IEnumerable<Dictionary<string, object>> rows)
    {
        var inner = new UserOptionsConnection(rows)
        {
            ConnectionString = $"Data Source=:memory:;EmulatedProduct={SupportedDatabase.SqlServer}"
        };
        inner.Open();
        return new TrackedConnection(inner);
    }

    [Fact]
    public void CheckForSqlServerSettings_NoDifferences_LeavesSettingsUnchanged()
    {
        using var ctx = CreateContext();
        SetSessionSettings(ctx, string.Empty);
        ForceSqlServer(ctx);

        var rows = new[]
        {
            new Dictionary<string, object> { { "a", "ANSI_NULLS" }, { "b", "SET" } },
            new Dictionary<string, object> { { "a", "ANSI_PADDING" }, { "b", "SET" } },
            new Dictionary<string, object> { { "a", "ANSI_WARNINGS" }, { "b", "SET" } },
            new Dictionary<string, object> { { "a", "ARITHABORT" }, { "b", "SET" } },
            new Dictionary<string, object> { { "a", "CONCAT_NULL_YIELDS_NULL" }, { "b", "SET" } },
            new Dictionary<string, object> { { "a", "QUOTED_IDENTIFIER" }, { "b", "SET" } },
            new Dictionary<string, object> { { "a", "NUMERIC_ROUNDABORT" }, { "b", "OFF" } }
        };

        var conn = BuildConnection(rows);
        Invoke(ctx, conn);

        Assert.Equal(string.Empty, GetSessionSettings(ctx));
    }

    [Fact]
    public void CheckForSqlServerSettings_Differences_BuildsSettingsScript()
    {
        using var ctx = CreateContext();
        SetSessionSettings(ctx, string.Empty);
        ForceSqlServer(ctx);

        var rows = new[]
        {
            new Dictionary<string, object> { { "a", "ANSI_NULLS" }, { "b", "OFF" } },
            new Dictionary<string, object> { { "a", "ANSI_PADDING" }, { "b", "OFF" } },
            new Dictionary<string, object> { { "a", "ANSI_WARNINGS" }, { "b", "OFF" } },
            new Dictionary<string, object> { { "a", "ARITHABORT" }, { "b", "OFF" } },
            new Dictionary<string, object> { { "a", "CONCAT_NULL_YIELDS_NULL" }, { "b", "OFF" } },
            new Dictionary<string, object> { { "a", "QUOTED_IDENTIFIER" }, { "b", "OFF" } },
            new Dictionary<string, object> { { "a", "NUMERIC_ROUNDABORT" }, { "b", "SET" } }
        };

        var conn = BuildConnection(rows);
        Invoke(ctx, conn);

        var nl = Environment.NewLine;
        var expected =
            $"SET NOCOUNT ON;{nl}" +
            $"SET ANSI_NULLS ON{nl}" +
            $"SET ANSI_PADDING ON{nl}" +
            $"SET ANSI_WARNINGS ON{nl}" +
            $"SET ARITHABORT ON{nl}" +
            $"SET CONCAT_NULL_YIELDS_NULL ON{nl}" +
            $"SET QUOTED_IDENTIFIER ON{nl}" +
            $"SET NUMERIC_ROUNDABORT OFF;{nl}" +
            $"SET NOCOUNT OFF;{nl}";

        Assert.Equal(expected, GetSessionSettings(ctx));
    }
}
