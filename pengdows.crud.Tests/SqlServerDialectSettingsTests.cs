using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlServerDialectSettingsTests
{
    private sealed class UserOptionsCommand : DbCommand
    {
        private readonly DbConnection _connection;
        private readonly fakeDbDataReader _reader;

        public UserOptionsCommand(DbConnection connection, fakeDbDataReader reader)
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

        protected override DbParameter CreateDbParameter() => new fakeDbParameter();
    }

    private sealed class UserOptionsConnection : fakeDbConnection
    {
        private readonly fakeDbDataReader _reader;

        public UserOptionsConnection(IEnumerable<Dictionary<string, object>> rows)
        {
            EmulatedProduct = SupportedDatabase.SqlServer;
            _reader = new fakeDbDataReader(rows);
        }

        protected override DbCommand CreateDbCommand() => new UserOptionsCommand(this, _reader);
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
    public void QuotePrefix_IsDoubleQuotes()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var dialect = new SqlServerDialect(factory, NullLogger<SqlServerDialect>.Instance);
        Assert.Equal("\"", dialect.QuotePrefix);
        Assert.Equal("\"", dialect.QuoteSuffix);
    }

    [Fact]
    public async Task ApplyConnectionSettings_OptimalSettings_CachesEmpty()
    {
        var rows = new[]
        {
            new Dictionary<string, object> { { "ANSI_NULLS", "SET" } },
            new Dictionary<string, object> { { "ANSI_PADDING", "SET" } },
            new Dictionary<string, object> { { "ANSI_WARNINGS", "SET" } },
            new Dictionary<string, object> { { "ARITHABORT", "SET" } },
            new Dictionary<string, object> { { "CONCAT_NULL_YIELDS_NULL", "SET" } },
            new Dictionary<string, object> { { "QUOTED_IDENTIFIER", "SET" } },
            new Dictionary<string, object> { { "NUMERIC_ROUNDABORT", "OFF" } }
        };
        await using var conn = BuildConnection(rows);
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var dialect = new SqlServerDialect(factory, NullLogger<SqlServerDialect>.Instance);
        await dialect.DetectDatabaseInfoAsync(conn);
        dialect.ApplyConnectionSettings(conn);
        Assert.Equal(string.Empty, dialect.GetConnectionSessionSettings());
    }

    [Fact]
    public async Task ApplyConnectionSettings_QuotedIdentifierOff_BuildsSettingsScript()
    {
        var rows = new[]
        {
            new Dictionary<string, object> { { "ANSI_NULLS", "SET" } },
            new Dictionary<string, object> { { "ANSI_PADDING", "SET" } },
            new Dictionary<string, object> { { "ANSI_WARNINGS", "SET" } },
            new Dictionary<string, object> { { "ARITHABORT", "SET" } },
            new Dictionary<string, object> { { "CONCAT_NULL_YIELDS_NULL", "SET" } },
            new Dictionary<string, object> { { "QUOTED_IDENTIFIER", "OFF" } },
            new Dictionary<string, object> { { "NUMERIC_ROUNDABORT", "OFF" } }
        };
        await using var conn = BuildConnection(rows);
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var dialect = new SqlServerDialect(factory, NullLogger<SqlServerDialect>.Instance);
        await dialect.DetectDatabaseInfoAsync(conn);
        dialect.ApplyConnectionSettings(conn);
        var settings = dialect.GetConnectionSessionSettings();
        Assert.Contains("SET QUOTED_IDENTIFIER ON", settings);
        Assert.StartsWith("SET NOCOUNT ON", settings);
    }
}
