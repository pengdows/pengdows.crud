#region
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;
#endregion

namespace pengdows.crud.Tests;

public class SqliteDialectAdvancedTests
{
    private readonly ILogger<SqliteDialect> _logger;
    private readonly SqliteDialect _dialect;
    private readonly fakeDbFactory _factory;

    public SqliteDialectAdvancedTests()
    {
        _factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        _logger = new LoggerFactory().CreateLogger<SqliteDialect>();
        _dialect = new SqliteDialect(_factory, _logger);
    }

    [Fact]
    public async Task GetProductNameAsync_Should_Return_Sqlite_From_Version_Query()
    {
        var connection = (fakeDbConnection)_factory.CreateConnection();
        var row = new Dictionary<string, object> { { "version", "3.0" } };
        connection.EnqueueReaderResult(new[] { row });
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var productName = await _dialect.GetProductNameAsync(trackedConnection);

        Assert.Equal("SQLite", productName);
    }

    [Fact]
    public async Task GetProductNameAsync_Should_Return_Null_When_Version_Query_Returns_No_Rows()
    {
        var connection = (fakeDbConnection)_factory.CreateConnection();
        connection.EnqueueReaderResult(Array.Empty<Dictionary<string, object>>());
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var productName = await _dialect.GetProductNameAsync(trackedConnection);

        Assert.Null(productName);
    }
}
