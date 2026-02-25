#region

using System.Collections.Generic;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;
#endregion

namespace pengdows.crud.Tests;

public class SqliteDialectLimitTests
{
    [Theory]
    [InlineData("3.31.1", 999)]
    [InlineData("3.32.0", 32766)]
    public void MaxParameterLimit_SwitchesOnVersion(string sqliteVersion, int expectedLimit)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (fakeDbConnection)factory.CreateConnection();
        connection.ScalarResultsByCommand["SELECT sqlite_version()"] = sqliteVersion;
        var scalars = new Dictionary<string, object>
        {
            ["SELECT sqlite_version()"] = sqliteVersion
        };

        using var tracked = new FakeTrackedConnection(connection, new DataTable(), scalars);

        var dialect = new SqliteDialect(factory, NullLogger<SqliteDialect>.Instance);
        dialect.DetectDatabaseInfo(tracked);

        Assert.Equal(expectedLimit, dialect.MaxParameterLimit);
    }
}
