using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class DialectDetectionAsyncTests
{
    private static DatabaseContext CreateContext(SupportedDatabase db)
    {
        var factory = new fakeDbFactory(db);
        return new DatabaseContext($"Data Source=test;EmulatedProduct={db}", factory);
    }

    [Theory]
    [InlineData(SupportedDatabase.DuckDB)]
    [InlineData(SupportedDatabase.Firebird)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.PostgreSql)]
    public async Task DetectDatabaseInfo_ExercisesDialectAsyncPaths(SupportedDatabase db)
    {
        await using var ctx = CreateContext(db);
        var factory = new fakeDbFactory(db);
        var dialect = SqlDialectFactory.CreateDialectForType(db, factory, NullLogger<SqlDialect>.Instance);

        var conn = ctx.GetConnection(ExecutionType.Read);
        await conn.OpenAsync();

        var info = await dialect.DetectDatabaseInfoAsync(conn);

        Assert.NotNull(info);
        Assert.NotNull(info.ProductName);
        Assert.NotNull(info.ProductVersion);
        Assert.Equal(db, info.DatabaseType);
    }
}

