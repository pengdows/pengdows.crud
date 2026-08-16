# Primary Keys vs. Pseudokeys Reference

Understanding the architectural distinction between surrogate **Row IDs (Pseudokeys)** and domain **Primary Keys (Business Keys)** in `pengdows.crud`.

---

## The Core Distinction

| Dimension | Pseudo Key / Row ID (`[Id]`) | Primary Key / Business Key (`[PrimaryKey]`) |
|---|---|---|
| **Attribute** | `[Id]` or `[Id(false)]` | `[PrimaryKey(1)]`, `[PrimaryKey(2)]`, ... |
| **Cardinality** | Exactly ONE property per entity | One or more properties (composite keys) |
| **Purpose** | Surrogate technical identifier for CRUD, foreign keys, row identity | Domain reason why the entity exists; natural business key |
| **Storage** | Typically autoincrement integer, identity, or UUIDv7 | Business fields (e.g. `tenant_id`, `order_number`, `email`) |
| **Gateway** | `TableGateway<TEntity, TRowID>` | `PrimaryKeyTableGateway<TEntity>` (or lookup in `TableGateway`) |

> [!WARNING]
> `[Id]` and `[PrimaryKey]` are **MUTUALLY EXCLUSIVE** on any given property. Never place both on the same property.

---

## Gateway Selection Rules

### 1. Entity has `[Id]` $\to$ Use `TableGateway<TEntity, TRowID>`
Supports row-id convenience methods:
```csharp
ValueTask<TEntity?> order = await gateway.RetrieveOneAsync(orderId);
ValueTask<int> affected = await gateway.DeleteAsync(orderId);
```

### 2. Entity has ONLY `[PrimaryKey]` $\to$ Use `PrimaryKeyTableGateway<TEntity>`
For tables without surrogate IDs (junction tables, domain natural keys):
```csharp
[Table("order_items")]
public class OrderItem
{
    [PrimaryKey(1)]
    [Column("order_id", DbType.Int64)]
    public long OrderId { get; set; }

    [PrimaryKey(2)]
    [Column("product_id", DbType.Int64)]
    public long ProductId { get; set; }

    [Column("quantity", DbType.Int32)]
    public int Quantity { get; set; }
}

public class OrderItemGateway : PrimaryKeyTableGateway<OrderItem>
{
    public OrderItemGateway(IDatabaseContext context) : base(context) { }
}
```

---

## Upsert Conflict Key Resolution

When calling `UpsertAsync` or `BuildUpsert`:
1. **First Choice**: `[PrimaryKey]` columns (if any are defined on the entity).
2. **Fallback**: `[Id]` column **ONLY if writable** (`[Id]` or `[Id(true)]`).
3. **Error**: Throws `SqlGenerationException` if no `[PrimaryKey]` is defined AND `[Id]` is non-writable (`[Id(false)]`).

---

## Coexistence on the Same Entity

Entities may have BOTH attributes on different properties:
```csharp
[Table("orders")]
public class Order
{
    [Id(false)]          // Pseudo key for fast row-id operations & foreign keys
    [Column("id", DbType.Int64)]
    public long Id { get; set; }

    [PrimaryKey(1)]      // Business key for natural domain lookup & upsert conflict target
    [Column("tenant_id", DbType.String, 50)]
    public string TenantId { get; set; } = string.Empty;

    [PrimaryKey(2)]
    [Column("order_number", DbType.String, 50)]
    public string OrderNumber { get; set; } = string.Empty;
}
```

- `gateway.RetrieveOneAsync(id)` queries by `[Id]`.
- `gateway.RetrieveOneAsync(new Order { TenantId = "t1", OrderNumber = "ORD-1" })` queries by `[PrimaryKey]` columns.

---

## Physical Index Order Preservation

The integer parameter in `[PrimaryKey(1)]`, `[PrimaryKey(2)]` specifies the **exact column order in the underlying physical B-tree index or UNIQUE constraint**:
- **PostgreSQL `ON CONFLICT`**: Requires column names in the conflict target to match the exact index definition order.
- **SQL Server / Oracle `MERGE`**: Matches index column precedence for optimal $O(\log N)$ seeks without table scans.
- `TableInfo.PrimaryKeys` guarantees stable, deterministic ordering across all SQL templates.

