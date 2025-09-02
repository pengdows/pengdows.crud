using System;
using System.Threading.Tasks;
using Xunit;

namespace pengdows.crud.Tests;

public class NonInsertableNonUpdateableColumnTests : SqlLiteContextTestBase
{
    [Fact]
    public void BuildCreate_SkipsNonInsertableColumn()
    {
        TypeMap.Register<NonInsertableColumnEntity>();
        var helper = new EntityHelper<NonInsertableColumnEntity, int>(Context);
        var entity = new NonInsertableColumnEntity { Id = 1, Name = "Foo", Secret = "Bar" };

        var container = helper.BuildCreate(entity);
        var sql = container.Query.ToString();

        var columnSecret = Context.WrapObjectName("Secret");
        var columnName = Context.WrapObjectName("Name");
        Assert.DoesNotContain(columnSecret, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(columnName, sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildUpdate_SkipsNonUpdateableColumn()
    {
        TypeMap.Register<NonInsertableColumnEntity>();
        var helper = new EntityHelper<NonInsertableColumnEntity, int>(Context);
        var entity = new NonInsertableColumnEntity { Id = 1, Name = "Foo", Secret = "Bar" };

        var sc = await helper.BuildUpdateAsync(entity);
        var sql = sc.Query.ToString();

        var columnSecret = Context.WrapObjectName("Secret");
        var columnName = Context.WrapObjectName("Name");
        Assert.DoesNotContain(columnSecret, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(columnName, sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildUpdateAsync_OnlyNonUpdateableChanged_Throws()
    {
        TypeMap.Register<NonInsertableColumnEntity>();
        var helper = new EntityHelper<NonInsertableColumnEntity, int>(Context);

        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var createTableSql = $"CREATE TABLE IF NOT EXISTS {qp}NonInsertableColumnEntity{qs} ({qp}Id{qs} INTEGER PRIMARY KEY AUTOINCREMENT, {qp}Name{qs} TEXT, {qp}Secret{qs} TEXT)";
        var tableContainer = Context.CreateSqlContainer();
        tableContainer.Query.Append(createTableSql);
        await tableContainer.ExecuteNonQueryAsync();

        var original = new NonInsertableColumnEntity { Id = 1, Name = "Foo", Secret = "Original" };
        var insert = helper.BuildCreate(original);
        await insert.ExecuteNonQueryAsync();

        var updated = new NonInsertableColumnEntity { Id = 1, Name = "Foo", Secret = "Changed" };

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await helper.BuildUpdateAsync(updated, true));
    }
}
