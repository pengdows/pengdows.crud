using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Integration tests for entities with composite (multi-column) primary keys.
/// </summary>
[Collection("IntegrationTests")]
public class CompositeKeyTests : DatabaseTestBase
{
    public CompositeKeyTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    private static long _nextOrderItemId;
    private static long _nextUserRoleId;

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        context.TypeMapRegistry.Register<OrderItem>();
        context.TypeMapRegistry.Register<UserRole>();

        await RecreateTableAsync(context, "order_items", BuildOrderItemsTableSql(provider, context));
        await RecreateTableAsync(context, "user_roles", BuildUserRolesTableSql(provider, context));
    }

    #region Two-Column Composite Key Tests

    [Fact]
    public Task CreateAsync_CompositeKey_InsertsSuccessfully()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new TableGateway<OrderItem, long>(context);
            var item = CreateOrderItem(100, 200, 5, 19.99m);

            var result = await helper.CreateAsync(item, context);

            Assert.True(result);
            Output.WriteLine($"{provider}: Created OrderItem: Order={item.OrderId}, Product={item.ProductId}");
        });
    }

    [Fact]
    public Task RetrieveOneAsync_ByCompositeKeyObject_ReturnsCorrectEntity()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new TableGateway<OrderItem, long>(context);
            var item = CreateOrderItem(101, 201, 3, 29.99m);
            await helper.CreateAsync(item, context);

            var keyObject = new OrderItem { OrderId = 101, ProductId = 201 };
            var retrieved = await helper.RetrieveOneAsync(keyObject, context);

            Assert.NotNull(retrieved);
            Assert.Equal(101, retrieved.OrderId);
            Assert.Equal(201, retrieved.ProductId);
            Assert.Equal(3, retrieved.Quantity);
            Assert.Equal(29.99m, retrieved.UnitPrice);

            Output.WriteLine($"{provider}: Retrieved Order={retrieved.OrderId}, Product={retrieved.ProductId}");
        });
    }

    [Fact]
    public Task RetrieveOneAsync_NonExistentCompositeKey_ReturnsNull()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new TableGateway<OrderItem, long>(context);
            var keyObject = new OrderItem { OrderId = 999, ProductId = 999 };
            var retrieved = await helper.RetrieveOneAsync(keyObject, context);
            Assert.Null(retrieved);
            Output.WriteLine($"{provider}: Non-existent composite key returns null");
        });
    }

    [Fact]
    public Task UpdateAsync_CompositeKey_UpdatesCorrectRecord()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new TableGateway<OrderItem, long>(context);
            var item = CreateOrderItem(102, 202, 10, 5.99m);
            await helper.CreateAsync(item, context);

            item.Quantity = 15;
            item.UnitPrice = 4.99m;
            var updateCount = await helper.UpdateAsync(item, context);

            Assert.Equal(1, updateCount);

            var keyObject = new OrderItem { OrderId = 102, ProductId = 202 };
            var updated = await helper.RetrieveOneAsync(keyObject, context);
            Assert.NotNull(updated);
            Assert.Equal(15, updated.Quantity);
            Assert.Equal(4.99m, updated.UnitPrice);

            Output.WriteLine($"{provider}: Updated Qty={updated.Quantity}, Price={updated.UnitPrice}");
        });
    }

    [Fact]
    public Task Delete_CompositeKey_DeletesCorrectRecord()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new TableGateway<OrderItem, long>(context);
            var item1 = CreateOrderItem(103, 301, 1, 10m);
            var item2 = CreateOrderItem(103, 302, 2, 20m);
            var item3 = CreateOrderItem(103, 303, 3, 30m);

            await helper.CreateAsync(item1, context);
            await helper.CreateAsync(item2, context);
            await helper.CreateAsync(item3, context);

            var deleteCount = await DeleteOrderItemAsync(context, 103, 302);

            Assert.Equal(1, deleteCount);

            var remaining1 = await helper.RetrieveOneAsync(new OrderItem { OrderId = 103, ProductId = 301 }, context);
            var remaining2 = await helper.RetrieveOneAsync(new OrderItem { OrderId = 103, ProductId = 302 }, context);
            var remaining3 = await helper.RetrieveOneAsync(new OrderItem { OrderId = 103, ProductId = 303 }, context);

            Assert.NotNull(remaining1);
            Assert.Null(remaining2);
            Assert.NotNull(remaining3);

            Output.WriteLine($"{provider}: Deleted item2, item1 and item3 remain");
        });
    }

    [Fact]
    public Task CreateAsync_DuplicateCompositeKey_Fails()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new TableGateway<OrderItem, long>(context);
            var item = CreateOrderItem(104, 204, 1, 9.99m);
            await helper.CreateAsync(item, context);

            var duplicate = CreateOrderItem(104, 204, 5, 19.99m);

            await Assert.ThrowsAnyAsync<Exception>(async () => { await helper.CreateAsync(duplicate, context); });

            Output.WriteLine($"{provider}: Duplicate composite key correctly rejected");
        });
    }

    #endregion

    #region Three-Column Composite Key Tests

    [Fact]
    public Task ThreeColumnKey_Create_Succeeds()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new TableGateway<UserRole, long>(context);
            var role = CreateUserRole(1, 100, 10, "admin");

            var result = await helper.CreateAsync(role, context);

            Assert.True(result);
            Output.WriteLine(
                $"{provider}: Created UserRole Tenant={role.TenantId}, User={role.UserId}, Role={role.RoleId}");
        });
    }

    [Fact]
    public Task ThreeColumnKey_RetrieveOne_ReturnsCorrectEntity()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new TableGateway<UserRole, long>(context);
            var grantedTime = DateTime.UtcNow;
            var role = CreateUserRole(2, 200, 20, "superadmin", grantedTime);
            await helper.CreateAsync(role, context);

            var keyObject = new UserRole { TenantId = 2, UserId = 200, RoleId = 20 };
            var retrieved = await helper.RetrieveOneAsync(keyObject, context);

            Assert.NotNull(retrieved);
            Assert.Equal(2, retrieved.TenantId);
            Assert.Equal(200, retrieved.UserId);
            Assert.Equal(20, retrieved.RoleId);
            Assert.Equal("superadmin", retrieved.GrantedBy);

            Output.WriteLine(
                $"{provider}: Retrieved UserRole Tenant={retrieved.TenantId}, User={retrieved.UserId}, Role={retrieved.RoleId}");
        });
    }

    [Fact]
    public Task ThreeColumnKey_Update_UpdatesCorrectRecord()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new TableGateway<UserRole, long>(context);
            var role = CreateUserRole(3, 300, 30, "old_admin");
            await helper.CreateAsync(role, context);

            role.GrantedBy = "new_admin";
            var updateCount = await helper.UpdateAsync(role, context);

            Assert.Equal(1, updateCount);

            var keyObject = new UserRole { TenantId = 3, UserId = 300, RoleId = 30 };
            var updated = await helper.RetrieveOneAsync(keyObject, context);
            Assert.NotNull(updated);
            Assert.Equal("new_admin", updated.GrantedBy);

            Output.WriteLine($"{provider}: Updated GrantedBy to {updated.GrantedBy}");
        });
    }

    [Fact]
    public Task ThreeColumnKey_Delete_DeletesOnlyMatchingRecord()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new TableGateway<UserRole, long>(context);
            var role1 = CreateUserRole(4, 400, 1, "admin");
            var role2 = CreateUserRole(4, 400, 2, "admin");
            var role3 = CreateUserRole(4, 400, 3, "admin");

            await helper.CreateAsync(role1, context);
            await helper.CreateAsync(role2, context);
            await helper.CreateAsync(role3, context);

            var deleteCount = await DeleteUserRoleAsync(context, 4, 400, 2);

            Assert.Equal(1, deleteCount);

            var remaining1 =
                await helper.RetrieveOneAsync(new UserRole { TenantId = 4, UserId = 400, RoleId = 1 }, context);
            var remaining2 =
                await helper.RetrieveOneAsync(new UserRole { TenantId = 4, UserId = 400, RoleId = 2 }, context);
            var remaining3 =
                await helper.RetrieveOneAsync(new UserRole { TenantId = 4, UserId = 400, RoleId = 3 }, context);

            Assert.NotNull(remaining1);
            Assert.Null(remaining2);
            Assert.NotNull(remaining3);

            Output.WriteLine($"{provider}: Deleted role2, roles 1 and 3 remain");
        });
    }

    #endregion

    #region Edge Cases

    [Fact]
    public Task CompositeKey_MultipleRecordsWithPartialKeyMatch_AreDistinct()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new TableGateway<OrderItem, long>(context);
            var items = new[]
            {
                CreateOrderItem(500, 1, 1, 1m),
                CreateOrderItem(500, 2, 2, 2m),
                CreateOrderItem(500, 3, 3, 3m)
            };

            foreach (var item in items)
            {
                await helper.CreateAsync(item, context);
            }

            for (var i = 0; i < items.Length; i++)
            {
                var key = new OrderItem { OrderId = 500, ProductId = i + 1 };
                var retrieved = await helper.RetrieveOneAsync(key, context);

                Assert.NotNull(retrieved);
                Assert.Equal(i + 1, retrieved.Quantity);
                Assert.Equal(i + 1, retrieved.UnitPrice);
            }

            Output.WriteLine($"{provider}: Partial key matches remain distinct");
        });
    }

    [Fact]
    public Task CompositeKey_UpdateNonKeyFields_DoesNotAffectKey()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new TableGateway<OrderItem, long>(context);
            var item = CreateOrderItem(600, 601, 10, 100m);
            await helper.CreateAsync(item, context);

            item.Quantity = 20;
            item.UnitPrice = 200m;
            await helper.UpdateAsync(item, context);

            var key = new OrderItem { OrderId = 600, ProductId = 601 };
            var retrieved = await helper.RetrieveOneAsync(key, context);

            Assert.NotNull(retrieved);
            Assert.Equal(600, retrieved.OrderId);
            Assert.Equal(601, retrieved.ProductId);
            Assert.Equal(20, retrieved.Quantity);
            Assert.Equal(200m, retrieved.UnitPrice);

            Output.WriteLine($"{provider}: Key unchanged after non-key update");
        });
    }

    #endregion

    private static OrderItem CreateOrderItem(int orderId, int productId, int quantity, decimal unitPrice)
    {
        return new OrderItem
        {
            Id = Interlocked.Increment(ref _nextOrderItemId),
            OrderId = orderId,
            ProductId = productId,
            Quantity = quantity,
            UnitPrice = unitPrice
        };
    }

    private static UserRole CreateUserRole(int tenantId, int userId, int roleId, string grantedBy,
        DateTime? grantedAt = null)
    {
        return new UserRole
        {
            Id = Interlocked.Increment(ref _nextUserRoleId),
            TenantId = tenantId,
            UserId = userId,
            RoleId = roleId,
            GrantedAt = grantedAt ?? DateTime.UtcNow,
            GrantedBy = grantedBy
        };
    }

    private static async Task RecreateTableAsync(IDatabaseContext context, string tableName, string createSql)
    {
        await DropTableIfExistsAsync(context, tableName);
        await using var container = context.CreateSqlContainer(createSql);
        await container.ExecuteNonQueryAsync();
    }

    private static string BuildOrderItemsTableSql(SupportedDatabase provider, IDatabaseContext context)
    {
        var table = context.WrapObjectName("order_items");
        var idColumn = context.WrapObjectName("id");
        var orderIdColumn = context.WrapObjectName("order_id");
        var productIdColumn = context.WrapObjectName("product_id");
        var quantityColumn = context.WrapObjectName("quantity");
        var unitPriceColumn = context.WrapObjectName("unit_price");

        var idType = GetBigIntType(provider);
        var integerType = GetIntType(provider);
        var decimalType = GetDecimalType(provider);

        return $@"
CREATE TABLE {table} (
    {idColumn} {idType} PRIMARY KEY,
    {orderIdColumn} {integerType} NOT NULL,
    {productIdColumn} {integerType} NOT NULL,
    {quantityColumn} {integerType} NOT NULL,
    {unitPriceColumn} {decimalType} NOT NULL,
    UNIQUE ({orderIdColumn}, {productIdColumn})
)";
    }

    private static string BuildUserRolesTableSql(SupportedDatabase provider, IDatabaseContext context)
    {
        var table = context.WrapObjectName("user_roles");
        var idColumn = context.WrapObjectName("id");
        var tenantColumn = context.WrapObjectName("tenant_id");
        var userColumn = context.WrapObjectName("user_id");
        var roleColumn = context.WrapObjectName("role_id");
        var grantedAtColumn = context.WrapObjectName("granted_at");
        var grantedByColumn = context.WrapObjectName("granted_by");

        var idType = GetBigIntType(provider);
        var integerType = GetIntType(provider);
        var dateTimeType = GetDateTimeType(provider);
        var stringType = GetStringType(provider);

        return $@"
CREATE TABLE {table} (
    {idColumn} {idType} PRIMARY KEY,
    {tenantColumn} {integerType} NOT NULL,
    {userColumn} {integerType} NOT NULL,
    {roleColumn} {integerType} NOT NULL,
    {grantedAtColumn} {dateTimeType} NOT NULL,
    {grantedByColumn} {stringType},
    UNIQUE ({tenantColumn}, {userColumn}, {roleColumn})
)";
    }

    private static string GetIntType(SupportedDatabase provider)
    {
        return provider switch
        {
            SupportedDatabase.Sqlite => "INTEGER",
            SupportedDatabase.Firebird => "INTEGER",
            _ => "INT"
        };
    }

    private static string GetDecimalType(SupportedDatabase provider)
    {
        return provider switch
        {
            SupportedDatabase.Sqlite => "NUMERIC(18,2)",
            _ => "DECIMAL(18,2)"
        };
    }

    private static string GetBigIntType(SupportedDatabase provider)
    {
        return provider switch
        {
            SupportedDatabase.Sqlite => "INTEGER",
            SupportedDatabase.Oracle => "NUMBER(19)",
            _ => "BIGINT"
        };
    }

    private static string GetStringType(SupportedDatabase provider)
    {
        return provider switch
        {
            SupportedDatabase.Sqlite => "TEXT",
            SupportedDatabase.SqlServer => "NVARCHAR(255)",
            SupportedDatabase.Oracle => "VARCHAR2(255)",
            SupportedDatabase.Firebird => "VARCHAR(255)",
            _ => "VARCHAR(255)"
        };
    }

    private static string GetDateTimeType(SupportedDatabase provider)
    {
        return provider switch
        {
            SupportedDatabase.Sqlite => "TEXT",
            SupportedDatabase.SqlServer => "DATETIME2",
            SupportedDatabase.MySql => "DATETIME",
            SupportedDatabase.MariaDb => "DATETIME",
            _ => "TIMESTAMP"
        };
    }

    private static async Task<int> DeleteOrderItemAsync(IDatabaseContext context, int orderId, int productId)
    {
        await using var container = context.CreateSqlContainer();
        var table = context.WrapObjectName("order_items");
        container.Query.Append($"DELETE FROM {table} WHERE ");
        container.Query.Append($"{context.WrapObjectName("order_id")} = ");
        container.Query.Append(container.MakeParameterName("orderId"));
        container.Query.Append(" AND ");
        container.Query.Append($"{context.WrapObjectName("product_id")} = ");
        container.Query.Append(container.MakeParameterName("productId"));
        container.AddParameterWithValue("orderId", DbType.Int32, orderId);
        container.AddParameterWithValue("productId", DbType.Int32, productId);
        return await container.ExecuteNonQueryAsync();
    }

    private static async Task<int> DeleteUserRoleAsync(IDatabaseContext context, int tenantId, int userId, int roleId)
    {
        await using var container = context.CreateSqlContainer();
        var table = context.WrapObjectName("user_roles");
        container.Query.Append($"DELETE FROM {table} WHERE ");
        container.Query.Append($"{context.WrapObjectName("tenant_id")} = ");
        container.Query.Append(container.MakeParameterName("tenantId"));
        container.Query.Append(" AND ");
        container.Query.Append($"{context.WrapObjectName("user_id")} = ");
        container.Query.Append(container.MakeParameterName("userId"));
        container.Query.Append(" AND ");
        container.Query.Append($"{context.WrapObjectName("role_id")} = ");
        container.Query.Append(container.MakeParameterName("roleId"));
        container.AddParameterWithValue("tenantId", DbType.Int32, tenantId);
        container.AddParameterWithValue("userId", DbType.Int32, userId);
        container.AddParameterWithValue("roleId", DbType.Int32, roleId);
        return await container.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// Entity with two-column composite primary key (order + product) plus a row ID.
/// </summary>
[Table("order_items")]
public class OrderItem
{
    [Id] [Column("id", DbType.Int64)] public long Id { get; set; }

    [PrimaryKey(1)]
    [Column("order_id", DbType.Int32)]
    public int OrderId { get; set; }

    [PrimaryKey(2)]
    [Column("product_id", DbType.Int32)]
    public int ProductId { get; set; }

    [Column("quantity", DbType.Int32)] public int Quantity { get; set; }

    [Column("unit_price", DbType.Decimal)] public decimal UnitPrice { get; set; }
}

/// <summary>
/// Entity with three-column composite primary key (tenant + user + role) plus a row ID.
/// </summary>
[Table("user_roles")]
public class UserRole
{
    [Id] [Column("id", DbType.Int64)] public long Id { get; set; }

    [PrimaryKey(1)]
    [Column("tenant_id", DbType.Int32)]
    public int TenantId { get; set; }

    [PrimaryKey(2)]
    [Column("user_id", DbType.Int32)]
    public int UserId { get; set; }

    [PrimaryKey(3)]
    [Column("role_id", DbType.Int32)]
    public int RoleId { get; set; }

    [Column("granted_at", DbType.DateTime)]
    public DateTime GrantedAt { get; set; }

    [Column("granted_by", DbType.String)] public string? GrantedBy { get; set; }
}