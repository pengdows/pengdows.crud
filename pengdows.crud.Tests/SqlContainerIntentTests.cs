using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
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

    [Fact]
    public async Task ExecuteScalarOrNullAsync_WriteIntent_ThrowsOnReadOnlyContext()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "test",
            ReadWriteMode = ReadWriteMode.ReadOnly
        };

        using var context = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.Sqlite));
        await using var container = context.CreateSqlContainer("SELECT 1");

        var ex = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await container.ExecuteScalarOrNullAsync<int?>(ExecutionType.Write));
        Assert.Contains("read-only mode", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TryExecuteScalarAsync_WriteIntent_ThrowsOnReadOnlyContext()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "test",
            ReadWriteMode = ReadWriteMode.ReadOnly
        };

        using var context = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.Sqlite));
        await using var container = context.CreateSqlContainer("SELECT 1");

        var ex = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await container.TryExecuteScalarAsync<int>(ExecutionType.Write));
        Assert.Contains("read-only mode", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteReaderAsync_WriteIntent_ThrowsOnReadOnlyContext()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "test",
            ReadWriteMode = ReadWriteMode.ReadOnly
        };

        using var context = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.Sqlite));
        await using var container = context.CreateSqlContainer("SELECT 1");

        // ExecuteReaderAsync goes through AssertIsWriteConnection rather than the
        // ReadWriteMode guard, so the exception type is InvalidOperationException.
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await container.ExecuteReaderAsync(ExecutionType.Write));
    }

    [Fact]
    public async Task ExecuteReaderAsync_DefaultIntent_UsesRead_AllowedOnReadOnlyContext()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "test",
            ReadWriteMode = ReadWriteMode.ReadOnly
        };

        using var context = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.Sqlite));
        await using var container = context.CreateSqlContainer("SELECT 1");

        // Default overload routes to ExecutionType.Read — must not throw on a read-only context.
        await using var reader = await container.ExecuteReaderAsync();
        Assert.NotNull(reader);
    }

    [Fact]
    public async Task ExecuteScalarRequiredAsync_DefaultIntent_UsesRead_AllowedOnReadOnlyContext()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "test",
            ReadWriteMode = ReadWriteMode.ReadOnly
        };

        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.SetScalarResult(42);
        using var context = new DatabaseContext(config, factory);
        await using var container = context.CreateSqlContainer("SELECT 42");

        // Default overload routes to ExecutionType.Read — must not throw on a read-only context.
        var result = await container.ExecuteScalarRequiredAsync<int>();
        Assert.Equal(42, result);
    }
}