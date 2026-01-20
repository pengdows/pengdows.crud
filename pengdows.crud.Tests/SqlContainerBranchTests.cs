using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlContainerBranchTests
{
    [Fact]
    public void AddParameterWithValue_OutputUnsupported_Throws()
    {
        using var ctx = CreateContext(SupportedDatabase.Sqlite);
        var container = ctx.CreateSqlContainer("SELECT 1");

        Assert.Throws<ArgumentException>(() =>
            container.AddParameterWithValue("p0", DbType.Int32, 1, ParameterDirection.Output));
    }

    [Fact]
    public void AddParameter_OutputExceedsLimit_Throws()
    {
        using var ctx = CreateContext(SupportedDatabase.PostgreSql);
        ((DataSourceInformation)ctx.DataSourceInfo).MaxOutputParameters = 1;
        var container = ctx.CreateSqlContainer("SELECT 1");

        container.AddParameterWithValue("p0", DbType.Int32, 1, ParameterDirection.Output);

        Assert.Throws<InvalidOperationException>(() =>
            container.AddParameterWithValue("p1", DbType.Int32, 2, ParameterDirection.Output));
    }

    [Fact]
    public async Task ExecuteScalarAsync_NoRows_NonNullable_Throws()
    {
        var connection = new fakeDbConnection();
        connection.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { { "version", "3.42.0" } }
        });
        connection.EnqueueReaderResult(Array.Empty<Dictionary<string, object?>>());
        using var ctx = CreateContext(SupportedDatabase.Sqlite, connection);
        var container = ctx.CreateSqlContainer("SELECT 1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => container.ExecuteScalarAsync<int>());
    }

    [Fact]
    public async Task ExecuteScalarAsync_NoRows_Nullable_ReturnsDefault()
    {
        var connection = new fakeDbConnection();
        connection.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { { "version", "3.42.0" } }
        });
        connection.EnqueueReaderResult(Array.Empty<Dictionary<string, object?>>());
        using var ctx = CreateContext(SupportedDatabase.Sqlite, connection);
        var container = ctx.CreateSqlContainer("SELECT 1");

        var result = await container.ExecuteScalarAsync<int?>();

        Assert.Null(result);
    }

    [Fact]
    public async Task ExecuteScalarAsync_Object_ReturnsRawValue()
    {
        var connection = new fakeDbConnection();
        connection.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { { "version", "3.42.0" } }
        });
        connection.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { { "value", "raw" } }
        });
        using var ctx = CreateContext(SupportedDatabase.Sqlite, connection);
        var container = ctx.CreateSqlContainer("SELECT 1");

        var result = await container.ExecuteScalarAsync<object>();

        Assert.Equal("raw", result);
    }

    [Fact]
    public async Task ExecuteScalarWriteAsync_NullResult_NonNullable_Throws()
    {
        var connection = new fakeDbConnection();
        connection.SetScalarResultForCommand("SELECT scalar_test", DBNull.Value);
        using var ctx = CreateContext(SupportedDatabase.Sqlite, connection);
        var container = ctx.CreateSqlContainer("SELECT scalar_test");

        await Assert.ThrowsAsync<InvalidOperationException>(() => container.ExecuteScalarWriteAsync<int>());
    }

    [Fact]
    public async Task ExecuteScalarWriteAsync_NullResult_Nullable_ReturnsDefault()
    {
        var connection = new fakeDbConnection();
        connection.SetScalarResultForCommand("SELECT scalar_test", DBNull.Value);
        using var ctx = CreateContext(SupportedDatabase.Sqlite, connection);
        var container = ctx.CreateSqlContainer("SELECT scalar_test");

        var result = await container.ExecuteScalarWriteAsync<int?>();

        Assert.Null(result);
    }

    [Fact]
    public async Task ExecuteScalarWriteAsync_Object_ReturnsRawValue()
    {
        var connection = new fakeDbConnection();
        connection.SetScalarResultForCommand("SELECT scalar_test", "raw");
        using var ctx = CreateContext(SupportedDatabase.Sqlite, connection);
        var container = ctx.CreateSqlContainer("SELECT scalar_test");

        var result = await container.ExecuteScalarWriteAsync<object>();

        Assert.Equal("raw", result);
    }

    [Fact]
    public async Task ExecuteScalarWriteAsync_CoercesValue()
    {
        var connection = new fakeDbConnection();
        connection.SetScalarResultForCommand("SELECT scalar_test", "42");
        using var ctx = CreateContext(SupportedDatabase.PostgreSql, connection);
        var container = ctx.CreateSqlContainer("SELECT scalar_test");

        var result = await container.ExecuteScalarWriteAsync<int>();

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_ReadOnlyContext_Throws()
    {
        using var ctx = CreateContext(SupportedDatabase.PostgreSql, null, ReadWriteMode.ReadOnly);
        var container = ctx.CreateSqlContainer("SELECT 1");

        await Assert.ThrowsAsync<NotSupportedException>(() => container.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_TableDirect_Throws()
    {
        using var ctx = CreateContext(SupportedDatabase.PostgreSql);
        var container = ctx.CreateSqlContainer("SELECT 1");

        await Assert.ThrowsAsync<NotSupportedException>(() => container.ExecuteNonQueryAsync(CommandType.TableDirect));
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_EmptyQuery_Throws()
    {
        using var ctx = CreateContext(SupportedDatabase.PostgreSql);
        var container = ctx.CreateSqlContainer();

        await Assert.ThrowsAsync<InvalidOperationException>(() => container.ExecuteNonQueryAsync());
    }

    private static DatabaseContext CreateContext(
        SupportedDatabase db,
        fakeDbConnection? connection = null,
        ReadWriteMode mode = ReadWriteMode.ReadWrite)
    {
        var factory = new fakeDbFactory(db);
        if (connection != null)
        {
            connection.EmulatedProduct = db;
            connection.ConnectionString = $"Data Source=test;EmulatedProduct={db}";
            factory.Connections.Add(connection);
        }

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={db}",
            ReadWriteMode = mode,
            DbMode = DbMode.SingleConnection
        };

        return new DatabaseContext(cfg, factory, NullLoggerFactory.Instance);
    }
}
