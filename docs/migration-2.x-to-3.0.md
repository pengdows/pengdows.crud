# Migration: 2.x → 3.0

This document lists public-surface removals, signature/shape changes, and observable behavior
changes in the `3.0` branch relative to `main`/2.x that a consumer's build or runtime could be
affected by. Every entry below was checked directly against current source on both branches (not
inferred from a commit message or from `InterfaceApiCheck`'s baseline alone — that tool only
tracks interface signatures in `pengdows.crud.abstractions`, and several real breaks here live
below that layer: concrete-class setter visibility, enum underlying types, record positional
shape, NuGet dependency composition, and runtime default values).

An earlier version of this document claimed to list *every* public-surface change. It didn't — a
follow-up audit found the additional items marked below. Treat any future edit to this file the
same way: verify against source, don't transcribe from memory or from `interfaces.txt` alone.

## Removed

### `IDatabaseContext.DataSource`

The `DbDataSource? DataSource { get; }` property is gone (also removed from the concrete
`DatabaseContext`/`TransactionContext` implementations). It exposed the raw ADO.NET `DbDataSource`
(e.g., `NpgsqlDataSource`), which let a caller bypass `DatabaseContext`'s connection governance
(pooling, mode coercion, metrics) entirely.

**Migration:** use `IDatabaseContext.CreateSqlContainer(...)`/`GetConnection(...)`-based APIs
instead of reaching for a raw provider `DbDataSource`.

### The SQL-standard-level abstraction, in full — not just one property

`SqlStandardLevel` (`pengdows.crud.enums`) is deleted outright, along with every public member that
exposed it:

- `IDataSourceInformation.StandardCompliance`
- `IDatabaseProductInfo.StandardCompliance`
- `ISqlDialect.MaxSupportedStandard`

**Migration:** none of the built-in `ISqlDialect` capability flags depended on a central
`SqlStandardLevel` derivation as of this release — each `Supports*` flag is now either an explicit
per-dialect override or a plain constant on the base `SqlDialect` class. If you read
`StandardCompliance`/`MaxSupportedStandard` directly, switch to the specific `Supports*` capability
flag you actually care about.

**Correctness note, not just a removal:** deleting the derivation collapsed
`SupportsXmlTypes`/`SupportsUserDefinedTypes`/`SupportsTemporalData`/`SupportsTruncateTable` to
unconditional base-class constants (`false`, `false`, `false`, `true`) for every dialect with no
explicit override — this was a real regression for `SqlServerDialect` (which had version-gated
`true` values on `main` for modern SQL Server) and a real bug for `SqliteDialect`/`AccessDialect`
(neither engine has a `TRUNCATE TABLE` statement, but the new `true` default applied to them
too). Fixed in this release, each verified against a real container (not assumed from
documentation):
- `SqlServerDialect`: `SupportsXmlTypes`/`SupportsUserDefinedTypes` (SQL Server 2005+) and
  `SupportsTemporalData` (SQL Server 2016+), via version checks.
- `SqliteDialect`/`AccessDialect`: `SupportsTruncateTable => false` (neither has that statement).
- `PostgreSqlDialect`: `SupportsXmlTypes` (8.3+, confirmed live against 16.13) and
  `SupportsUserDefinedTypes => true` (`CREATE TYPE` composite types, confirmed live).
- `CockroachDbDialect` (inherits `PostgreSqlDialect`): explicitly overrides
  `SupportsXmlTypes => false` — confirmed live that CockroachDB rejects an `xml` column
  (`"syntax error: unimplemented: this syntax"`) even though it inherits the new PostgreSQL
  `true`; `SupportsUserDefinedTypes` is correctly left inherited (`CREATE TYPE` confirmed live).
- `YugabyteDbDialect` (inherits `PostgreSqlDialect`): both flags correctly inherited as `true` —
  confirmed live that YSQL's genuine PostgreSQL query-layer heritage supports both `xml` columns
  and `CREATE TYPE`, unlike CockroachDB's reimplementation.
- `MariaDbDialect`: `SupportsTemporalData` (10.3+, system-versioned tables — confirmed live
  against 10.11 with an actual `INSERT`/`UPDATE`/`SELECT ... FOR SYSTEM_TIME ALL` round trip).
  `MySqlDialect` itself has no equivalent and stays `false`.

