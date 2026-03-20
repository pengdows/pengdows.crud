# Batch Operations Compatibility

This matrix reflects the current 2.0 implementation, not proposed future bulk-loading features.

## Current Behavior By Operation

| Operation | Uses dialect capability | Fallback when unsupported |
|---|---|---|
| Batch create | `SupportsBatchInsert` | one `BuildCreate(...)` container per entity |
| Batch update | `SupportsBatchUpdate` | one update container per entity |
| Batch upsert | `SupportsInsertOnConflict` / `SupportsOnDuplicateKey` | one `BuildUpsert(...)` container per entity |
| Batch delete by IDs | always available on `TableGateway<TEntity, TRowID>` | n/a |
| Batch delete by entities | always available | one delete container per entity |

## Upsert Shape Used Today

| Product family | Batch upsert shape |
|---|---|
| PostgreSQL-compatible products with `SupportsInsertOnConflict` | multi-row `INSERT ... ON CONFLICT ...` |
| MySQL-compatible products with `SupportsOnDuplicateKey` | multi-row `INSERT ... ON DUPLICATE KEY UPDATE ...` |
| Products without either flag | per-entity `BuildUpsert(...)` fallback |

That means SQL Server, Oracle, Firebird, and similar engines still support batch upsert through the batch API, but the batch is executed as a sequence of per-entity upsert containers rather than a single multi-row upsert statement.

## Non-Goals Of The Current Implementation

The repository does not currently implement:

- provider-native bulk copy pipelines
- concurrent chunk execution
- partial-success reporting
- user-selectable batch strategies
- progress callbacks
