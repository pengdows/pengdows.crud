using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlContainerIntentTests
{
    [Fact]
    public void ISqlContainer_ExecutionMethods_ExposeExecutionIntent()
    {
        var executeMethodNames = new[]
        {
            "ExecuteNonQueryAsync",
            "ExecuteScalarRequiredAsync",
            "ExecuteScalarOrNullAsync",
            "TryExecuteScalarAsync",
            "ExecuteReaderAsync"
        };

        var methods = typeof(ISqlContainer)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .ToArray();

        foreach (var name in executeMethodNames)
        {
            var overloads = methods.Where(method => method.Name == name).ToArray();
            Assert.NotEmpty(overloads);

            Assert.True(
                overloads.Any(method =>
                    method.GetParameters().Any(param => param.ParameterType == typeof(ExecutionType))),
                $"Expected {name} to expose ExecutionType intent overloads.");
        }
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_DefaultIntent_UsesWrite()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "test",
            ReadWriteMode = ReadWriteMode.ReadOnly
        };

        using var context = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.Sqlite));
        await using var container = context.CreateSqlContainer("SELECT 1");

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await container.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task ExecuteScalarOrNullAsync_DefaultIntent_UsesRead()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "test",
            ReadWriteMode = ReadWriteMode.ReadOnly
        };

        using var context = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.Sqlite));
        await using var container = context.CreateSqlContainer("SELECT 1");

        var result = await container.ExecuteScalarOrNullAsync<int?>();

        Assert.Null(result);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_WithReadIntent_AllowsReadOnlyContext()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "test",
            ReadWriteMode = ReadWriteMode.ReadOnly
        };

        using var context = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.Sqlite));
        await using var container = context.CreateSqlContainer("SELECT 1");

        var result = await container.ExecuteNonQueryAsync(ExecutionType.Read);

        Assert.True(result >= 0);
    }
}