using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using Xunit;

namespace pengdows.crud.Tests;

public class AuditFieldNonUpdateableNonInsertableTests : SqlLiteContextTestBase
{
    [Table("NonUpdateableLastUpdatedOnEntity")]
    private class NonUpdateableLastUpdatedOnEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey(1)]
        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [LastUpdatedOn]
        [NonUpdateable]
        [Column("LastUpdatedOn", DbType.DateTime)]
        public DateTime LastUpdatedOn { get; set; }
    }

    [Table("NonInsertableCreatedOnEntity")]
    private class NonInsertableCreatedOnEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey(1)]
        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [CreatedOn]
        [NonInsertable]
        [Column("CreatedOn", DbType.DateTime)]
        public DateTime CreatedOn { get; set; }
    }

    [Fact]
    public async Task UpdateAsync_NonUpdateableLastUpdatedOn_DoesNotMutateInMemoryValue()
    {
        TypeMap.Register<NonUpdateableLastUpdatedOnEntity>();
        var helper = new TableGateway<NonUpdateableLastUpdatedOnEntity, int>(Context);
        await CreateNonUpdateableLastUpdatedOnTable();

        var entity = new NonUpdateableLastUpdatedOnEntity { Name = Guid.NewGuid().ToString() };
        await helper.CreateAsync(entity, Context);
        var afterCreate = entity.LastUpdatedOn;

        // Create path is unaffected by NonUpdateable — it's orthogonal to insert.
        Assert.NotEqual(default, afterCreate);

        await Task.Delay(5);
        entity.Name = Guid.NewGuid().ToString();
        await helper.UpdateAsync(entity, Context);

        // The column is excluded from the UPDATE SET clause (IsNonUpdateable), so the
        // in-memory value must not be mutated either — otherwise it diverges from what
        // was actually persisted.
        Assert.Equal(afterCreate, entity.LastUpdatedOn);
    }

    [Fact]
    public async Task CreateAsync_NonInsertableCreatedOn_DoesNotMutateInMemoryValue()
    {
        TypeMap.Register<NonInsertableCreatedOnEntity>();
        var helper = new TableGateway<NonInsertableCreatedOnEntity, int>(Context);
        await CreateNonInsertableCreatedOnTable();

        var entity = new NonInsertableCreatedOnEntity { Name = Guid.NewGuid().ToString() };
        Assert.Equal(default, entity.CreatedOn); // starts default, would trigger the audit setter today

        await helper.CreateAsync(entity, Context);

        // The column is excluded from the INSERT (IsNonInsertable), so the in-memory
        // value must not be mutated either — otherwise it diverges from what was
        // actually persisted (the DB-side DEFAULT).
        Assert.Equal(default, entity.CreatedOn);
    }

    private async Task CreateNonUpdateableLastUpdatedOnTable()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var sql = string.Format(
            @"CREATE TABLE IF NOT EXISTS {0}NonUpdateableLastUpdatedOnEntity{1} (
                {0}Id{1} INTEGER PRIMARY KEY AUTOINCREMENT,
                {0}Name{1} TEXT UNIQUE NOT NULL,
                {0}LastUpdatedOn{1} TIMESTAMP NOT NULL
            )", qp, qs);
        var container = Context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    private async Task CreateNonInsertableCreatedOnTable()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var sql = string.Format(
            @"CREATE TABLE IF NOT EXISTS {0}NonInsertableCreatedOnEntity{1} (
                {0}Id{1} INTEGER PRIMARY KEY AUTOINCREMENT,
                {0}Name{1} TEXT UNIQUE NOT NULL,
                {0}CreatedOn{1} TIMESTAMP NOT NULL DEFAULT '2000-01-01 00:00:00'
            )", qp, qs);
        var container = Context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }
}