**Still not independently verified**: Oracle, MySQL (XML/UDT — well-established as unsupported,
just not container-verified this pass), Firebird, TiDB, DuckDB (already had explicit `false`
overrides pre-3.0, unaffected), Db2, Snowflake, SAP HANA, Informix, InterBase, Sybase ASE, Spanner,
FlatFile (already had explicit overrides, unaffected). `SnowflakeDialect`'s old `true` value for
these flags on `main` was itself only a side effect of its `Sql2016` default-level fallback, never
a deliberately verified per-capability claim, so it was deliberately **not** restored on unverified
confidence. If you rely on `SupportsXmlTypes`/`SupportsUserDefinedTypes`/`SupportsTemporalData`/
`SupportsTruncateTable` for a dialect not listed as fixed above, verify it against the real engine
before trusting it — this is not a 3.0 regression for those dialects (`main` had the same
unverified `Sql92`-derived value), just still-open verification debt.

### `pengdows.crud.connection.ConnectionLocalState` (concrete class)

The concrete `public sealed class ConnectionLocalState : IConnectionLocalState` is gone entirely —
not merely internalized (see "Visibility reduced" below for the interface itself, which is a
separate, narrower change). Confirmed: no file/class of this name exists anywhere in the 3.0 tree.

**Migration:** this was always meant to be an internal connection-locking primitive; there is no
public replacement. If you referenced it directly you were reaching past the supported API surface.

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

### `pengdows.crud.tenant.ITenantConfiguration`

Unused, empty public marker interface with zero implementations and zero consumers outside its own
type and test file — internalized in this release (not present in either audit pass; found and
fixed directly against current source).

**Migration:** none expected. If you implemented or referenced it directly, there is no public
replacement.

## Visibility reduced (public → internal)

These interfaces are no longer part of the public API surface. All were confirmed to have no
external extension point depending on their public visibility (`InterfaceApiCheck` baseline
updated accordingly).

- `pengdows.crud.threading.ILockerAsync`
- `pengdows.crud.connection.IConnectionLocalState`
- `pengdows.crud.tenant.ITenantConfiguration`
- `pengdows.crud.TypeCoercionOptions` (was `public record`, now `internal sealed record`)

**Migration:** if your code implemented or referenced any of these types directly (not expected for
a typical consumer — these are low-level connection/locking/coercion-configuration primitives),
that code will no longer compile. There is no public replacement.

## Concrete property setter visibility reduced

Neither of these appears in `InterfaceApiCheck`'s baseline — both interfaces (`IDatabaseContext`)
already exposed these properties as getter-only, so this is invisible to that tool. It's only a
break for code that assigns the property on the concrete `DatabaseContext` type directly.

- `DatabaseContext.ReadWriteMode` — setter changed from `public` to `private`. The read/write flags
  it derives are baked into the connection string, pool sizing, and connection-strategy selection
  at construction; nothing re-runs on a later assignment, so a public setter could silently desync
  those from the flag rather than actually reconfigure anything.
- `DatabaseContext.ProcWrappingStyle` — setter changed from `public` to `internal`. Production code
  only ever assigns the backing field directly at its real detection call sites; the setter exists
  purely so test fixtures can force a style against a fake dialect.

**Migration:** if you assigned `context.ReadWriteMode = ...` or `context.ProcWrappingStyle = ...`
directly on a concrete `DatabaseContext` instance, that code no longer compiles. There was never a
supported way to change either after construction — reconstruct the context with the desired
`DatabaseContextConfiguration` instead.

## Enum changes

### `SupportedDatabase` underlying type: `int` → `ulong`

Existing member values are preserved exactly (each is still `1UL << n`, matching the old `int`
literals), and `[Flags]` was already present on `main`. This is **not** a value-collision risk, but
it is an ABI/reflection-shape change: `Enum.GetUnderlyingType(typeof(SupportedDatabase))` now
returns `typeof(ulong)`, not `typeof(int)`; code that does `(int)someDatabase` or round-trips the
enum through an `int`-typed serializer/config binder needs review.

### `DbMode.KeepAlive` renamed to `DbMode.PreventDatabaseUnload`

```csharp
PreventDatabaseUnload = 1,

[Obsolete("Use PreventDatabaseUnload.")]
KeepAlive = PreventDatabaseUnload,
```

