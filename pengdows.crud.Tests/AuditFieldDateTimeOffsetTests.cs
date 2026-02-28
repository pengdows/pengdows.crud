using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using Xunit;

namespace pengdows.crud.Tests;

public class AuditFieldDateTimeOffsetTests : SqlLiteContextTestBase
{
    #region Test Entities

    [Table("DtoAudit")]
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

    [Table("NullableDtoAudit")]
    private class NullableDateTimeOffsetAuditEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey(1)]
        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [CreatedOn]
        [Column("CreatedOn", DbType.DateTimeOffset)]
        public DateTimeOffset? CreatedOn { get; set; }

        [LastUpdatedOn]
        [Column("LastUpdatedOn", DbType.DateTimeOffset)]
        public DateTimeOffset? LastUpdatedOn { get; set; }
    }

    #endregion

    #region DateTimeOffset type correctness

    [Fact]
    public async Task SetAuditFields_CreatedOn_DateTimeOffset_SetsCorrectType()
    {
        // Arrange
        TypeMap.Register<DateTimeOffsetAuditEntity>();
        var helper = new TableGateway<DateTimeOffsetAuditEntity, int>(Context);

        await CreateDtoAuditTable();

        // Act
        var entity = new DateTimeOffsetAuditEntity { Name = Guid.NewGuid().ToString() };
        var success = await helper.CreateAsync(entity, Context);

        // Assert
        Assert.True(success);
        Assert.NotEqual(default, entity.CreatedOn);
        Assert.True(entity.CreatedOn.Offset == TimeSpan.Zero, "CreatedOn should be UTC");
    }

    [Fact]
    public async Task SetAuditFields_LastUpdatedOn_DateTimeOffset_SetsCorrectType()
    {
        // Arrange
        TypeMap.Register<DateTimeOffsetAuditEntity>();
        var helper = new TableGateway<DateTimeOffsetAuditEntity, int>(Context);

        await CreateDtoAuditTable();

        // Act
        var entity = new DateTimeOffsetAuditEntity { Name = Guid.NewGuid().ToString() };
        await helper.CreateAsync(entity, Context);

        // Assert
        Assert.NotEqual(default, entity.LastUpdatedOn);
        Assert.True(entity.LastUpdatedOn.Offset == TimeSpan.Zero, "LastUpdatedOn should be UTC");
    }

    #endregion

    #region Preserve existing values

    [Fact]
    public async Task SetAuditFields_CreatedOn_DateTimeOffset_PreservesExistingValue()
    {
        // Arrange
        TypeMap.Register<DateTimeOffsetAuditEntity>();
        var helper = new TableGateway<DateTimeOffsetAuditEntity, int>(Context);

        await CreateDtoAuditTable();

        var presetTime = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.FromHours(5));
        var entity = new DateTimeOffsetAuditEntity
        {
            Name = Guid.NewGuid().ToString(),
            CreatedOn = presetTime
        };

        // Act
        await helper.CreateAsync(entity, Context);

        // Assert — user-set CreatedOn should be preserved
        Assert.Equal(presetTime, entity.CreatedOn);
    }

    [Fact]
    public async Task SetAuditFields_CreatedOn_DateTime_PreservesExistingValue()
    {
        // Arrange — regression test for DateTime path
        TypeMap.Register<DateTimeAuditEntity>();
        var helper = new TableGateway<DateTimeAuditEntity, int>(Context);

        await CreateDateTimeAuditTable();

        var presetTime = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var entity = new DateTimeAuditEntity
        {
            Name = Guid.NewGuid().ToString(),
            CreatedOn = presetTime
        };

        // Act
        await helper.CreateAsync(entity, Context);

        // Assert — user-set CreatedOn should be preserved
        Assert.Equal(presetTime, entity.CreatedOn);
    }

    #endregion

    #region Nullable DateTimeOffset

    [Fact]
    public async Task SetAuditFields_CreatedOn_NullableDateTimeOffset_Default_SetsValue()
    {
        // Arrange
        TypeMap.Register<NullableDateTimeOffsetAuditEntity>();
        var helper = new TableGateway<NullableDateTimeOffsetAuditEntity, int>(Context);

        await CreateNullableDtoAuditTable();

        // Act — null CreatedOn? should get populated
        var entity = new NullableDateTimeOffsetAuditEntity { Name = Guid.NewGuid().ToString() };
        await helper.CreateAsync(entity, Context);

        // Assert
        Assert.NotNull(entity.CreatedOn);
        Assert.True(entity.CreatedOn!.Value.Offset == TimeSpan.Zero, "CreatedOn should be UTC");
    }

    #endregion

    #region DateTime regression

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

    [Fact]
    public async Task SetAuditFields_LastUpdatedOn_DateTime_StillWorks()
    {
        // Arrange — regression: DateTime properties must continue to work
        TypeMap.Register<DateTimeAuditEntity>();
        var helper = new TableGateway<DateTimeAuditEntity, int>(Context);

        await CreateDateTimeAuditTable();

        // Act
        var entity = new DateTimeAuditEntity { Name = Guid.NewGuid().ToString() };
        await helper.CreateAsync(entity, Context);

        // Assert
        Assert.True(entity.CreatedOn > DateTime.MinValue);
        Assert.True(entity.LastUpdatedOn > DateTime.MinValue);
    }

    #endregion

    #region Table Creation Helpers

    private async Task CreateDtoAuditTable()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var sql = string.Format(
            @"CREATE TABLE IF NOT EXISTS {0}DtoAudit{1} (
                {0}Id{1} INTEGER PRIMARY KEY AUTOINCREMENT,
                {0}Name{1} TEXT UNIQUE NOT NULL,
                {0}CreatedOn{1} TIMESTAMP NOT NULL,
                {0}LastUpdatedOn{1} TIMESTAMP NOT NULL
            )", qp, qs);
        var container = Context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    private async Task CreateNullableDtoAuditTable()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var sql = string.Format(
            @"CREATE TABLE IF NOT EXISTS {0}NullableDtoAudit{1} (
                {0}Id{1} INTEGER PRIMARY KEY AUTOINCREMENT,
                {0}Name{1} TEXT UNIQUE NOT NULL,
                {0}CreatedOn{1} TIMESTAMP,
                {0}LastUpdatedOn{1} TIMESTAMP
            )", qp, qs);
        var container = Context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    private async Task CreateDateTimeAuditTable()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var sql = string.Format(
            @"CREATE TABLE IF NOT EXISTS {0}DateTimeAudit{1} (
                {0}Id{1} INTEGER PRIMARY KEY AUTOINCREMENT,
                {0}Name{1} TEXT UNIQUE NOT NULL,
                {0}CreatedOn{1} TIMESTAMP NOT NULL,
                {0}LastUpdatedOn{1} TIMESTAMP NOT NULL
            )", qp, qs);
        var container = Context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    #endregion
}