# Gateway Count Helpers

Every gateway — `TableGateway<TEntity, TRowID>` and `PrimaryKeyTableGateway<TEntity>` alike —
exposes four `COUNT(*)` convenience methods. They live in `BaseTableGateway<TEntity>`
(`pengdows.crud/BaseTableGateway.Count.cs`), the shared base both gateway types derive from, so the
signatures and behavior are identical regardless of which gateway you're using. Declared on
`ITableGateway<TEntity, TRowID>` and `IPrimaryKeyTableGateway<TEntity>` respectively.

These are lightweight, string-column-oriented shortcuts for the handful of `COUNT` queries every
CRUD-style application ends up writing by hand — they are **not** a general query or filter
expression system. For anything beyond what's listed below, use `BuildBaseRetrieve`/custom SQL and
`ExecuteScalarRequiredAsync<long>` directly.

## Signatures

```csharp
ValueTask<long> CountAllAsync(
    IDatabaseContext? context = null);

ValueTask<long> CountWhereAsync(
    string column,
    string value,
    bool isLike = false,
    IDatabaseContext? context = null);

ValueTask<long> CountWhereNullAsync(
    string column,
    IDatabaseContext? context = null);

ValueTask<long> CountWhereEqualsAsync(
    string column,
    string value,
    string? andWhereNull = null,
    string? andWhereNotNull = null,
    IDatabaseContext? context = null);
```

All four accept an optional `IDatabaseContext` override (same pattern as the rest of the gateway's
Tier 3 convenience methods) — pass a tenant context or a transaction's context to run the count
against something other than the gateway's own default context. All four return `0` (never `null`)
against an empty result, and all four go through `ExecuteScalarOrNullAsync<long?>() ?? 0` internally.

## What each one produces

### `CountAllAsync`

```sql
SELECT COUNT(*) FROM "table"
```

Unconditional row count. No parameters.

### `CountWhereAsync(column, value, isLike)`

```sql
-- isLike: false (default)
SELECT COUNT(*) FROM "table" WHERE "column" = @v

-- isLike: true
SELECT COUNT(*) FROM "table" WHERE "column" LIKE @v
```

Single-column equality or `LIKE` count. `isLike` just swaps the operator — there is no separate
"LIKE count" method, it's a `bool` parameter on the same method. When `isLike: true`, the wildcard
characters (`%`, `_`) are the caller's responsibility inside `value`; the method does no escaping or
wildcard injection of its own.

```csharp
var processing = await gateway.CountWhereAsync("state", "Processing");
var recurring  = await gateway.CountWhereAsync("queue", "recurring-jobs:%", isLike: true);
```

### `CountWhereNullAsync(column)`

```sql
SELECT COUNT(*) FROM "table" WHERE "column" IS NULL
```

No parameters — `IS NULL` never takes a bound value. There is no `CountWhereNotNullAsync`
counterpart; use `CountWhereEqualsAsync` with `andWhereNotNull` (see below), or a custom
`BuildBaseRetrieve` query, for a standalone not-null count.

### `CountWhereEqualsAsync(column, value, andWhereNull?, andWhereNotNull?)`

The compound helper — one equality predicate plus an optional null-state check on a **second**
column:

```sql
-- andWhereNull set
SELECT COUNT(*) FROM "table" WHERE "column" = @v AND "andWhereNull" IS NULL

-- andWhereNotNull set
SELECT COUNT(*) FROM "table" WHERE "column" = @v AND "andWhereNotNull" IS NOT NULL

-- neither set
SELECT COUNT(*) FROM "table" WHERE "column" = @v
```

```csharp
// Rows in the "default" queue that haven't been fetched yet
var pending = await gateway.CountWhereEqualsAsync("queue", "default", andWhereNull: "fetched_at");

// Rows in the "default" queue that have already been fetched
var fetched = await gateway.CountWhereEqualsAsync("queue", "default", andWhereNotNull: "fetched_at");
```

**If both `andWhereNull` and `andWhereNotNull` are non-null, `andWhereNull` silently wins** —
the implementation checks `andWhereNull != null` first and only falls through to
`andWhereNotNull` in an `else if`. There is no argument validation and no exception thrown for
supplying both; `andWhereNotNull` is simply ignored in that case. Callers should treat the two
parameters as mutually exclusive by convention, since the XML doc comment on the interface says
"exactly one... may be set" but nothing enforces it at runtime.

## Identifier quoting and parameterization

- Every table and column name passed through `column`, `andWhereNull`, or `andWhereNotNull` goes
  through `ISqlContainer.WrapObjectName(...)` — the same ANSI double-quote identifier quoting used
  everywhere else in the library (see CLAUDE.md's `WrapObjectName` section). There is no raw string
  concatenation of identifiers into SQL text.
- Every `value` is bound as a `DbType.String` parameter via `AddParameterWithValue` /
  `MakeParameterName` — never interpolated into the query text. This means `value` is always
  compared/matched as a string; there is no overload that binds a typed (`int`, `DateTime`, `Guid`,
  etc.) value directly. If the target column is numeric or temporal, whether string comparison
  behaves as expected depends on the database's own implicit-conversion rules for that column
  type — test against the actual dialect if you rely on this for anything but string/text columns.

## Known limitations

- **String-valued predicates only.** `CountWhereAsync` and `CountWhereEqualsAsync` both type their
  `value` parameter as `string`; there's no generic/typed overload.
- **Exactly one compound shape.** `CountWhereEqualsAsync` supports one equality column plus at most
  one null-state check on a second column — it does not support multiple equality predicates, `OR`
  logic, ranges, or arbitrary combinations of null checks across more than one column.
- **No general expression/filter system.** These four methods cover `COUNT(*)`, single-column
  equality/`LIKE`, `IS NULL`, and the one compound equality+null-state shape above — nothing else.
  Anything more complex (multi-column filters, joins, aggregates other than `COUNT(*)`, grouping)
  requires building the SQL yourself via `BuildBaseRetrieve`/`context.CreateSqlContainer()` and
  executing it with `ExecuteScalarRequiredAsync<long>()` or `ExecuteScalarOrNullAsync<long?>()`.
- **`IDatabaseContext` override is supported** on all four methods, consistent with other Tier 3
  convenience methods on the gateway — pass a transaction's or tenant's context to scope the count
  accordingly.