Source compiles unchanged (the alias resolves to the same numeric value) **unless your build
treats warnings as errors**, in which case the `[Obsolete]` warning on `DbMode.KeepAlive` now
fails the build. More subtly: with two enum names sharing one value, .NET does not guarantee which
name `Enum.ToString()`/`Enum.GetName()` returns for that value — do not assume persisted/logged
`DbMode` text is stably `"KeepAlive"` or `"PreventDatabaseUnload"` across this change. If you
persist or compare `DbMode` as a string anywhere, migrate explicitly to `"PreventDatabaseUnload"`.

### `DbErrorCategory.AmbiguousResult` added (value `6`)

New member on an existing public enum, feeding the transaction commit-ambiguity work (see
"Behavior changes" below). Deliberately **excluded** from the transient/retryable category set. An
existing exhaustive `switch` over `DbErrorCategory` with no `default` arm will now fail to compile
(a `switch` expression) or silently fall through (a `switch` statement with no `default`) for this
new value — audit any exhaustive switch over this enum.

## Signature changes

These are source-compatible for ordinary callers (new parameters are optional/default-valued) but
are genuine **binary** breaks — the CLR method token changes, so an already-compiled consumer
assembly can throw `MissingMethodException` at runtime without a recompile. They are also
**source** breaks for anyone implementing the affected interface directly rather than calling it.

- **`IDatabaseContext.BeginTransaction(IsolationProfile, ExecutionType)`** gained a third parameter,
  `IsolationResolutionPolicy policy = IsolationResolutionPolicy.AllowHigher`.
  **`BeginTransactionAsync(IsolationProfile, ...)`** gained the same parameter, placed *after*
  `CancellationToken cancellationToken = default`.
- **`ISqlDialect.BuildBatchUpdateSql(...)`** gained two trailing optional parameters:
  `string? versionColumnName = null, bool versionColumnIsOpaque = false`.
- **`TransactionException`**'s public constructor gained a trailing optional parameter:
  `TransactionPhase? phase = null`.

**Migration:** recompile against 3.0. If you implement `IDatabaseContext`, `ISqlDialect`, or
construct `TransactionException` by name from a precompiled assembly, update the call/implementation.

### `ISqlDialect` gained 9 new members with no default implementation

Anyone implementing `ISqlDialect` from scratch (not extending the internal `SqlDialect` base class,
which already provides these) must now implement all of:

`IsClientServerDatabase`, `IsEmbeddedSingleWriterEngine`, `DetectInMemoryKind(string?)`,
`CoerceConnectionMode(DbMode, string?, bool)`, `JoinParenthesization`,
`SupportsOverridingSystemValue`, `SavepointCapabilities`, `GetReleaseSavepointSql(string)`,
`SupportsSemicolonStatementSeparator`.

A number of other new `ISqlDialect` members *do* have default interface implementations and are
safe to ignore unless you want to override them: `EmitsAnsiMergeSyntax`,
`RequiresMergeStatementTerminator`, `ReadOnlyConnectionsCanBlockConcurrentWriters`,
`RequiresSerializedConnectionOpen`, `RejectsExplicitIsolationLevelOnBeginTransaction`,
`SupportsPureKeyUpsert`, `RequiresOutputParameterForReturning`,
`WrapsInsertStatementForReturning`, `RenderInsertReturningPrefix`.

**Migration:** if you implement `ISqlDialect` directly, add the 9 abstract members above.
`IDatabaseContextFactory.CreateAsync` was deliberately given a default implementation
(`Task.FromResult(Create(...))`) specifically to avoid this class of break for that interface —
`ISqlDialect` predates that pattern being applied consistently.

### New mandatory members on other public interfaces

Each of these needs an implementation if you implement the interface directly rather than using the
library's own concrete type:

- `ITransactionContext.ReleaseSavepointAsync(string)` — throws `NotSupportedException` when the
  dialect's `SavepointCapabilities` lacks `Release`, rather than silently no-op-ing.
- `IIsolationResolver.Resolve(IsolationProfile, IsolationResolutionPolicy)` and
  `ResolveWithDetail(IsolationProfile, IsolationResolutionPolicy)`.
- `ITenantContextRegistry.GetContextAsync(...)`, `AcquireLease(...)`, `AcquireLeaseAsync(...)`.
- `ITableGateway<TEntity, TRowID>.AuditCreationPolicy` / `IPrimaryKeyTableGateway<TEntity>.AuditCreationPolicy`
  — new `AuditCreationPolicy AuditCreationPolicy { get; init; }` member, defaulting to
  `AuditCreationPolicy.PreserveExplicitValues`. See the root `CLAUDE.md`'s "CRITICAL: Audit Field
  Behavior" section: an application binding an untrusted request DTO directly onto an audited
  entity should set this to `AuditCreationPolicy.Authoritative` explicitly.
