using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Verifies that DateTime values with DateTimeKind.Unspecified returned by database drivers
/// (Snowflake TIMESTAMP_NTZ, SQL Server datetime, MySQL datetime, Oracle DATE) are normalized
/// to DateTimeKind.Utc by the compiled reader mapper, preventing a 6-hour (or other TZ-offset)
/// drift when the test host runs in a non-UTC timezone.
/// </summary>
[Collection("SqliteSerial")]
public class DateTimeNormalizationTests : SqlLiteContextTestBase
{
    [Table("dt_test")]
    private class DateTimeEntity
    {
        [Id(writable: true)]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [Column("created_at", DbType.DateTime)]
        public DateTime CreatedAt { get; set; }
    }

    public DateTimeNormalizationTests()
    {
        TypeMap.Register<DateTimeEntity>();
    }

    // -------------------------------------------------------------------------
    // Unspecified → UTC
    // -------------------------------------------------------------------------

    [Fact]
    public void MapReaderToObject_WhenDriverReturnsUnspecifiedKind_NormalizesToUtc()
    {
        var helper = new TableGateway<DateTimeEntity, long>(Context);

        var stored = new DateTime(2026, 2, 24, 1, 10, 0, DateTimeKind.Unspecified);
        var rows = new[] { new Dictionary<string, object> { ["id"] = 1L, ["created_at"] = stored } };

        using var reader = new SimpleDateTimeTrackedReader(rows);
        reader.Read();
        var entity = helper.MapReaderToObject(reader);

        Assert.Equal(DateTimeKind.Utc, entity.CreatedAt.Kind);
        Assert.Equal(stored.Ticks, entity.CreatedAt.Ticks); // value unchanged, only Kind set
    }

    // -------------------------------------------------------------------------
    // Utc → Utc (pass-through, value and kind both unchanged)
    // -------------------------------------------------------------------------

    [Fact]
    public void MapReaderToObject_WhenDriverReturnsUtcKind_RemainsUtc()
    {
        var helper = new TableGateway<DateTimeEntity, long>(Context);

        var stored = new DateTime(2026, 2, 24, 1, 10, 0, DateTimeKind.Utc);
        var rows = new[] { new Dictionary<string, object> { ["id"] = 2L, ["created_at"] = stored } };

        using var reader = new SimpleDateTimeTrackedReader(rows);
        reader.Read();
        var entity = helper.MapReaderToObject(reader);

        Assert.Equal(DateTimeKind.Utc, entity.CreatedAt.Kind);
        Assert.Equal(stored, entity.CreatedAt);
    }

    // -------------------------------------------------------------------------
    // Local → UTC (converted to universal time)
    // -------------------------------------------------------------------------

    [Fact]
    public void MapReaderToObject_WhenDriverReturnsLocalKind_ConvertsToUtc()
    {
        var helper = new TableGateway<DateTimeEntity, long>(Context);

        var stored = new DateTime(2026, 2, 24, 1, 10, 0, DateTimeKind.Local);
        var rows = new[] { new Dictionary<string, object> { ["id"] = 3L, ["created_at"] = stored } };

        using var reader = new SimpleDateTimeTrackedReader(rows);
        reader.Read();
        var entity = helper.MapReaderToObject(reader);

        Assert.Equal(DateTimeKind.Utc, entity.CreatedAt.Kind);
        Assert.Equal(stored.ToUniversalTime(), entity.CreatedAt);
    }

    // -------------------------------------------------------------------------
    // Nullable DateTime column: Unspecified → Utc
    // -------------------------------------------------------------------------

    [Table("dt_nullable")]
    private class NullableDateTimeEntity
    {
        [Id(writable: true)]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [Column("updated_at", DbType.DateTime)]
        public DateTime? UpdatedAt { get; set; }
    }

    [Fact]
    public void MapReaderToObject_NullableDateTimeWithUnspecifiedKind_NormalizesToUtc()
    {
        TypeMap.Register<NullableDateTimeEntity>();
        var helper = new TableGateway<NullableDateTimeEntity, long>(Context);

        var stored = new DateTime(2026, 2, 24, 7, 30, 0, DateTimeKind.Unspecified);
        var rows = new[] { new Dictionary<string, object> { ["id"] = 4L, ["updated_at"] = stored } };

        using var reader = new SimpleDateTimeTrackedReader(rows);
        reader.Read();
        var entity = helper.MapReaderToObject(reader);

        Assert.NotNull(entity.UpdatedAt);
        Assert.Equal(DateTimeKind.Utc, entity.UpdatedAt!.Value.Kind);
        Assert.Equal(stored.Ticks, entity.UpdatedAt.Value.Ticks);
    }

    // -------------------------------------------------------------------------
    // Minimal ITrackedReader adapter over fakeDbDataReader
    // -------------------------------------------------------------------------

    private sealed class SimpleDateTimeTrackedReader : fakeDbDataReader, ITrackedReader
    {
        public SimpleDateTimeTrackedReader(IEnumerable<Dictionary<string, object>> rows) : base(rows) { }

        public new Task<bool> ReadAsync() => base.ReadAsync(CancellationToken.None);

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
