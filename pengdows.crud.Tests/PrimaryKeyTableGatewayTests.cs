// =============================================================================
// FILE: PrimaryKeyTableGatewayTests.cs
// PURPOSE: TDD tests for PrimaryKeyTableGateway<TEntity> — written RED before
//          the class exists. All tests target entities with [PrimaryKey] columns
//          and no [Id] surrogate.
// =============================================================================

using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

[Collection("SqliteSerial")]
public class PrimaryKeyTableGatewayTests
{
    // -------------------------------------------------------------------------
    // Test entities
    // -------------------------------------------------------------------------

    /// <summary>
    /// Composite natural-key junction table — no surrogate [Id].
    /// Models a classic DBA-owned table pengdows.crud currently cannot handle.
    /// </summary>
    [Table("order_line")]
    public class OrderLine
    {
        [PrimaryKey(1)]
        [Column("order_id", DbType.Int32)]
        public int OrderId { get; set; }

        [PrimaryKey(2)]
        [Column("line_number", DbType.Int32)]
        public int LineNumber { get; set; }

        [Column("product_code", DbType.String)]
        public string ProductCode { get; set; } = string.Empty;

        [Column("quantity", DbType.Int32)]
        public int Quantity { get; set; }
    }

    /// <summary>Single-column natural key.</summary>
    [Table("category")]
    public class Category
    {
        [PrimaryKey]
        [Column("code", DbType.String)]
        public string Code { get; set; } = string.Empty;

        [Column("label", DbType.String)]
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>Entity with no [PrimaryKey] at all — used to verify guard throws.</summary>
    [Table("no_key_entity")]
    public class NoKeyEntity
    {
        [Column("value", DbType.String)]
        public string Value { get; set; } = string.Empty;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static IDatabaseContext MakeContext(SupportedDatabase db)
    {
        var factory = new fakeDbFactory(db);
        factory.EnableDataPersistence = true;
        var cs = db switch
        {
            SupportedDatabase.PostgreSql  => "Host=localhost;EmulatedProduct=PostgreSql",
            SupportedDatabase.MySql       => "Server=localhost;EmulatedProduct=MySql",
            SupportedDatabase.SqlServer   => "Server=localhost;EmulatedProduct=SqlServer",
            _                             => "Data Source=:memory:;EmulatedProduct=Sqlite"
        };
        return new DatabaseContext(cs, factory);
    }

    // =========================================================================
    // Construction
    // =========================================================================

    [Fact]
    public void Constructor_EntityWithPrimaryKey_Succeeds()
    {
        using var ctx = MakeContext(SupportedDatabase.Sqlite);
        // PrimaryKeyTableGateway<T> does not yet exist — this test is RED.
        var gw = new PrimaryKeyTableGateway<OrderLine>(ctx);
        Assert.NotNull(gw);
    }

    [Fact]
    public void Constructor_EntityWithNoPrimaryKey_Throws()
    {
        using var ctx = MakeContext(SupportedDatabase.Sqlite);
        Assert.Throws<InvalidOperationException>(() =>
            new PrimaryKeyTableGateway<NoKeyEntity>(ctx));
    }

    [Fact]
    public void ImplementsInterface()
    {
        using var ctx = MakeContext(SupportedDatabase.Sqlite);
        var gw = new PrimaryKeyTableGateway<OrderLine>(ctx);
        Assert.IsAssignableFrom<IPrimaryKeyTableGateway<OrderLine>>(gw);
    }

    // =========================================================================
    // CREATE
    // =========================================================================

    [Fact]
    public void BuildCreate_ProducesInsertWithAllColumns()
    {
        using var ctx = MakeContext(SupportedDatabase.Sqlite);
        var gw = new PrimaryKeyTableGateway<OrderLine>(ctx);
        var line = new OrderLine { OrderId = 1, LineNumber = 1, ProductCode = "SKU-001", Quantity = 2 };

        var sc = gw.BuildCreate(line);
        var sql = sc.Query.ToString();

        Assert.Contains("INSERT INTO", sql);
        Assert.Contains("order_line", sql);
        Assert.Contains("order_id", sql);
        Assert.Contains("line_number", sql);
        Assert.Contains("product_code", sql);
        Assert.Contains("quantity", sql);
    }

    [Fact]
    public async Task CreateAsync_ReturnsTrueOnSuccess()
    {
        using var ctx = MakeContext(SupportedDatabase.Sqlite);
        var gw = new PrimaryKeyTableGateway<OrderLine>(ctx);
        var line = new OrderLine { OrderId = 1, LineNumber = 1, ProductCode = "SKU-001", Quantity = 2 };

        var result = await gw.CreateAsync(line);
        Assert.True(result);
    }

    // =========================================================================
    // RETRIEVE
    // =========================================================================

