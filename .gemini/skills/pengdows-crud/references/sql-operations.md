# SQL Operations & Three-Tier API

`pengdows.crud` provides a clear, three-tier API for SQL building and execution.

## Three-Tier API

| Tier | Purpose | Key Methods |
|------|---------|-------------|
| **1. Build** | SQL generation (no I/O). | `BuildCreate`, `BuildBaseRetrieve`, `BuildDelete`, `BuildUpsert`, `BuildUpdateAsync`, `BuildBatchCreate/Update/Upsert/Delete`. |
| **2. Load** | Execute pre-built `ISqlContainer`. | `LoadSingleAsync`, `LoadListAsync`, `LoadStreamAsync`. |
| **3. Convenience** | One-call Build + Execute. | `CreateAsync`, `RetrieveOneAsync`, `UpdateAsync`, `DeleteAsync`, `UpsertAsync`. |

## Custom SQL with `SqlContainer`

Inherit from `TableGateway` to add custom methods.

```csharp
public class CustomerGateway : TableGateway<Customer, long>, ICustomerGateway
{
    public async Task<List<Customer>> SearchByNameAsync(string namePattern)
    {
        var sc = BuildBaseRetrieve("c"); // Tier 1: Build base SELECT

        sc.Query.Append(" WHERE ");
        sc.Query.Append(sc.WrapObjectName("c.name"));
        sc.Query.Append(" LIKE ");
        var param = sc.AddParameterWithValue("pattern", DbType.String, $"%{namePattern}%");
        sc.Query.Append(sc.MakeParameterName(param));

        return await LoadListAsync(sc); // Tier 2: Execute
    }
}
```

## Parameterization & Quoting

- **`WrapObjectName(name)`**: Always use for column/table names and aliases (e.g., `[column]` or `"column"`).
- **`MakeParameterName(param)`**: Converts a parameter name to the dialect's marker (e.g., `@p0` or `:p0`).
- **`AddParameterWithValue(name, type, value)`**: Safely adds a parameter to the container.

## ValueTasks

Execution methods return `ValueTask` for reduced allocation overhead on hot paths.

## Streaming

Process large result sets sequentially without loading all into memory.

```csharp
await foreach (var customer in gateway.LoadStreamAsync(sc))
{
    // Process one item at a time
}
```
