#region

using System.Collections.Generic;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
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

    [Theory]
    [InlineData(9876.54321)]
    [InlineData(-99999999.99999999)]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void CreateDbParameter_Decimal_StoresAsDouble(double rawValue)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new SqliteDialect(factory, NullLogger<SqliteDialect>.Instance);
        var decimalValue = (decimal)rawValue;

        var p = dialect.CreateDbParameter("col_decimal", DbType.Decimal, decimalValue);

        // SQLite stores DECIMAL as REAL (double). The parameter must use DbType.Double
        // so Microsoft.Data.Sqlite can bind the value correctly — DbType.Decimal causes
        // the driver to store 0 instead of the actual value.
        Assert.Equal(DbType.Double, p.DbType);
        Assert.Equal((double)decimalValue, (double)p.Value!);
    }
}
