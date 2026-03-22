# Batch Operations Architecture

This document describes the batch implementation that exists in the 2.0 codebase today.

## Entry Points

The implementation is split across:

- `pengdows.crud/TableGateway.Batch.cs`
- `pengdows.crud/TableGateway.Core.cs`
- `pengdows.crud/PrimaryKeyTableGateway.Delete.cs`
- `pengdows.crud/PrimaryKeyTableGateway.Update.cs`
- `pengdows.crud/PrimaryKeyTableGateway.Upsert.cs`

There is no separate batch coordinator type. The gateway itself owns validation, chunking, SQL generation, and execution.

## Execution Flow

1. Validate input and short-circuit empty collections.
2. Resolve the effective `IDatabaseContext`.
3. Detect the dialect and capability flags from the context.
4. Prepare entities for the batch:
   - assign writable IDs when needed
   - resolve audit values once for the batch
   - populate audit fields
   - initialize version values for create paths
5. Split the input into chunks based on parameter limits and dialect row caps.
6. Build one `ISqlContainer` per chunk.
7. Execute containers sequentially and sum the affected-row count.

## Chunking Model

Chunking is based on:

- columns/parameters consumed per row
- `IDatabaseContext.MaxParameterLimit`
- dialect-specific `MaxRowsPerBatch`

This keeps generated statements within provider limits without exposing a user-configurable batching API.

## Fallback Rules

- If `SupportsBatchInsert` is false, batch create returns one `BuildCreate(...)` container per entity.
- If `SupportsBatchUpdate` is false, batch update returns one update container per entity.
- If the product does not advertise `SupportsInsertOnConflict` or `SupportsOnDuplicateKey`, batch upsert returns one `BuildUpsert(...)` container per entity.
- Entity-keyed batch delete is intentionally one container per entity.

## Important Constraints

- Batch APIs are SQL-container based, not data-loader based.
- Batch execution is sequential over the generated containers.
- There is no partial-success result object.
- There is no retry, compensation, or per-row error reporting layer.
- There is no diff-based `BuildUpdateAsync(original, updated)` batch path in the public API.

## Design Intent

The current implementation optimizes the public API that already exists:

- explicit SQL
- inspectable containers
- deterministic fallback behavior
- provider-aware generation without inventing extra orchestration types

If richer batch orchestration is added later, it should be introduced as a new API rather than backfilled into the existing documentation.
