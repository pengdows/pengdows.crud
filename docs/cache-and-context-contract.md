# Cache Inventory and Context-Derived Generation Contract

Two related contracts for anyone running a long-lived, multi-tenant process: what internal state
gets cached and how it's bounded, and which parts of a gateway's behavior come from the
*constructor* context versus the *per-call* operation context.

## Cache inventory

All caching in this library uses `pengdows.crud/internal/BoundedCache.cs` — a thread-safe LRU
cache (`ConcurrentDictionary` + a monotonic access clock; eviction is a linear scan for the lowest
`LastAccess` timestamp, cheap at the sizes used here, 32–512 entries) — except where noted
otherwise below.

| Cache | Scope | Key | Bound | Notes |
|---|---|---|---|---|
| `DataReaderMapper._planCache` | Static, process-wide | `PlanCacheKey(Type, SchemaHash, ColumnsOnly, EnumMode)` — `SchemaHash` is a 64-bit arithmetic hash of the column names+types | 128 (`MaxPlanCacheSize`) | `DataReaderMapper` is public and externally reachable (`DataReaderMapper.Instance`, see `docs/data-reader-mapper.md`) — this cache backs its own hydration path, separate from gateway hydration's `_readerPlans` below. Not tenant-cardinality-related; keyed by entity type + result shape. |
| `DataReaderMapper._setterCache` | Static, process-wide | `SetterCacheKey` (property-level) | 512 (`MaxSetterCacheSize`) | Same as above. |
| `DataReaderMapper._propertyLookupCache` | Static, process-wide | `PropertyLookupCacheKey` | 64 (`MaxPropertyLookupCacheSize`) | Same as above. |
| `BaseTableGateway._readerPlans` | Per-gateway-instance | `long` hash of the recordset shape (column count, names case-insensitively, and field types, via `System.HashCode`) | Configurable via `ReaderPlanCacheSize`, default 32 (`DefaultReaderPlanCapacity`) | **Not tenant-cardinality-bound and doesn't need to be** — captured once at gateway construction (CORE-019: intentional, not a gap), keyed by result-set shape, which is a property of the query/entity, not of which tenant executed it. One singleton gateway serving 10,000 tenants still has at most a few dozen distinct shapes. |
| `BaseTableGateway._columnListCache` | Per-gateway-instance | `string` | 100 (`MaxCacheSize`) | Same tenant-independence reasoning as `_readerPlans`. |
| `BaseTableGateway._queryCache` | Per-gateway-instance, two-level | Outer: `ConditionalWeakTable<ISqlDialect, BoundedCache<string,string>>` keyed by **dialect instance**; inner: `BoundedCache` capped at 100 per dialect instance | Outer table holds one entry per live dialect instance, reclaimed by the GC once that dialect (and the `DatabaseContext` that owns it) is no longer referenced | **This is the multi-tenant cache-design point**: keying by dialect instance (not by the coarse `SupportedDatabase` enum) is deliberate — two tenants on *different* versions of the same engine (e.g. MySQL 8.0.19 vs. 8.0.33) can never share SQL text that was generated for the other's version. The cost is redundancy: each tenant context has its own dialect instance, so N live tenant contexts on the same engine+version each build and hold their own inner cache (at most 100 short strings each), rather than sharing one. The `ConditionalWeakTable` keeps this from becoming a leak — entries for disposed/unreferenced tenant contexts are collected, not retained for the process lifetime. |
| `BaseTableGateway._whereParameterNames` | Per-gateway-instance, two-level | Same dialect-instance-keyed `ConditionalWeakTable` structure as `_queryCache` | Same reasoning | Same as `_queryCache`. |
| `BaseTableGateway._wrappedTableNameCache` | Per-gateway-instance | `ConditionalWeakTable<ISqlDialect, string>` keyed by **dialect instance**, same as `_queryCache`/`_whereParameterNames` | One entry per live dialect instance; reclaimed with the dialect | Same tenant-scale reasoning as `_queryCache`: one small string per live tenant context, released when that context's dialect is no longer referenced. |
| `TypeMapRegistry._typeMap` | **Per-`DatabaseContext`-instance by default** (each `DatabaseContext(...)` constructor creates its own `new TypeMapRegistry()` unless one is explicitly injected via one of the constructor overloads accepting `ITypeMapRegistry typeMapRegistry`) | `System.Type` | Unbounded `ConcurrentDictionary`, but self-limiting — bounded by the number of distinct compiled entity `Type`s the application ever registers, which cannot grow at runtime | Confirmed non-issue for growth. **Redundancy tradeoff, not a leak:** because each `DatabaseContext` gets its own registry by default, N tenant contexts recompute and cache the same entity metadata N times (small, one-time cost per context, not per-request) — share one `ITypeMapRegistry` instance across tenant contexts via the explicit constructor overload if this redundant computation matters for your entity-type count. |
| `ConnectionStringNormalizationCache` (`pengdows.crud/internal/ConnectionStringNormalizationCache.cs`) | Static, process-wide | A **SHA-256 digest** of the connection string plus the read-only key/value, application-name setting and read-only suffix (everything that shapes the cached map) — never the raw string | 256 entries (`BoundedCache`), evicts beyond that | Populated only when a context compares its primary and read-only connection strings for equivalence (`AreConnectionStringsEquivalentIgnoringCredentials`). Values are credential-scrubbed before storage (password/user/secret/token/access-style keys are dropped from the normalized map), and keys are a one-way hash, so no connection string or credential is retained. |