- `IDataSourceInformation.ParsedVersion`.
- `IDatabaseContextConfiguration` gained `EnforceUniqueConnectionString`, `MaxQueuedReads`,
  `MaxQueuedWrites`, `SessionInitializationFailureMode` — all four have workable default values if
  you implement the interface by hand rather than using `DatabaseContextConfiguration`.

## Public record positional-shape changes

Each of these is source-compatible for ordinary *construction* (a compatibility constructor was
kept, or the new fields are trailing and optional), but the synthesized primary-constructor arity
and `Deconstruct` changed — so positional-pattern deconstruction (`var (a, b, c) = value;`) written
against the old shape no longer compiles, and any code holding a pre-3.0-compiled reference to the
old constructor/`Deconstruct` breaks at the binary level.

- **`IsolationResolution`**: 3 positional components (`Profile, Level, Degraded`) → 4
  (`Profile, Level, Degraded, Kind`). A 3-argument constructor overload was added back for source
  construction compatibility, but `Deconstruct` now always produces 4 outputs.
- **`DatabaseMetrics`**: gained 3 new trailing optional positional components —
  `AvgReaderTimeToFirstRowMs`, `AvgReaderConsumptionMs`, `AvgReaderLeaseMs` (all default `0d`).
- **`DatabaseRoleMetrics`**: gained the same 3 new trailing optional positional components.

**Migration:** update any `(a, b, c) = ...` deconstruction of `IsolationResolution`. Ordinary
property access (`resolution.Profile`, `metrics.AvgCommandMs`, etc.) is unaffected.

## Behavior changes

### CockroachDB native `IsolationLevel` is now enforced, not silently upgraded

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

### PostgreSQL/CockroachDB/YugabyteDB `IsolationProfile.SafeNonBlockingReads` now succeeds instead of throwing

The mirror image of the CockroachDB change above, in the opposite direction — and, unlike the
framing this document originally gave it, a deliberate, well-tested correctness fix rather than an
unverified behavior change. `main` threw `TransactionModeNotSupportedException` for
`BeginTransaction(IsolationProfile.SafeNonBlockingReads)` against PostgreSQL/YugabyteDB, on the
premise that PostgreSQL has no non-blocking-safe-reads equivalent. That premise was wrong:
PostgreSQL's `REPEATABLE READ` takes a transaction-start MVCC snapshot, is fully non-blocking, and
(unlike the ANSI baseline) also prevents phantom reads for the transaction's lifetime — see
[PostgreSQL's own docs](https://www.postgresql.org/docs/current/transaction-iso.html#XACT-REPEATABLE-READ).
`PostgreSqlDialect.GetIsolationProfileMapping()` now maps `SafeNonBlockingReads` directly to
`IsolationLevel.RepeatableRead` through the same generic resolution path every other database uses
— there is no separate product-switch special case anymore. Covered by
`DatabaseContextIsolationTests.BeginTransaction_SafeNonBlockingReads_ResolvesForPostgresCompatibleDatabases`
and `IsolationResolverTest.Resolve_SafeNonBlockingReads_PostgresCompatibleDatabases_ResolvesToRepeatableRead`.

**Migration:** if you branched on `TransactionModeNotSupportedException` for this profile/database
combination, or deliberately chose a different profile because this one was rejected, review that
logic — it now gets a genuinely correct `RepeatableRead` transaction instead.

**`TransactionModeNotSupportedException` removed.** This change left it with zero throw sites
anywhere in 3.0 — a public exception type nothing could produce, misdescribing the runtime contract
(`README.md` still called it "savepoint or read-only tx on unsupported dialect," neither of which
throws it). Removed outright rather than kept-and-redocumented or deprecated, consistent with this
release's other removals of unreferenced public surface (`IEphemeralSecureString`,
`WeirdTypeAttributes.cs`, the concrete `ConnectionLocalState`). If you had a
`catch (TransactionModeNotSupportedException)`, it no longer compiles; catch `NotSupportedException`
(or the more specific `ReadOnlyContextException`/`InvalidOperationException` the two former call
sites now throw) instead. Locked down by
`CompleteExceptionTests.TransactionModeNotSupportedException_NoLongerExists`.

### Read-only contexts fail closed on session-initialization failure by default

