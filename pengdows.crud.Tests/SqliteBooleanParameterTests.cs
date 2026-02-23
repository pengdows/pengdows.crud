using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class SqliteBooleanParameterTests
{
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void CreateDbParameter_BooleanWithNumericDbType_CoercesToInt(bool value, int expected)
    {
        var dialect = new SqliteDialect(new fakeDbFactory(SupportedDatabase.Sqlite),
            NullLogger<SqliteDialect>.Instance);

        var param = dialect.CreateDbParameter("active", DbType.Int32, value);

        Assert.Equal(DbType.Int32, param.DbType);
        Assert.Equal(expected, param.Value);
    }
}