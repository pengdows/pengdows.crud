# Migration: 2.x → 3.0

This document lists every public-surface removal and addition in the `3.0` branch relative to
`main`/2.x that a consumer's build could be affected by, found and verified during a full-branch
code review against `REVIEW_POLICY.md`. Each entry is checked directly against the current source
(not inferred from a commit message alone).

## Removed

### `IDatabaseContext.DataSource`

The `DbDataSource? DataSource { get; }` property is gone. It exposed the raw ADO.NET
`DbDataSource` (e.g., `NpgsqlDataSource`), which let a caller bypass `DatabaseContext`'s connection
governance (pooling, mode coercion, metrics) entirely.

**Migration:** use `IDatabaseContext.CreateSqlContainer(...)`/`GetConnection(...)`-based APIs
instead of reaching for a raw provider `DbDataSource`.

### `IDataSourceInformation.StandardCompliance`

The `SqlStandardLevel StandardCompliance { get; }` property is gone.

**Migration:** none of the built-in `ISqlDialect` capability flags (`Supports*`) depended on this
value; if you read it directly, switch to the specific `Supports*` capability flag you actually
care about.

### `IEphemeralSecureString` / `EphemeralSecureString`

Both types are deleted outright. Confirmed zero production call sites before removal — this was
dead public surface, not a behavior change to any code path that used it.

**Migration:** none needed for typical consumers. If you implemented or referenced this interface
directly, there is no replacement; secret handling should go through your own
`IAuditValueResolver`/configuration secret store as documented in "Security & Configuration Tips"
in the root `CLAUDE.md`.

### `pengdows.crud/types/attributes/WeirdTypeAttributes.cs` — 12 attribute types + 1 enum

All of the following public types are deleted, confirmed zero remaining references anywhere in the
tree:

`DbEnumAttribute`, `JsonContractAttribute`, `ConcurrencyTokenAttribute`, `RangeTypeAttribute`,
`ComputedAttribute`, `CaseInsensitiveAttribute`, `AsStringAttribute`,
`MaxLengthForInlineAttribute`, `AllowZeroDateAttribute`, `CaseFoldOnReadAttribute`,
`SpatialTypeAttribute`, `CurrencyAttribute`, and the `EnumStorage` enum.

**Migration:** none of these were wired into any mapping/execution path — they were unused
scaffolding. If you applied one of these attributes to an entity property expecting it to do
something, it never did; remove the attribute (it will now fail to compile) with no behavior
change.

## Visibility reduced (public → internal)

These two interfaces are no longer part of the public API surface. Both were confirmed to have no
external extension point depending on their public visibility (`interface-api-check` baseline
updated accordingly).

- `pengdows.crud.threading.ILockerAsync`
- `pengdows.crud.connection.IConnectionLocalState`

**Migration:** if your code implemented or referenced either type directly (not expected for a
typical consumer — both are low-level connection/locking primitives), that code will no longer
compile. There is no public replacement; these are internal implementation details of connection
and command locking.

## Added

### `ITableGateway<TEntity, TRowID>.AuditCreationPolicy` / `IPrimaryKeyTableGateway<TEntity>.AuditCreationPolicy`

A new `AuditCreationPolicy AuditCreationPolicy { get; init; }` member, defaulting to
`AuditCreationPolicy.PreserveExplicitValues`. See the root `CLAUDE.md`'s "CRITICAL: Audit Field
Behavior" section for the full security implication: an application binding an untrusted request
DTO directly onto an audited entity should set this to `AuditCreationPolicy.Authoritative`
explicitly.

**Migration:** any class implementing `ITableGateway`/`IPrimaryKeyTableGateway` directly (rather
than inheriting `TableGateway<,>`/`PrimaryKeyTableGateway<>`) must add this member.

### `ITransactionContext.ReleaseSavepointAsync(string)`

A new abstract member alongside the existing `SavepointAsync`/`RollbackToSavepointAsync`. Throws
`NotSupportedException` when the dialect's `SavepointCapabilities` lacks the `Release` flag, rather
than silently no-op-ing.

**Migration:** any class implementing `ITransactionContext` directly must add this member.

## Behavior change: CockroachDB native `IsolationLevel` is now enforced, not silently upgraded

`TransactionContext`'s constructor used to silently rewrite any native `IsolationLevel` passed for
a CockroachDB context to `IsolationLevel.Serializable` before validating it. That silent
substitution was removed as part of replacing `IsolationResolver`'s hardcoded per-database
switches with dialect-owned data (`ISqlDialect.GetSupportedIsolationLevels`). Since
`CockroachDbDialect.GetSupportedIsolationLevels()` returns only `{Serializable}`, a call like:

```csharp
context.BeginTransaction(IsolationLevel.ReadCommitted); // CockroachDB context
```

now throws `InvalidOperationException` instead of silently running as `Serializable`. This is a
deliberate correctness fix — the previous behavior masked a caller's request for an isolation
level CockroachDB does not support — but it is an observable, breaking behavior change for a
caller relying on the old silent substitution.

**Migration:** pass `IsolationLevel.Serializable` explicitly for CockroachDB, or use the portable
`context.BeginTransaction(IsolationProfile.StrictConsistency)` overload, which already resolves to
`Serializable` for CockroachDB and is unaffected by this change.

Locked down by `DatabaseContextIsolationTests.BeginTransaction_NativeIsolationLevel_CockroachDb_UnsupportedLevel_Throws`
and `...Serializable_Succeeds`.