    [Fact]
    public void BuildBaseRetrieve_ProducesSelectFromTable()
    {
        using var ctx = MakeContext(SupportedDatabase.Sqlite);
        var gw = new PrimaryKeyTableGateway<OrderLine>(ctx);

        var sc = gw.BuildBaseRetrieve("ol");
        var sql = sc.Query.ToString();

        Assert.Contains("SELECT", sql);
        Assert.Contains("order_line", sql);
    }

    [Fact]
    public void BuildRetrieve_ByEntityList_ProducesWhereUsingPrimaryKeyColumns()
    {
        using var ctx = MakeContext(SupportedDatabase.Sqlite);
        var gw = new PrimaryKeyTableGateway<OrderLine>(ctx);
        var lines = new[]
        {
            new OrderLine { OrderId = 1, LineNumber = 1 },
            new OrderLine { OrderId = 1, LineNumber = 2 }
        };

        var sc = gw.BuildRetrieve(lines, "ol");
        var sql = sc.Query.ToString();

        Assert.Contains("order_id", sql);
        Assert.Contains("line_number", sql);
        Assert.Contains("WHERE", sql);
    }

    [Fact]
    public async Task RetrieveOneAsync_ByEntity_QueriesUsingPrimaryKeyColumns()
    {
        using var ctx = MakeContext(SupportedDatabase.Sqlite);
        var gw = new PrimaryKeyTableGateway<OrderLine>(ctx);
        var line = new OrderLine { OrderId = 5, LineNumber = 3 };

        // fakeDb returns no rows — result is null, but SQL must be generated without throwing
        var result = await gw.RetrieveOneAsync(line);
        Assert.Null(result);
    }

    // =========================================================================
    // UPDATE — WHERE must use [PrimaryKey], NOT a non-existent [Id]
    // =========================================================================

    [Fact]
    public async Task BuildUpdateAsync_WhereClauseUsesPrimaryKeyColumns()
    {
        using var ctx = MakeContext(SupportedDatabase.Sqlite);
        var gw = new PrimaryKeyTableGateway<OrderLine>(ctx);
        var line = new OrderLine { OrderId = 1, LineNumber = 2, ProductCode = "SKU-002", Quantity = 5 };

        var sc = await gw.BuildUpdateAsync(line);
        var sql = sc.Query.ToString();

        Assert.Contains("UPDATE", sql);
        Assert.Contains("order_line", sql);
        Assert.Contains("WHERE", sql);
        Assert.Contains("order_id", sql);
        Assert.Contains("line_number", sql);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsAffectedRows()
    {
        using var ctx = MakeContext(SupportedDatabase.Sqlite);
        var gw = new PrimaryKeyTableGateway<OrderLine>(ctx);
        var line = new OrderLine { OrderId = 1, LineNumber = 1, ProductCode = "UPDATED", Quantity = 10 };

        // fakeDb returns 1 for DML
        var rows = await gw.UpdateAsync(line);
        Assert.True(rows >= 0);
    }

    // =========================================================================
    // UPSERT
    // =========================================================================

    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.SqlServer)]
    public void BuildUpsert_AllDialects_ProducesConflictOnPrimaryKeyColumns(SupportedDatabase db)
    {
        using var ctx = MakeContext(db);
        var gw = new PrimaryKeyTableGateway<OrderLine>(ctx);
        var line = new OrderLine { OrderId = 1, LineNumber = 1, ProductCode = "SKU-001", Quantity = 3 };

        var sc = gw.BuildUpsert(line);
        var sql = sc.Query.ToString();

        // Must reference the PK columns in the conflict / WHEN MATCHED clause
        Assert.Contains("order_id", sql);
        Assert.Contains("line_number", sql);
    }

    [Fact]
    public async Task UpsertAsync_DoesNotThrow()
    {
        using var ctx = MakeContext(SupportedDatabase.Sqlite);
        var gw = new PrimaryKeyTableGateway<OrderLine>(ctx);
        var line = new OrderLine { OrderId = 2, LineNumber = 1, ProductCode = "SKU-X", Quantity = 1 };

        var rows = await gw.UpsertAsync(line);
        Assert.True(rows >= 0);
    }

    // =========================================================================
    // BATCH CREATE
    // =========================================================================

    [Fact]
    public void BuildBatchCreate_MultipleEntities_ProducesInsertWithAllColumns()
    {
        using var ctx = MakeContext(SupportedDatabase.Sqlite);
        var gw = new PrimaryKeyTableGateway<OrderLine>(ctx);
        var lines = new[]
        {
            new OrderLine { OrderId = 1, LineNumber = 1, ProductCode = "A", Quantity = 1 },
            new OrderLine { OrderId = 1, LineNumber = 2, ProductCode = "B", Quantity = 2 }
        };

        var containers = gw.BuildBatchCreate(lines);
        Assert.NotEmpty(containers);
        var sql = containers[0].Query.ToString();
        Assert.Contains("INSERT INTO", sql);
        Assert.Contains("order_line", sql);
    }

