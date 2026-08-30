# Automatic Audit-Field Lifecycle

Gateways automatically populate `[CreatedBy]`, `[CreatedOn]`, `[LastUpdatedBy]`, and
`[LastUpdatedOn]` columns through an `IAuditValueResolver` you supply — no manual timestamp or
user-ID assignment in application code. This doc is the full lifecycle guide; for the attribute
mechanics themselves (property-type validation, which attribute requires a resolver, `[NonInsertable]`/
`[NonUpdateable]` interaction), see [`entity-mapping.md`](./entity-mapping.md)'s audit-attribute
section — this doc covers the *runtime* behavior across create, update, and batch operations.

## The resolver contract

```csharp
public interface IAuditValueResolver
{
    IAuditValues Resolve();
}

public interface IAuditValues
{
    object UserId { get; init; }
    DateTime UtcNow { get; }
    DateTimeOffset? TimestampOffset { get; }
}
```

`Resolve()` is **synchronous only** — there is no async overload. A resolver backed by an
async-only identity source (a remote claims service, say) must block on it internally. If
`Resolve()` throws, the exception propagates unwrapped from `CreateAsync`/`UpdateAsync` — it is
never caught or translated by the gateway.

See [`docs/examples/OidcAuditFieldResolver-example.cs`](./examples/OidcAuditFieldResolver-example.cs)
for a reference implementation extracting a user ID from ASP.NET Core OIDC claims via
`IHttpContextAccessor` (registered as a DI singleton — safe because `IHttpContextAccessor` itself
is `AsyncLocal`-backed, not because the resolver holds no state).

## When a resolver is required

