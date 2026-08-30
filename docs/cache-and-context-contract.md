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
| `DataReaderMapper._planCache` | Static, process-wide | `(Type, RecordsetShape, ColumnsOnly, EnumMode)` | 128 (`MaxPlanCacheSize`) | `DataReaderMapper` is public and externally reachable (`DataReaderMapper.Instance`, see `docs/data-reader-mapper.md`) — this cache backs its own hydration path, separate from gateway hydration's `_readerPlans` below. Not tenant-cardinality-related; keyed by entity type + result shape. |
| `DataReaderMapper._setterCache` | Static, process-wide | `SetterCacheKey` (property-level) | 512 (`MaxSetterCacheSize`) | Same as above. |
| `DataReaderMapper._propertyLookupCache` | Static, process-wide | `PropertyLookupCacheKey` | 64 (`MaxPropertyLookupCacheSize`) | Same as above. |
| `BaseTableGateway._readerPlans` | Per-gateway-instance | `RecordsetShape` (column names+types, structural equality) | Configurable via `ReaderPlanCacheSize`, default 32 (`DefaultReaderPlanCapacity`) | **Not tenant-cardinality-bound and doesn't need to be** — captured once at gateway construction (CORE-019: intentional, not a gap), keyed by result-set shape, which is a property of the query/entity, not of which tenant executed it. One singleton gateway serving 10,000 tenants still has at most a few dozen distinct shapes. |
| `BaseTableGateway._columnListCache` | Per-gateway-instance | `string` | 100 (`MaxCacheSize`) | Same tenant-independence reasoning as `_readerPlans`. |
| `BaseTableGateway._queryCache` | Per-gateway-instance, two-level | Outer: `ConcurrentDictionary<string, BoundedCache<string,string>>` keyed by **dialect fingerprint** (`DatabaseType`+`ParsedVersion`, via `ISqlDialect.GetCacheFingerprint()`); inner: `BoundedCache` capped at 100 per fingerprint | Outer dictionary itself is **unbounded** — one entry per distinct engine+version fingerprint ever seen by this gateway | **This is the real multi-tenant cache-design point of DOC-018**: keying by fingerprint (not by tenant, not by dialect instance) is deliberate — two tenants on the *same* engine+version share one inner cache (correct, since the generated SQL text is identical), while two tenants on *different* versions of the same engine (e.g. MySQL 8.0.19 vs. 8.0.33) get separate entries, because generated SQL can legitimately differ by version. The outer dictionary's cardinality is bounded by how many distinct engine+version combinations exist in your fleet, not by tenant count — realistically small even at large tenant scale. |
| `BaseTableGateway._whereParameterNames` | Per-gateway-instance, two-level | Same fingerprint-keyed structure as `_queryCache` | Same reasoning | Same as `_queryCache`. |
| `BaseTableGateway._wrappedTableNameCache` | Per-gateway-instance | `ConcurrentDictionary<ISqlDialect, string>` — **plain dictionary, not a `BoundedCache`** | **Unbounded, no eviction** | **Real, previously-undocumented growth risk, unlike every sibling cache above — but its practical severity depends entirely on whether contexts are ever disposed and recreated during live operation, which is not currently a designed feature (see `multitenancy-architecture.md`'s "Context disposal: application shutdown, not live ejection, is the designed path").** This is keyed by `ISqlDialect` *instance* (not fingerprint, not database type) — every `IDatabaseContext` gets its own dialect instance, so every distinct context a singleton gateway is ever called with adds one permanent entry that is never evicted, and it does not shrink when a context is disposed. Under the intended, designed lifecycle (contexts disposed only at application shutdown), this cache simply grows to one entry per tenant context that ever existed for the process's lifetime and is then discarded with the whole process — not a live leak. **It would only become an actual slow memory leak if an application chose to call `Invalidate`/recreate contexts repeatedly during live operation** (itself explicitly not a recommended pattern, per the correction above) — flagged here for a future CORE-level fix (switch to fingerprint-keying like `_queryCache`, or bound it) in case that assumption ever changes, not something this doc-only pass fixes or something to treat as an active production risk today. |
| `TypeMapRegistry._typeMap` | **Per-`DatabaseContext`-instance by default** (each `DatabaseContext(...)` constructor creates its own `new TypeMapRegistry()` unless one is explicitly injected via one of the constructor overloads accepting `ITypeMapRegistry typeMapRegistry`) | `System.Type` | Unbounded `ConcurrentDictionary`, but self-limiting — bounded by the number of distinct compiled entity `Type`s the application ever registers, which cannot grow at runtime | Confirmed non-issue for growth (matches this project's earlier "TypeMapRegistry staleness" investigation — see `docs/planning/future-work.md`'s architecture-review history). **Redundancy tradeoff, not a leak:** because each `DatabaseContext` gets its own registry by default, N tenant contexts recompute and cache the same entity metadata N times (small, one-time cost per context, not per-request) — share one `ITypeMapRegistry` instance across tenant contexts via the explicit constructor overload if this redundant computation matters for your entity-type count. |
| `ConnectionStringNormalizationCache` | Static, process-wide | SHA-256 digest of the raw connection string (never the raw string — CORE-012 fix) | 256 (`MaxEntries`), LRU | Values are already credential-scrubbed before storage; keys are now a one-way hash, not the credential-bearing string itself. Safe for long-lived multi-tenant processes with many distinct per-tenant connection strings. |

**Secret handling summary:** only `ConnectionStringNormalizationCache` ever had a connection-string
(and therefore potential-credential) exposure, and it's fixed (hashed keys, scrubbed values). No
other cache in this table stores connection strings, credentials, or raw tenant-identifying
strings as either key or value — keys are types, shapes, dialect fingerprints, or plain query-text
strings with no tenant identity baked in.

**Collision guarantees:** every `BoundedCache` here uses `ConcurrentDictionary`'s own
`Equals`/`GetHashCode` contract for its key type — a hash collision resolves correctly via
`Equals`, it never silently returns the wrong entry (this is exactly the property CORE-013 fixed
for `RecordsetShape`-keyed caches this session, after finding the previous bare-hash keying was
unsafe). `_wrappedTableNameCache`'s key is `ISqlDialect` reference identity (default
`object.Equals`), which is trivially collision-free by construction.

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
  registry computed it (see CORE-019's resolution in `docs/planning/future-work.md` for the full
  reasoning).