`DatabaseContextConfiguration.SessionInitializationFailureMode` is new (see above). When left
unset, 3.0 resolves it based on the context's own `ReadWriteMode`:

```csharp
configuration.SessionInitializationFailureMode
    ?? (ReadWriteMode == ReadWriteMode.ReadOnly
        ? SessionInitializationFailureMode.FailClosed
        : SessionInitializationFailureMode.BestEffort);
```

An unchanged, `ReadOnly`-configured context that previously logged a failed session-setting
initialization and continued (`main`'s effective behavior was always best-effort) can now throw
`ConnectionException` on construction instead. This is a deliberate security-motivated default — an
unknown session state is treated as unsafe for a context meant to guarantee read-only behavior —
but it's a real behavior change for the same configuration and the same provider failure.

**Migration:** set `SessionInitializationFailureMode = SessionInitializationFailureMode.BestEffort`
explicitly on a `ReadOnly` context's configuration if you need the old best-effort behavior back.

### `PoolAcquireTimeout` default: 5s → 10s

`DatabaseContextConfiguration.DefaultPoolAcquireSeconds` changed from `5` to `10`. An application
relying on the default now waits twice as long before a saturated pool throws
`PoolSaturatedException`. Set `PoolAcquireTimeout` explicitly if your application depends on the
old 5-second ceiling.

### SQL Server: no session-settings round trip on modern compatibility levels

SQL Server session initialization now skips its `SET` script entirely once the detected
compatibility level is confirmed to already have the same effective behavior. This removes one
per-checkout round trip in the common case (a performance change) but is also an *observable*
change: `SessionInitCount` no longer increments the same way, and anything tracing/profiling the
per-checkout initialization batch will see it disappear for supported SQL Server versions.

## Packaging / build changes

### `AssemblyVersion`: `2.0.5.0` → `3.0.0.0`

Both assemblies remain strong-named. On modern .NET (unlike .NET Framework), a loaded assembly
whose version is equal to or higher than the version a reference asked for can satisfy that
reference without a binding redirect, so this alone does not force every consumer to recompile.
However, any precompiled consumer that references one of the removed/changed members documented
above can still fail at runtime (`MissingMethodException`, `MissingMemberException`,
`TypeLoadException`) regardless of assembly-version compatibility — the two are independent
questions.

### `pengdows.crud.abstractions` dropped a transitive dependency

```diff
- Microsoft.Extensions.Configuration
- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Logging
+ Microsoft.Extensions.DependencyInjection.Abstractions
+ Microsoft.Extensions.Logging.Abstractions
```

`Microsoft.Extensions.Configuration` is removed entirely (no replacement — nothing in
`pengdows.crud.abstractions` used it beyond what the `.Abstractions` swap covers), and the DI/Logging
references narrow from the full implementation packages to their `.Abstractions`-only counterparts.
A consumer that was getting `IConfiguration`, the full DI container, or the full logging
implementation transitively through a reference to `pengdows.crud`/`pengdows.crud.abstractions`
will need to reference those packages directly.

### New analyzer diagnostic: `PGC027`

`GatewayCallSiteContextAnalyzer` (`DiagnosticSeverity.Error`) ships in the separate
`pengdows.crud.analyzers` package, gated behind an explicit opt-in
(`<PengdowsMultiTenancy>true</PengdowsMultiTenancy>` in the consuming project). It is **off by
default** and does not affect a build that doesn't reference `pengdows.crud.analyzers` or doesn't
set that property. Not independently confirmed whether the root `pengdows.crud` package's `.nupkg`
transitively delivers the analyzer's `build/*.props` asset — if you reference only `pengdows.crud`
today, verify whether `PGC027` is active for you before assuming it is or isn't.

## Known follow-up needed (not yet resolved as of this writing)

- **`SupportsXmlTypes`/`SupportsUserDefinedTypes`/`SupportsTemporalData`/`SupportsTruncateTable`**
  are still unverified against their real engines for Oracle, Firebird, TiDB, Db2, Snowflake, SAP
  HANA, Informix, InterBase, Sybase ASE, and Spanner (see "The SQL-standard-level abstraction"
  above for what's already been fixed and container-verified: SQL Server, SQLite, Access,
  PostgreSQL, CockroachDB, YugabyteDB, MariaDB). Not a 3.0 regression for the remaining dialects —
  `main` had the same unverified value — but worth closing out with real per-dialect verification.
