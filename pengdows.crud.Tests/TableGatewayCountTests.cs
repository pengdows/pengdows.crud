// =============================================================================
// FILE: TableGatewayCountTests.cs
// PURPOSE: TDD tests for CountAllAsync, CountWhereAsync, CountWhereNullAsync,
//          CountWhereEqualsAsync on BaseTableGateway — written before the
//          implementation is added to BaseTableGateway.
// =============================================================================

using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

[Collection("SqliteSerial")]
public class TableGatewayCountTests
{
    // -------------------------------------------------------------------------
    // Test entity: queue item with a nullable FetchedAt column
    // -------------------------------------------------------------------------

    [Table("count_item")]
    public class CountItem
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey]
        [Column("queue", DbType.String)]
        public string Queue { get; set; } = string.Empty;

        [Column("state", DbType.String)]
        public string? State { get; set; }

        [Column("fetched_at", DbType.DateTime)]
        public DateTime? FetchedAt { get; set; }
    }

    private static IDatabaseContext MakeContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite) { EnableDataPersistence = true };
        return new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory);
    }

    // -------------------------------------------------------------------------
    // CountAllAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CountAllAsync_EmptyTable_ReturnsZero()
    {
        await using var ctx = MakeContext();
        var gw = new TableGateway<CountItem, int>(ctx);

        var count = await gw.CountAllAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task CountAllAsync_AfterInserts_ReturnsCorrectTotal()
    {
        await using var ctx = MakeContext();
        var gw = new TableGateway<CountItem, int>(ctx);

        await gw.CreateAsync(new CountItem { Queue = "default", State = "Enqueued" });
        await gw.CreateAsync(new CountItem { Queue = "critical", State = "Enqueued" });
        await gw.CreateAsync(new CountItem { Queue = "default", State = "Processing" });

        var count = await gw.CountAllAsync();

        Assert.Equal(3, count);
    }

    // -------------------------------------------------------------------------
    // CountWhereAsync — equality
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CountWhereAsync_Equality_ReturnsMatchingCount()
    {
        await using var ctx = MakeContext();
        var gw = new TableGateway<CountItem, int>(ctx);

        await gw.CreateAsync(new CountItem { Queue = "default", State = "Processing" });
        await gw.CreateAsync(new CountItem { Queue = "default", State = "Enqueued" });
        await gw.CreateAsync(new CountItem { Queue = "default", State = "Processing" });

        var count = await gw.CountWhereAsync("state", "Processing");

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task CountWhereAsync_NoMatch_ReturnsZero()
    {
        await using var ctx = MakeContext();
        var gw = new TableGateway<CountItem, int>(ctx);

        await gw.CreateAsync(new CountItem { Queue = "default", State = "Enqueued" });

        var count = await gw.CountWhereAsync("state", "Failed");

        Assert.Equal(0, count);
    }

    // -------------------------------------------------------------------------
    // CountWhereAsync — LIKE
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CountWhereAsync_Like_ReturnsMatchingCount()
    {
        await using var ctx = MakeContext();
        var gw = new TableGateway<CountItem, int>(ctx);

        await gw.CreateAsync(new CountItem { Queue = "recurring-jobs:cleanup", State = "Scheduled" });
        await gw.CreateAsync(new CountItem { Queue = "recurring-jobs:report", State = "Scheduled" });
        await gw.CreateAsync(new CountItem { Queue = "default", State = "Enqueued" });

        var count = await gw.CountWhereAsync("queue", "recurring-jobs:%", isLike: true);

        Assert.Equal(2, count);
    }

    // -------------------------------------------------------------------------
    // CountWhereNullAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CountWhereNullAsync_CountsRowsWhereColumnIsNull()
    {
        await using var ctx = MakeContext();
        var gw = new TableGateway<CountItem, int>(ctx);

        await gw.CreateAsync(new CountItem { Queue = "default", State = "Enqueued", FetchedAt = null });
        await gw.CreateAsync(new CountItem { Queue = "default", State = "Processing", FetchedAt = DateTime.UtcNow });
        await gw.CreateAsync(new CountItem { Queue = "default", State = "Enqueued", FetchedAt = null });

        var count = await gw.CountWhereNullAsync("fetched_at");

        Assert.Equal(2, count);
    }

    // -------------------------------------------------------------------------
    // CountWhereEqualsAsync — with andWhereNull
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CountWhereEqualsAsync_WithAndWhereNull_CountsCorrectSubset()
    {
        await using var ctx = MakeContext();
        var gw = new TableGateway<CountItem, int>(ctx);

        // default + null FetchedAt → should count
        await gw.CreateAsync(new CountItem { Queue = "default", FetchedAt = null });
        // default + non-null FetchedAt → should NOT count
        await gw.CreateAsync(new CountItem { Queue = "default", FetchedAt = DateTime.UtcNow });
        // critical + null FetchedAt → should NOT count (wrong queue)
        await gw.CreateAsync(new CountItem { Queue = "critical", FetchedAt = null });

        var count = await gw.CountWhereEqualsAsync("queue", "default", andWhereNull: "fetched_at");

        Assert.Equal(1, count);
    }

    // -------------------------------------------------------------------------
    // CountWhereEqualsAsync — with andWhereNotNull
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CountWhereEqualsAsync_WithAndWhereNotNull_CountsCorrectSubset()
    {
        await using var ctx = MakeContext();
        var gw = new TableGateway<CountItem, int>(ctx);

        await gw.CreateAsync(new CountItem { Queue = "default", FetchedAt = null });
        await gw.CreateAsync(new CountItem { Queue = "default", FetchedAt = DateTime.UtcNow });
        await gw.CreateAsync(new CountItem { Queue = "default", FetchedAt = DateTime.UtcNow });
        await gw.CreateAsync(new CountItem { Queue = "critical", FetchedAt = DateTime.UtcNow });

        var count = await gw.CountWhereEqualsAsync("queue", "default", andWhereNotNull: "fetched_at");

        Assert.Equal(2, count);
    }
}
