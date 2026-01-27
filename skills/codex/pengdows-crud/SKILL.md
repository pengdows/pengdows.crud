---
name: pengdows-crud
description: Implement and review data access code using pengdows.crud (.NET SQL-first DAL). Use when creating or modifying TableGateway classes, SqlContainer queries, entity mapping attributes ([Table], [Column], [Id], [PrimaryKey], audit/version), DatabaseContext connection modes (DbMode), transactions, parameter naming, type coercion, or FakeDb unit tests.
---

# pengdows.crud skill

## Non-negotiables

- **SQL-first**: generate/compose SQL explicitly; no LINQ, no implicit tracking.
- **Singleton lifetimes**: treat `DatabaseContext` and `TableGateway` as **singleton-safe** (stateless gateways; context owns pooling/metrics). See `references/connections.md`.
- **Transactions are explicit**: use `Context.BeginTransaction()` / `ITransactionContext`. Avoid ambient transactions (`TransactionScope`) because it fights open-late/close-early connection behavior. See `references/sql-container.md` and `references/connections.md`.
- **Always quote identifiers via the dialect**: use `WrapObjectName()` for tables/columns/aliases in custom SQL. See `references/sql-container.md`.
- **Parameters are bound**: never string-interpolate values into SQL. Use `AddParameterWithValue` + `MakeParameterName`. See `references/api-reference.md`.

## Quick navigation

- **API surface / method signatures**: `references/api-reference.md`
- **DbMode + connection lifecycle**: `references/connections.md`
- **Entity attributes ([Table]/[Column]/keys/audit/version/json)**: `references/entity-mapping.md`
- **[Id] vs [PrimaryKey] philosophy + engine impacts**: `references/primary-keys.md`
- **SqlContainer details + stored-proc wrapping**: `references/sql-container.md`
- **FakeDb unit testing patterns**: `references/testing.md`
- **Type coercion + enum parse behavior**: `references/type-coercion.md`

## Standard workflows

### 1) Add a new entity

1. Add `[Table("schema.table")]`.
2. Add exactly one `[Id]` property and map it with `[Column(...)]`.
3. Add `[PrimaryKey(n)]` columns only for **business identity** (optional, can be composite).
4. Add `[Version]` if you need optimistic concurrency.
5. Add audit attributes only if you have an `IAuditValueResolver` plan.

Use the attribute rules and examples in `references/entity-mapping.md`.

### 2) Add or extend a gateway

- Prefer **inheritance**: `public sealed class FooGateway : TableGateway<Foo,long>`.
- Put custom query methods **inside** the gateway.
- Build SQL with `BuildBaseRetrieve(alias)` and append clauses manually.

See examples + method list in `references/api-reference.md`.

### 3) Write custom SQL

Pattern (inside a gateway):

- Start: `var sc = BuildBaseRetrieve("a");`
- Append SQL fragments to `sc.Query`.
- Quote identifiers: `sc.WrapObjectName("a.col")`.
- Bind parameters: `var p = sc.AddParameterWithValue("x", DbType.Int32, value); sc.Query.Append(sc.MakeParameterName(p));`
- Load: `LoadListAsync(sc)` / `LoadSingleAsync(sc)`.

Reference: `references/sql-container.md`.

### 4) Execute commands

- Use `ExecuteNonQueryAsync`, `ExecuteScalarAsync<T>`, `ExecuteReaderAsync` on `ISqlContainer`.
- Keep readers short-lived; dispose promptly.

Reference: `references/api-reference.md` and `references/sql-container.md`.

### 5) Transactions

- Create transaction **inside** the operation. Do not store transaction contexts in fields.
- Commit only after the last write succeeds.
- Use savepoints for partial rollback where supported.

Reference: `references/api-reference.md` and `references/connections.md`.

### 6) Unit tests with FakeDb

- Use `fakeDbFactory(SupportedDatabase.X)` to emulate provider behavior.
- Test SQL generation and error paths deterministically.
- Use integration tests for real SQL correctness / constraints.

Reference: `references/testing.md`.

## What to do when requirements conflict

- If someone asks for "ORM-style" behavior (tracking, lazy loading, auto-joins): **don’t retrofit** it. Use explicit SQL and add focused helper methods.
- If someone wants `TransactionScope`: push back; explain the connection semantics and use explicit transactions.
- If a query needs portable identifier quoting or stored-proc invocation: route through `SqlContainer` helpers; don’t hand-roll dialect branches.