**Secret handling summary:** no cache in this table retains a connection string or credential.
`ConnectionStringNormalizationCache` is the only one derived from connection strings: its values are
credential-scrubbed and its keys are SHA-256 digests, and it holds at most 256 entries. No cache in
this table stores connection strings, credentials, or raw tenant-identifying strings as either key
or value — keys are types, shape hashes, dialect instances, or plain query-text strings with no
tenant identity baked in.

**Collision guarantees:** every `BoundedCache` here uses `ConcurrentDictionary`'s own
`Equals`/`GetHashCode` contract for its key type, and the dialect-instance-keyed tables use
reference identity — for string-, type-, and instance-keyed caches a hash collision resolves
correctly via `Equals` and never returns the wrong entry. The two **shape-keyed** reader-plan caches
are the exception: `BaseTableGateway._readerPlans` is keyed by the shape hash itself (a `long`
widened from a 32-bit `HashCode`), and `DataReaderMapper._planCache`'s key embeds a 64-bit
`SchemaHash` rather than the column list. Neither re-verifies the actual column names/types on a
hit, so two distinct result shapes whose hashes collide would share a compiled plan. The
probability is very low at these cache sizes, but it is a hash-equality guarantee, not a
structural one.

## Context-derived generation contract

Every gateway method takes an optional `IDatabaseContext? context = null` parameter (defaulting to
the constructor's context if omitted). This parameter determines, for *that one call*:

- **Which dialect** renders the SQL (`context.GetDialect()`) — identifier quoting, parameter
  markers, upsert strategy, and every other dialect-specific behavior documented in
  `docs/capability-discovery.md`.
- **Which physical connection/transaction** the operation actually executes against.
- **Parameter values** — bound fresh per call, never cached across calls.

What is **not** re-derived per call — fixed once at gateway construction, from whichever context
built the gateway, and shared across every subsequent call regardless of which context is passed
in later:

- **Entity metadata** (`_tableInfo`, from `TypeMapRegistry.GetTableInfo<TEntity>()`) — derived
  purely from `TEntity`'s own attributes via reflection, identical regardless of which context's
  registry computed it.
- **The reader-plan cache** (`_readerPlans`) and its configured capacity (`ReaderPlanCacheSize`) —
  a property of the gateway's lifetime, not of any one call's context.
- **The dialect-instance-keyed query/parameter-name caches** (`_queryCache`,
  `_whereParameterNames`) — the tables themselves live for the gateway's lifetime; each call picks
  (or creates) the inner cache belonging to its own context's dialect instance.

This is what makes one singleton gateway instance safe and efficient across an unbounded number of
tenant contexts: the expensive, reusable parts (compiled reader plans, rendered SQL text) are
cached per *shape* (and, for SQL text, per live dialect instance), while the cheap, per-call parts (connection, parameters) come fresh from
whichever context you pass to that call.

### `ISqlContainer.Clone(otherContext)` — rebinding, not translation

`Clone(context)` changes which dialect/connection a *pre-built* container executes and renders
against for its **next** execution — it does not, and cannot, translate arbitrary caller-authored
SQL text between dialects. See `docs/sql-container-templates.md`'s "Cross-dialect rebinding: what
actually gets re-rendered" section for the exact mechanics, including the one real caveat:
identifier quoting (`WrapObjectName` output) is baked into the query text at *build*
time and does not re-render on `Clone`, unlike parameter markers, which do re-render correctly per
target dialect on the clone's own first execution (provided the original hadn't already executed
and cached its rendered text — see that doc's caveat). That doc is the canonical reference for `Clone`
— this section exists only to connect it to the gateway-level context contract above: a gateway
that builds one query once and clones it per tenant context is exactly the mechanism this whole
document describes.