- **The reader-plan cache** (`_readerPlans`) and its configured capacity (`ReaderPlanCacheSize`) —
  a property of the gateway's lifetime, not of any one call's context.
- **The dialect-fingerprint-keyed query/parameter-name caches** (`_queryCache`,
  `_whereParameterNames`) — shared across every call whose context resolves to the same
  engine+version fingerprint, regardless of which specific tenant context that came from.

This is what makes one singleton gateway instance safe and efficient across an unbounded number of
tenant contexts: the expensive, reusable parts (compiled reader plans, rendered SQL text) are
cached once per *shape*, while the cheap, per-call parts (connection, parameters) come fresh from
whichever context you pass to that call.

### `ISqlContainer.Clone(otherContext)` — rebinding, not translation

`Clone(context)` changes which dialect/connection a *pre-built* container executes and renders
against for its **next** execution — it does not, and cannot, translate arbitrary caller-authored
SQL text between dialects. See `docs/sql-container-templates.md`'s "Cross-dialect rebinding: what
actually gets re-rendered" section for the exact mechanics, including the one real caveat found
this session: identifier quoting (`WrapObjectName` output) is baked into the query text at *build*
time and does not re-render on `Clone`, unlike parameter markers, which do re-render correctly per
target dialect on the clone's own first execution. That doc is the canonical reference for `Clone`
— this section exists only to connect it to the gateway-level context contract above: a gateway
that builds one query once and clones it per tenant context is exactly the mechanism this whole
document describes.