    [Fact]
    public async Task BatchCreateAsync_ReturnsInsertedCount()
    {
        using var ctx = MakeContext(SupportedDatabase.Sqlite);
        var gw = new PrimaryKeyTableGateway<OrderLine>(ctx);
        var lines = new[]
        {
            new OrderLine { OrderId = 1, LineNumber = 1, ProductCode = "A", Quantity = 1 },
            new OrderLine { OrderId = 1, LineNumber = 2, ProductCode = "B", Quantity = 2 }
        };

        var rows = await gw.BatchCreateAsync(lines);
        Assert.True(rows >= 0);
    }

    // =========================================================================
    // BATCH UPDATE — WHERE must use [PrimaryKey]
    // =========================================================================

    [Fact]
    public void BuildBatchUpdate_WhereClauseUsesPrimaryKeyColumns()
    {
        using var ctx = MakeContext(SupportedDatabase.Sqlite);
        var gw = new PrimaryKeyTableGateway<OrderLine>(ctx);
        var lines = new[]
        {
            new OrderLine { OrderId = 1, LineNumber = 1, ProductCode = "X", Quantity = 9 },
            new OrderLine { OrderId = 1, LineNumber = 2, ProductCode = "Y", Quantity = 8 }
        };

        var containers = gw.BuildBatchUpdate(lines);
        Assert.NotEmpty(containers);
        var sql = containers[0].Query.ToString();
        Assert.Contains("UPDATE", sql);
        Assert.Contains("order_id", sql);
        Assert.Contains("line_number", sql);
    }

    // =========================================================================
    // DELETE
    // =========================================================================

    [Fact]
    public void BuildBatchDelete_ByEntityList_ProducesDeleteWithPrimaryKeyWhere()
    {
        using var ctx = MakeContext(SupportedDatabase.Sqlite);
        var gw = new PrimaryKeyTableGateway<OrderLine>(ctx);
        var lines = new[]
        {
            new OrderLine { OrderId = 1, LineNumber = 1 },
            new OrderLine { OrderId = 1, LineNumber = 2 }
        };

        var containers = gw.BuildBatchDelete(lines);
        Assert.NotEmpty(containers);
        var sql = containers[0].Query.ToString();
        Assert.Contains("DELETE", sql);
        Assert.Contains("order_id", sql);
        Assert.Contains("line_number", sql);
    }

    [Fact]
    public async Task BatchDeleteAsync_ByEntityList_ReturnsAffectedRows()
    {
        using var ctx = MakeContext(SupportedDatabase.Sqlite);
        var gw = new PrimaryKeyTableGateway<OrderLine>(ctx);
        var lines = new[]
        {
            new OrderLine { OrderId = 1, LineNumber = 1 },
        };

        var rows = await gw.BatchDeleteAsync(lines);
        Assert.True(rows >= 0);
    }

    // =========================================================================
    // SINGLE-COLUMN PK
    // =========================================================================

    [Fact]
    public void BuildCreate_SingleColumnPk_ProducesInsert()
    {
        using var ctx = MakeContext(SupportedDatabase.Sqlite);
        var gw = new PrimaryKeyTableGateway<Category>(ctx);
        var cat = new Category { Code = "ELEC", Label = "Electronics" };

        var sc = gw.BuildCreate(cat);
        var sql = sc.Query.ToString();
        Assert.Contains("INSERT INTO", sql);
        Assert.Contains("category", sql);
        Assert.Contains("code", sql);
        Assert.Contains("label", sql);
    }

    [Fact]
    public async Task BuildUpdateAsync_SingleColumnPk_WhereUsesCodeColumn()
    {
        using var ctx = MakeContext(SupportedDatabase.Sqlite);
        var gw = new PrimaryKeyTableGateway<Category>(ctx);
        var cat = new Category { Code = "ELEC", Label = "Electronics v2" };

        var sc = await gw.BuildUpdateAsync(cat);
        var sql = sc.Query.ToString();
        Assert.Contains("UPDATE", sql);
        Assert.Contains("WHERE", sql);
        Assert.Contains("code", sql);
    }

    // =========================================================================
    // LOAD helpers (execute a pre-built container)
    // =========================================================================

    [Fact]
    public async Task LoadListAsync_WithBaseRetrieve_ReturnsEmptyListFromFakeDb()
    {
        using var ctx = MakeContext(SupportedDatabase.Sqlite);
        var gw = new PrimaryKeyTableGateway<OrderLine>(ctx);
        var sc = gw.BuildBaseRetrieve("ol");

        var results = await gw.LoadListAsync(sc);
        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Fact]
    public async Task LoadSingleAsync_WithBaseRetrieve_ReturnsNullFromFakeDb()
    {
        using var ctx = MakeContext(SupportedDatabase.Sqlite);
        var gw = new PrimaryKeyTableGateway<OrderLine>(ctx);
        var sc = gw.BuildBaseRetrieve("ol");

        var result = await gw.LoadSingleAsync(sc);
        Assert.Null(result);
    }
}
