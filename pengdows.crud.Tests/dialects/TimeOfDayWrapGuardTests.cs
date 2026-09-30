using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Found live 2026-09-29 (adversarial type mapping): Sybase ASE stored TimeSpan -01:00:00 in TIME as
/// 23:00:00 and Informix stored 1.00:00:00 (24 h) in DATETIME HOUR TO SECOND as 00:00:00 — silently a
/// different value; pengdows.flatfile stored -01:00:00 and then could not read it back. Their TIME
/// holds only a time of day, so the library rejects a TimeSpan outside
/// [00:00:00, 24:00:00) before binding. Databases whose TIME holds more (MySQL: ±838 h) or that
/// reject it themselves (PostgreSQL, Firebird, DuckDB, SQL Server) are unchanged.
/// </summary>
public sealed class TimeOfDayWrapGuardTests
{
    private static SqlDialect Dialect(SupportedDatabase database) =>
        (SqlDialect)SqlDialectFactory.CreateDialectForType(database, new fakeDbFactory(database),
            NullLogger<SqlDialect>.Instance);

    [Theory]
    [InlineData(SupportedDatabase.SybaseASE, -1)]
    [InlineData(SupportedDatabase.SybaseASE, 24)]
    [InlineData(SupportedDatabase.SybaseASE, 25)]
    [InlineData(SupportedDatabase.Informix, -1)]
    [InlineData(SupportedDatabase.Informix, 24)]
    [InlineData(SupportedDatabase.Informix, 25)]
    [InlineData(SupportedDatabase.FlatFile, -1)]
    [InlineData(SupportedDatabase.FlatFile, 24)]
    public void TimeSpanOutsideADay_IsRejectedWhereTheDriverWouldWrapIt(SupportedDatabase database, int hours)
    {
        var dialect = Dialect(database);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            dialect.CreateDbParameter("t", DbType.Time, TimeSpan.FromHours(hours)));
        Assert.Contains("time of day", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            dialect.CreateDbParameter<TimeSpan?>("t", DbType.Time, TimeSpan.FromHours(hours)));
    }

    [Theory]
    [InlineData(SupportedDatabase.SybaseASE)]
    [InlineData(SupportedDatabase.Informix)]
    [InlineData(SupportedDatabase.FlatFile)]
    public void TimeOfDayValues_StillBind(SupportedDatabase database)
    {
        var dialect = Dialect(database);

        Assert.NotNull(dialect.CreateDbParameter("t", DbType.Time, TimeSpan.Zero));
        Assert.NotNull(dialect.CreateDbParameter("t", DbType.Time, new TimeSpan(0, 23, 59, 59).Add(TimeSpan.FromTicks(9_999_999))));
        Assert.NotNull(dialect.CreateDbParameter("t", DbType.Time, new TimeOnly(23, 59, 59)));
    }

    [Theory]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.SqlServer)]
    public void OtherDialects_AreUnchanged(SupportedDatabase database)
    {
        Assert.NotNull(Dialect(database).CreateDbParameter("t", DbType.Time, TimeSpan.FromHours(-1)));
    }

    [Fact]
    public async Task CreateAsync_OnSybase_RejectsAnOutOfDayTimeSpanBeforeWriting()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SybaseASE);
        await using var context = new DatabaseContext("Server=x;Database=y;EmulatedProduct=SybaseASE", factory);
        var gateway = new TableGateway<Slot, int>(context);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await gateway.CreateAsync(new Slot { Id = 1, At = TimeSpan.FromHours(-1) }));

        Assert.DoesNotContain(factory.CreatedConnections, c => c.ExecutedNonQueryTexts.Exists(t => t.Contains("INSERT")));
    }

    [Table("slots")]
    private sealed class Slot
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("at", DbType.Time)] public TimeSpan At { get; set; }
    }
}
