using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using AuditResolver = global::pengdows.crud.AuditValueResolver;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for all three audit timestamp paths:
///   1. Entity property is DateTime  → uses IAuditValues.UtcNow directly
///   2. Entity property is DateTimeOffset + resolver supplies TimestampOffset → used directly
///   3. Entity property is DateTimeOffset + TimestampOffset is null → wraps UtcNow as UTC DateTimeOffset
/// The audit resolver ALWAYS returns UTC.
/// </summary>
[Collection("SqliteSerial")]
public class AuditTimestampOptionsTests : SqlLiteContextTestBase
{
    #region Entities

    [Table("DateTimeAudit")]
    private class DateTimeAuditEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey(1)]
        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [CreatedOn]
        [Column("CreatedOn", DbType.DateTime)]
        public DateTime CreatedOn { get; set; }

        [LastUpdatedOn]
        [Column("LastUpdatedOn", DbType.DateTime)]
        public DateTime LastUpdatedOn { get; set; }
    }

    [Table("DtoTimestampAudit")]
    private class DateTimeOffsetAuditEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey(1)]
        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [CreatedOn]
        [Column("CreatedOn", DbType.DateTimeOffset)]
        public DateTimeOffset CreatedOn { get; set; }

        [LastUpdatedOn]
        [Column("LastUpdatedOn", DbType.DateTimeOffset)]
        public DateTimeOffset LastUpdatedOn { get; set; }
    }

    #endregion

    #region Stub resolvers

    /// <summary>Returns a DateTimeOffset via TimestampOffset; UtcNow is the same instant.</summary>
    private sealed class TimestampOffsetResolver : AuditResolver
    {
        private readonly DateTimeOffset _ts;

        public TimestampOffsetResolver(DateTimeOffset ts) => _ts = ts;

        public override IAuditValues Resolve() => new AuditValues
        {
            UserId = "test-user",
            UtcNow = _ts.UtcDateTime,
            TimestampOffset = _ts // explicit UTC DateTimeOffset
        };
    }

    /// <summary>Returns only UtcNow (no TimestampOffset) — simulates existing resolvers.</summary>
    private sealed class UtcNowOnlyResolver : AuditResolver
    {
        private readonly DateTime _utcNow;

        public UtcNowOnlyResolver(DateTime utcNow) => _utcNow = utcNow;

        public override IAuditValues Resolve() => new AuditValues
        {
            UserId = "test-user",
            UtcNow = _utcNow
            // TimestampOffset intentionally not set
        };
    }

    #endregion

    // -------------------------------------------------------------------------
    // Path 1: DateTime entity property → UtcNow used directly
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Path1_DateTimeProperty_UsesUtcNowDirectly()
    {
        // Arrange
        TypeMap.Register<DateTimeAuditEntity>();
        var expected = new DateTime(2025, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var resolver = new UtcNowOnlyResolver(expected);
        var gateway = new TableGateway<DateTimeAuditEntity, int>(Context, resolver);
        await CreateDateTimeAuditTable();

        // Act
        var entity = new DateTimeAuditEntity { Name = "path1" };
        var ok = await gateway.CreateAsync(entity, Context);

        // Assert
        Assert.True(ok);
        Assert.Equal(expected, entity.CreatedOn);
        Assert.Equal(expected, entity.LastUpdatedOn);
    }

    // -------------------------------------------------------------------------
    // Path 2: DateTimeOffset property + resolver supplies TimestampOffset → used directly
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Path2_DateTimeOffsetProperty_WithExplicitTimestampOffset_UsesItDirectly()
    {
        // Arrange
        TypeMap.Register<DateTimeOffsetAuditEntity>();
        var expected = new DateTimeOffset(2025, 6, 15, 10, 0, 0, TimeSpan.Zero); // UTC
        var resolver = new TimestampOffsetResolver(expected);
        var gateway = new TableGateway<DateTimeOffsetAuditEntity, int>(Context, resolver);
        await CreateDateTimeOffsetAuditTable();

        // Act
        var entity = new DateTimeOffsetAuditEntity { Name = "path2" };
        var ok = await gateway.CreateAsync(entity, Context);

        // Assert
        Assert.True(ok);
        Assert.Equal(expected, entity.CreatedOn);
        Assert.Equal(TimeSpan.Zero, entity.CreatedOn.Offset); // always UTC
        Assert.Equal(expected, entity.LastUpdatedOn);
    }

    [Fact]
    public async Task Path2_TimestampOffset_MustBeUtc()
    {
        // Non-UTC TimestampOffset must be rejected — resolver contract is always UTC.
        var nonUtc = new DateTimeOffset(2025, 6, 15, 10, 0, 0, TimeSpan.FromHours(5));
        var resolver = new TimestampOffsetResolver(nonUtc);
        TypeMap.Register<DateTimeOffsetAuditEntity>();
        var gateway = new TableGateway<DateTimeOffsetAuditEntity, int>(Context, resolver);
        await CreateDateTimeOffsetAuditTable();

        var entity = new DateTimeOffsetAuditEntity { Name = "utc-check" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.CreateAsync(entity, Context));
    }

    [Fact]
    public async Task Path2_TimestampOffset_NonUtc_ErrorMessageIncludesActualOffset()
    {
        // Error message must clearly state that Offset must be TimeSpan.Zero and show the actual offset,
        // so developers know exactly what went wrong rather than "supply UTC offsets" (ambiguous).
        var offset = TimeSpan.FromHours(5);
        var nonUtc = new DateTimeOffset(2025, 6, 15, 10, 0, 0, offset);
        var resolver = new TimestampOffsetResolver(nonUtc);
        TypeMap.Register<DateTimeOffsetAuditEntity>();
        var gateway = new TableGateway<DateTimeOffsetAuditEntity, int>(Context, resolver);
        await CreateDateTimeOffsetAuditTable();

        var entity = new DateTimeOffsetAuditEntity { Name = "message-check" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.CreateAsync(entity, Context));

        // Message must mention TimeSpan.Zero requirement and the actual offset value.
        Assert.Contains("TimeSpan.Zero", ex.Message);
        Assert.Contains(offset.ToString(), ex.Message);
    }

    // -------------------------------------------------------------------------
    // Path 3: DateTimeOffset property + TimestampOffset null → wraps UtcNow as UTC
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Path3_DateTimeOffsetProperty_NullTimestampOffset_WrapsUtcNow()
    {
        // Arrange
        TypeMap.Register<DateTimeOffsetAuditEntity>();
        var utcNow = new DateTime(2025, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var resolver = new UtcNowOnlyResolver(utcNow); // no TimestampOffset
        var gateway = new TableGateway<DateTimeOffsetAuditEntity, int>(Context, resolver);
        await CreateDateTimeOffsetAuditTable();

        // Act
        var entity = new DateTimeOffsetAuditEntity { Name = "path3" };
        var ok = await gateway.CreateAsync(entity, Context);

        // Assert
        Assert.True(ok);
        Assert.Equal(utcNow, entity.CreatedOn.UtcDateTime);
        Assert.Equal(TimeSpan.Zero, entity.CreatedOn.Offset); // wrapped as UTC
    }

    // -------------------------------------------------------------------------
    // Regression: existing resolver with neither field → framework clock used
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Regression_NoResolver_DateTimeOffset_FallsBackToFrameworkUtcClock()
    {
        TypeMap.Register<DateTimeOffsetAuditEntity>();
        var gateway = new TableGateway<DateTimeOffsetAuditEntity, int>(Context); // no resolver
        await CreateDateTimeOffsetAuditTable();

        var before = DateTimeOffset.UtcNow;
        var entity = new DateTimeOffsetAuditEntity { Name = "regression" };
        await gateway.CreateAsync(entity, Context);
        var after = DateTimeOffset.UtcNow;

        Assert.True(entity.CreatedOn >= before && entity.CreatedOn <= after);
        Assert.Equal(TimeSpan.Zero, entity.CreatedOn.Offset);
    }

    #region Table helpers

    private async Task CreateDateTimeAuditTable()
    {
        using var sc = Context.CreateSqlContainer(
            "CREATE TABLE IF NOT EXISTS DateTimeAudit " +
            "(Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, " +
            "CreatedOn TEXT NOT NULL, LastUpdatedOn TEXT NOT NULL)");
        await sc.ExecuteNonQueryAsync();
    }

    private async Task CreateDateTimeOffsetAuditTable()
    {
        using var sc = Context.CreateSqlContainer(
            "CREATE TABLE IF NOT EXISTS DtoTimestampAudit " +
            "(Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, " +
            "CreatedOn TEXT NOT NULL, LastUpdatedOn TEXT NOT NULL)");
        await sc.ExecuteNonQueryAsync();
    }

    #endregion
}