A resolver is only mandatory if the entity has a **user-identity** audit attribute
(`[CreatedBy]`/`[LastUpdatedBy]`). If neither is present — only `[CreatedOn]`/`[LastUpdatedOn]` —
no resolver is needed at all; timestamps use `DateTime.UtcNow` directly. Constructing a gateway
for an entity with `[CreatedBy]`/`[LastUpdatedBy]` but no resolver supplied does **not** fail at
construction — it fails at the first `CreateAsync`/`UpdateAsync` call, with
`InvalidOperationException("AuditValues resolver is required for user-based audit fields.")`
(`BaseTableGateway.Audit.cs`'s `SetAuditFields`/`ResolveAuditValuesForBatch`).

## What happens on CREATE

Both pairs are set, not just the "created" one:

| Field | Behavior |
|---|---|
| `CreatedOn` | Set to the resolved timestamp, unless `AuditCreationPolicy.PreserveExplicitValues` (the default) is active and the entity already carries a non-default value — see below. |
| `CreatedBy` | Set to the resolved, type-coerced `UserId`, unless preserved for the same reason. |
| `LastUpdatedOn` | **Also set**, to the same timestamp. |
| `LastUpdatedBy` | **Also set**, to the same `UserId`. |

Setting `LastUpdatedBy`/`LastUpdatedOn` on CREATE is intentional (see CLAUDE.md's "CRITICAL: Audit
Field Behavior") — it lets a "most recently modified" query work correctly without a separate
check for "has this row ever been updated."

## What happens on UPDATE

Only the `LastUpdated*` pair changes. `CreatedBy`/`CreatedOn` are excluded from every UPDATE's SET
clause by dedicated filtering in the gateway SQL builders (`TableGateway.Sql.cs`/`.Batch.cs`) —
not by the generic `[NonUpdateable]` flag, which is a separate mechanism (see `entity-mapping.md`).

## `AuditCreationPolicy` — the security-relevant control

A settable property on every `ITableGateway`/`IPrimaryKeyTableGateway`, default
`PreserveExplicitValues`:

| Policy | CREATE behavior |
|---|---|
| `PreserveExplicitValues` (default) | If the entity's `CreatedBy`/`CreatedOn` already holds a non-default value (non-empty string, non-zero numeric, non-empty `Guid`, non-default timestamp) *before* `CreateAsync` runs, that value survives untouched instead of being overwritten by the resolver. Verified directly in `BaseTableGateway.Audit.cs`'s `SetAuditFields`: the check is `currentValue == null \|\| currentValue as string == string.Empty \|\| Utils.IsZeroNumeric(currentValue) \|\| (currentValue is Guid guid && guid == Guid.Empty)` for `CreatedBy`, and `IsDefaultTimestamp(currentValue)` for `CreatedOn`. |
| `Authoritative` | Always overwrites `CreatedBy`/`CreatedOn` with resolver-supplied values, ignoring whatever the entity already holds. |

**Security implication:** the default's "preserve a non-default explicit value" behavior means an
application that binds an incoming request DTO directly onto an audited entity — without
explicitly setting `AuditCreationPolicy = AuditCreationPolicy.Authoritative` — lets a caller supply
their own `CreatedBy` value, which is then trusted and persisted as the actual creator. Set
`Authoritative` on any gateway whose `CreateAsync` might receive an entity populated from
untrusted input. `PreserveExplicitValues` exists for the opposite case: imports/migrations that
need to carry an original creation timestamp/author forward.

## Timestamps are always UTC

`ResolveAuditTimestamp` (`BaseTableGateway.Audit.cs`) throws `InvalidOperationException` if
`IAuditValues.TimestampOffset` is non-null but its `Offset` isn't exactly `TimeSpan.Zero` — a
resolver cannot supply a local-time-with-offset value. If `TimestampOffset` is null, `UtcNow` is
used directly. The resolved UTC instant is then coerced to whatever the target property's type
actually is (`DateTime` or `DateTimeOffset`) via `CoerceTimestamp`.

## User-ID coercion

`UserId` (declared as `object` on `IAuditValues`, so a resolver can return a `string`, `Guid`, or
numeric identity) is coerced to the audit property's actual .NET type before assignment. A
`string` UserId being assigned to a `Guid`-typed `CreatedBy` property is parsed via `Guid.TryParse`
(throwing `InvalidOperationException` with the attempted value if it doesn't parse); every other
mismatch goes through `TypeCoercionHelper.ConvertWithCache` — the same general-purpose coercion
path used elsewhere in the library.

## Batch operations resolve once, not once per row

`ResolveAuditValuesForBatch()` calls `IAuditValueResolver.Resolve()` **exactly once** for an
entire `BatchCreateAsync`/`BatchUpdateAsync`/`BatchUpsertAsync` call, then applies the same
resolved `IAuditValues` instance to every entity in the batch via the
`SetAuditFields(obj, updateOnly, auditValues)` overload — confirmed directly in
`TableGateway.Batch.cs`/`PrimaryKeyTableGateway.Upsert.cs` (`var auditValues = _auditValueResolver
!= null && _hasAuditColumns ? ... .Resolve() : null`, computed once before the entity loop). This
means every row in one batch call shares the identical timestamp and resolved user identity — by
design, not an inconsistency — since a resolver call typically represents "who is running this
operation right now," which doesn't change mid-batch. See
[`docs/batch-operations.md`](./batch-operations.md) for the surrounding batch API.

## Failed writes restore in-memory audit values

`SetAuditFields` runs during **Build** (`BuildCreate`/`BuildUpdateAsync`), before any SQL executes
— so if the subsequent `Execute` never actually persists (throws, or returns 0 rows affected), the
entity's in-memory audit properties would otherwise claim a write that never happened. Every
`CreateAsync`/`UpdateAsync`/`UpsertAsync` path pairs `SnapshotAuditFields` (captures the pre-Build
values) with `RestoreAuditFields`/`RestoreAuditFieldsIfFailed` around the execute call, so a caller
that catches the failure (or retries) sees the entity's audit fields exactly as they were before
the failed attempt — not a stale "successfully created/updated" stamp. This applies uniformly to
single-entity and batch operations (`entities.Select(SnapshotAuditFields).ToArray()` in the batch
paths).

## Summary table

| Scenario | `CreatedBy`/`CreatedOn` | `LastUpdatedBy`/`LastUpdatedOn` | Resolver calls |
|---|---|---|---|
| `CreateAsync`, no existing value | Set from resolver | Set from resolver (same values) | 1 |
| `CreateAsync`, `PreserveExplicitValues` + entity already has a value | Preserved as-is | Set from resolver | 1 |
| `CreateAsync`, `Authoritative` | Always overwritten | Set from resolver | 1 |
| `UpdateAsync` | Untouched (excluded from SET clause) | Set from resolver | 1 |
| `BatchCreateAsync`/`BatchUpdateAsync` (N entities) | Per-entity, same rules as above | Per-entity, same rules as above | **1 total**, reused for all N |
| Write fails (exception or 0 rows affected) | Restored to pre-Build value | Restored to pre-Build value | (already spent, no re-resolve on retry) |
