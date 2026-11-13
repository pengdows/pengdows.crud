using pengdows.crud.attributes;
using System.Reflection;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// One private selector in BaseTableGateway chooses the batch upsert shape for both gateways (ON CONFLICT, then
/// ON DUPLICATE KEY when the incoming values are reliable, else one statement per entity), instead of each gateway
/// repeating the rule.
/// </summary>
public class BatchUpsertShapeSelectionTests
{
    private static object? Select(SupportedDatabase database, bool fragmentIsReliable)
    {
        var factory = new fakeDbFactory(database);
        var connectionString = $"Data Source=batch-shape;EmulatedProduct={database}";
        using var context = new DatabaseContext(connectionString, factory);
        var method = typeof(BaseTableGateway<ShapeEntity>).GetMethod(
            "SelectBatchUpsertShape",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return method!.Invoke(null, new object[] { context, fragmentIsReliable });
    }

    [Fact]
    public void OnConflictDatabase_SelectsOnConflict()
    {
        Assert.Equal("OnConflict", Select(SupportedDatabase.Sqlite, true)?.ToString());
    }

    [Fact]
    public void MergeOnlyDatabase_SelectsPerEntity()
    {
        Assert.Equal("PerEntity", Select(SupportedDatabase.SqlServer, true)?.ToString());
    }

    [Fact]
    public void UnreliableIncomingValues_DoNotSelectOnDuplicate()
    {
        Assert.NotEqual("OnDuplicate", Select(SupportedDatabase.MySql, false)?.ToString());
    }

    private sealed class ShapeEntity
    {
        [Id]
        public int Id { get; set; }
    }
}
