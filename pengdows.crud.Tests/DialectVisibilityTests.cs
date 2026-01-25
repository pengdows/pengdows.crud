using System.Reflection;
using pengdows.crud;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class DialectVisibilityTests
{
    [Theory]
    [InlineData("pengdows.crud.dialects.SqlDialect")]
    [InlineData("pengdows.crud.dialects.Sql92Dialect")]
    [InlineData("pengdows.crud.dialects.SqlServerDialect")]
    [InlineData("pengdows.crud.dialects.PostgreSqlDialect")]
    [InlineData("pengdows.crud.dialects.MySqlDialect")]
    [InlineData("pengdows.crud.dialects.MariaDbDialect")]
    [InlineData("pengdows.crud.dialects.SqliteDialect")]
    [InlineData("pengdows.crud.dialects.OracleDialect")]
    [InlineData("pengdows.crud.dialects.FirebirdDialect")]
    [InlineData("pengdows.crud.dialects.DuckDbDialect")]
    public void Dialects_AreNotPublic(string typeName)
    {
        var assembly = typeof(DatabaseContext).Assembly;
        var type = assembly.GetType(typeName, false);

        Assert.NotNull(type);
        Assert.True(type!.IsNotPublic, $"{typeName} should not be public.");
    }
}