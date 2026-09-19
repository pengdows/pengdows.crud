# Application Name / Pooling / Read-Only Audit — Databases Added Since 2.0.5 (`main`)

## Status: partially applied

The three findings that needed no live server connection (real, driver-confirmed
`ApplicationNameSettingName` keywords for FlatFile/Db2/Sybase ASE) have been applied via TDD:
`FlatFileDialect.ApplicationNameSettingName => "applicationName"` (plus
`SupportsExternalPooling => false` and `GetReadOnlyConnectionParameter() => "readonly=true"`,
both also fully confirmed via `pengdows.flatfile`'s own source, no live test needed),
`Db2Dialect.ApplicationNameSettingName => "ClientApplicationName"`, and
`SybaseDialect.ApplicationNameSettingName => "ApplicationName"`. See each dialect file for the
locked-down test coverage (`FlatFileDialectTests.cs`, `Db2DialectTests.cs`,
`SybaseDialectTests.cs`).

While re-verifying these via independent reflection (not just trusting this document's own
earlier pass), a real error in this document was caught and corrected: the original Sybase ASE
row claimed no `MinPoolSize`/`MaxPoolSize`-equivalent property exists on
`AdoNetCore.AseClient.Internal.ConnectionParameters` — false. Both exist (`Int16`, defaulting to
`MinPoolSize=0`/`MaxPoolSize=100`, matching every other driver here), findable only by
constructing the type and reading its defaults, not just enumerating property names as the
original pass did. See the Sybase ASE section below for the corrected finding.

**Sybase ASE's read-only question is now also resolved (2026-09-18, live against a real ASE 16.0
container)**: no usable per-connection/per-session read-only mechanism exists. `SET TRANSACTION
READ ONLY` is a flat syntax error (doesn't even parse); `sp_dboption <db>, 'read only', true` is
real, confirmed-live enforcement but database-wide, not connection-scoped — wiring it into
`GetReadOnlyConnectionParameter()` would break the writer connection too. Deliberately left
unimplemented (stays at the base `null`); see `SybaseDialect.cs`'s file-level AI SUMMARY and
`SybaseDialectTests.GetReadOnlyConnectionParameter_ReturnsNull_NoUsablePerConnectionMechanismExists`.

**InterBase is now also resolved (2026-09-18, live against a real Docker container)**: its
discriminator gap is fixed (`ReadOnlyPoolDiscriminatorSettingName => "fetch size"`, value `"200"`
— the driver's own compiled-in default, so it's guaranteed behaviorally inert rather than merely
observed to look unchanged). Its read-only claim is CONFIRMED LIVE to work at the driver level
(`IBTransactionOptions` with `IBTransactionBehavior.Read`), but is NOT wired into pengdows.crud —
`ISqlDialect.TryEnterReadOnlyTransaction`'s hook runs after the transaction is already opened via
the ordinary `IsolationLevel`-based path, and InterBase requires the read-only flag at
transaction-*creation* time. Implementing this needs a new pengdows.crud extension point (see
"Next steps" item 5) — left open rather than forcing a broken partial fix.

**Informix's read-only question is now also resolved (2026-09-18, live against a real
`icr.io/informix/informix-developer-database` container)**: `SET TRANSACTION READ ONLY` inside an
active transaction genuinely enforces read-only (a subsequent write fails with `"Invalid
operation for a READ-ONLY transaction."`) and is now implemented via
`InformixDialect.TryEnterReadOnlyTransaction`/`TryEnterReadOnlyTransactionAsync` — the same shared
helper `OracleDialect` uses. Its discriminator gap remains open: two candidates connect
successfully (`Optofc=1`, `DelimIdent=true`) but neither is confirmed behaviorally inert, so
nothing was implemented rather than guessing (see its section below).

**Db2's questions are now also resolved (2026-09-18, live against a real `ibmcom/db2:11.5.8.0`
container)**: `SET TRANSACTION READ ONLY` is a flat syntax error there too (contradicting the
original ANSI-plausibility assumption), so left unimplemented, matching Sybase's identical
conclusion. Separately, a real, previously-undiscovered bug was found and fixed:
`Db2Dialect.MaxPoolSizeSettingName`/`MinPoolSizeSettingName` were never set at all, so any
explicit `Max Pool Size` a caller wrote into their own Db2 connection string was silently ignored.
The driver itself was also confirmed to not enforce `Max Pool Size` as a real cap at any value —
pengdows.crud's own in-process `PoolGovernor` is Db2's sole real admission-control safety net.

The only remaining gaps (HANA's session-SQL read-only claim and HANA/Informix's pool
discriminators) still require live server verification — see "Next steps" at the bottom for what's
still open.

## Why this document exists

The Access work in this session (see `docs/connection/access-concurrency-verification.md` and
`AccessDialect.cs`'s file-level AI SUMMARY) found that `AccessDialect` had silently inherited
`SqlDialect`'s base defaults for `ApplicationNameSettingName` (null), `PoolingSettingName`
("Pooling"), `ReadOnlyPoolDiscriminatorSettingName` (null), and `GetReadOnlyConnectionParameter()`
(null) — none of these were ever examined against the real driver, and one of them
(`PoolingSettingName`) was an outright bug because Access's real driver doesn't recognize the
generic "Pooling" keyword at all.

This raised the obvious question: do the other databases added since `main` (Db2, FlatFile,
SybaseASE, Informix, SapHana, InterBase, SingleStore, Spanner) have the same kind of unexamined
gap? This document is the research pass answering that, database by database, using each real
driver assembly's actual `DbConnectionStringBuilder`-derived type (or, where the public builder
is a thin wrapper, the internal type that actually parses the connection string) — inspected via
reflection on the real NuGet package DLLs already referenced by this solution, with **no live
database connection required** for this part.

**What this document does NOT do**: verify read-only enforcement (connection-string or
session-SQL) actually behaves as expected against a real server. That needs a live connection,
which requires Docker (currently unavailable in this environment) and, for several of these,
significant additional setup (SAP HANA needs 16-32GB RAM; InterBase needs an externally-managed
licensed container). Live verification of the session-SQL claims below is explicitly deferred —
see the "Not yet live-verified" callouts throughout.

## Safe via inheritance — no gap, no work needed

- **`SingleStoreDialect`**: doesn't exist as a class — `SupportedDatabase.SingleStore` is served
  by a plain `MySqlDialect` instance (`SqlDialectFactory.cs`: `new MySqlDialect(factory, logger,
  SupportedDatabase.SingleStore)`). Inherits `MySqlDialect.ApplicationNameSettingName` and its
  pooling settings automatically.
- **`SpannerDialect : PostgreSqlDialect`**, **`CockroachDbDialect : PostgreSqlDialect`**,
  **`YugabyteDbDialect : PostgreSqlDialect`**: inherit `PostgreSqlDialect.ApplicationNameSettingName`
  and `PostgreSqlDialect.GetReadOnlyConnectionParameter()` (`Options='-c
  default_transaction_read_only=on'`) automatically.
- **`TiDbDialect : MySqlDialect`**: inherits `MySqlDialect.ApplicationNameSettingName`
  automatically.

None of these four need any dialect-level changes on this axis.

## The six databases with a real, unexamined gap

All six extend `SqlDialect` directly and currently override none of
`ApplicationNameSettingName`, `PoolingSettingName`/`SupportsExternalPooling`,
`ReadOnlyPoolDiscriminatorSettingName`, or `GetReadOnlyConnectionParameter()` — confirmed via
`grep` across `pengdows.crud/dialects/*.cs`.

### FlatFile — pengdows' own driver, fully confirmed, no live test needed at all

Source: `pengdows.flatfile/FlatFileConnectionStringBuilder.cs` (this repo's sibling project,
cloned locally this session).

| Capability | Real keyword | Status |
|---|---|---|
| Application Name | `applicationName` | **Confirmed** — recorded into the per-location write-lock file for diagnostics (`FlatFileConnectionStringBuilder.ApplicationName`'s own doc comment). |
| Pooling | none — not a meaningful concept | This is a custom, in-process, file-based provider with no network handshake and no real connection pool; architecturally identical to why `DuckDbDialect.SupportsExternalPooling => false`. |
| Read-only | `readonly=true` (connection string) | **Confirmed, hard-enforced** — "any mutating statement (DML/DDL) is rejected immediately" per the doc comment. This is the *ideal* case: connection-string-level, no round trip, no session SQL needed. |

`FlatFileDialect.cs` currently uses **none** of this. Recommended fix (not yet applied —
research only, per instruction): `ApplicationNameSettingName => "applicationName"`,
`GetReadOnlyConnectionParameter() => "readonly=true"`, `SupportsExternalPooling => false`. Because
`ApplicationNameSettingName` would be set, the reader/writer pool-key split comes for free — no
`ReadOnlyPoolDiscriminatorSettingName` needed.

### Db2 (`IBM.Data.Db2.DB2ConnectionStringBuilder`, confirmed via `net.ibm.data.db2` 8.0.0.400) — RESOLVED (2026-09-18, live against a real `ibmcom/db2:11.5.8.0` container)

| Capability | Real keyword | Status |
|---|---|---|
| Application Name | `ClientApplicationName` | **Applied.** Confirmed in the connection-string builder. (`ProgramName` also exists as a second, lower-level candidate — `ClientApplicationName` is the one that surfaces in Db2's own connection-monitoring views, e.g. `SYSIBMADM.APPLICATIONS`.) |
| Pooling | `Pooling` (bool), `Max Pool Size` (int), `Min Pool Size` (int) | **Applied — and a real, separate bug found and fixed.** `Db2Dialect.MaxPoolSizeSettingName`/`MinPoolSizeSettingName` were never set at all (inherited the base `null`), so `PoolingConfigReader` silently ignored ANY explicit `Max Pool Size` a caller wrote into their own Db2 connection string and always used the dialect default — a real, previously-undiscovered gap distinct from the `MaxPoolSize=0` question below. Fixed via TDD (`Db2DialectTests.cs`). |
| `MaxPoolSize=0` default — is it "unbounded" or "literally zero"? | N/A | **RESOLVED, but the bigger finding subsumes it**: `pengdows.crud`'s own `ResolveEffectiveMaxPoolSize` (in `DatabaseContext.Initialization.cs`) already special-cases a connection-string `MaxPoolSize=0` as "fall back to dialect default" generically, dialect-agnostically — this was already safe before today's fix, once `MaxPoolSizeSettingName` is wired up at all. Separately, live-confirmed the driver's `Max Pool Size` setting **does not actually enforce anything** as a client-side cap on physical connections at ANY value (0/5/100/unset all behaved identically — 150 concurrent opens succeeded in ~0.01s even with `Max Pool Size=5` explicitly set; DB2's own `SYSIBMADM.APPLICATIONS` admin view confirmed 313 genuine concurrent server-side sessions while those connections were held open, ruling out a client-side illusion). `pengdows.crud`'s own `PoolGovernor` (semaphore-based, fully in-process) is Db2's ONLY real safety net — more so than for drivers that also enforce their own cap as defense-in-depth. |
| Read-only (connection string) | none found | Confirmed no dedicated keyword among the full property list. The only candidate, `IsReadOnly`, is `DbConnectionStringBuilder`'s own base-class "is this builder locked" indicator (present on every ADO.NET builder) — not a Db2-specific property; the compiler refuses assigning to it. |
| Read-only (session SQL) | `SET TRANSACTION READ ONLY` — **REJECTED, does not exist** | Live-tested and confirmed to be a flat SYNTAX ERROR: `SQL0104N An unexpected token "READ ONLY" was found following "SET TRANSACTION". Expected tokens may include: "<space>".` Db2 LUW's ANSI conformance does NOT extend to this statement, contradicting the original plausibility assumption. |

**Conclusion**: no database-level read-only enforcement mechanism exists for Db2 reachable via this driver — `GetReadOnlyConnectionParameter()` stays at the base `null`/no-op, matching the same conclusion independently reached for Sybase ASE the same day (see that section below). `pengdows.crud`'s own `ReadWriteMode.ReadOnly` pre-flight check still applies regardless. See `Db2Dialect.cs`'s own comments for the full dated trail.

### SAP HANA (`Sap.Data.Hana.HanaConnectionStringBuilder`, confirmed via `sap.data.hana.net.v8.0` 2.29.27)

| Capability | Real keyword | Status |
|---|---|---|
| Application Name | none found | Not present among 62 inspected properties. HANA may support this only via a session-level `SET` statement (e.g. an `APPLICATION`-style session variable) — **not yet live-verified**. |
| Pooling | `Pooling` (bool), `MaxPoolSize` (int), `MinPoolSize` (int) | **Confirmed**, full min/max sizing support. **Default values: `Pooling=True`, `MinPoolSize=0`, `MaxPoolSize=100`.** |
| Read-only (connection string) | none found | No dedicated keyword. |
| Read-only (session SQL) | `SET TRANSACTION READ ONLY` — **not yet live-verified** | HANA has broad ANSI SQL support; plausible but unconfirmed. |

Because there is no `ApplicationName` keyword, this dialect **needs a
`ReadOnlyPoolDiscriminatorSettingName` fallback** (the same class of fix Access got via `Jet
OLEDB:Database Locking Mode=1`) to avoid collapsing its reader/writer pools. No safe, confirmed-
inert candidate keyword has been identified yet for HANA — needs either documentation research or
a live connection to test candidates the way Access's was verified (open, measure, confirm
behaviorally inert).

### Informix (`Informix.Net.Core.IfxConnectionStringBuilder`, confirmed via `informix.net.core-lnx` 4.1501.2.2026) — FULLY RESOLVED 2026-09-19 (discriminator gap closed)

**LIVE-VERIFIED (2026-09-18)** against a real `icr.io/informix/informix-developer-database`
container (the prior pass here was reflection-only against the package DLL; this pass actually
connected and ran SQL). The live builder dump found 51 properties, not 45 — the earlier
reflection pass likely missed the "1"/"2"-suffixed HDR/RSS-failover variants that a fresh,
never-configured instance still exposes identically.

| Capability | Real keyword | Status |
|---|---|---|
| Application Name | none found | Confirmed absent, live: 51 properties inspected on `IfxConnectionStringBuilder`, no `ApplicationName`-shaped keyword among them. |
| Pooling | `Pooling` (bool), `MaxPoolSize`/`MaxPoolSize1` (int), `MinPoolSize`/`MinPoolSize1` (int) | **Confirmed**, live defaults match the earlier reflection-only pass exactly: `Pooling=True`, `MinPoolSize`/`MinPoolSize1=0`, `MaxPoolSize`/`MaxPoolSize1=100`. |
| Read-only (connection string) | none found | No dedicated keyword. |
| Read-only (session SQL) | `SET TRANSACTION READ ONLY` | **CONFIRMED LIVE, real enforcement.** Requires being inside an active transaction first (`BEGIN WORK`, or a real ADO.NET `conn.BeginTransaction()` — both tested) — issued standalone it fails with `"Not in transaction."`, which is not a rejection of the statement itself. Inside a transaction it's accepted, and a subsequent write then fails with `"Invalid operation for a READ-ONLY transaction."` Implemented via `InformixDialect.TryEnterReadOnlyTransaction`/`TryEnterReadOnlyTransactionAsync` (the same `TryExecuteReadOnlySql` shared helper `OracleDialect` uses), not `GetReadOnlyConnectionParameter()` — this is transaction-scoped SQL, not a connection-string property. |

**Discriminator: RESOLVED 2026-09-19 (live, real Docker container, follow-up pass).** The prior
pass correctly declined `Optofc=1`/`DelimIdent=true` (neither confirmed behaviorally inert — see
below for why that bar matters). This pass systematically dumped every property's own compiled-in
default off a live-connected `IfxConnectionStringBuilder`, then live-tested which ones can be set
explicitly to their OWN default value without error (the `InterBaseDialect` "fetch size=200"
pattern — guaranteed inert by construction, not just observed to look unchanged). Three qualify:
`Exclusive=no`, `MaxPoolSize=100`, and `LeaveTrailingSpaces=False` — all connect successfully with
their default value explicitly set. **`LeaveTrailingSpaces=False` was chosen** (a CHAR-column
trailing-space read-behavior flag): `MaxPoolSize`, despite also qualifying on paper, was
deliberately rejected because `ConnectionPoolingConfiguration.ApplyPoolDiscriminator` skips
setting the discriminator key when the caller's own connection string already contains it — and
`MaxPoolSize` is exactly the kind of property a real caller is plausible to have already
configured themselves, which would silently defeat pool separation in precisely the case where a
caller has customized their own pooling. `LeaveTrailingSpaces` is obscure enough that no real
caller is expected to ever set it. Implemented as
`InformixDialect.ReadOnlyPoolDiscriminatorSettingName => "LeaveTrailingSpaces"` /
`ReadOnlyPoolDiscriminatorSettingValue => "False"`.

Original finding, preserved for context: no `ApplicationName` keyword exists on
`IfxConnectionStringBuilder` at all (confirmed absent, live, across all 51 properties) — this is
why a discriminator (not `ApplicationNameSettingName`) was the right mechanism here in the first
place. The earlier candidates `Optofc=1`/`DelimIdent=true` were rejected because "connects without
error" is not the same bar as "confirmed behaviorally inert," which is exactly the distinction
Access's original `SupportsExternalPooling`/`PoolingSettingName` bug blurred.

### InterBase (`InterBaseSql.Data.InterBaseClient.IBConnectionStringBuilder`, confirmed via `interbasesql.data.interbaseclient` 10.0.3) — RESOLVED 2026-09-18 (live, real Docker container)

| Capability | Real keyword | Status |
|---|---|---|
| Application Name | none found | Not present among 33 inspected properties (re-confirmed live this pass). |
| Pooling | `Pooling` (bool), `MaxPoolSize` (int), `MinPoolSize` (int) | **Confirmed.** **Default values: `Pooling=True`, `MinPoolSize=0`, `MaxPoolSize=100`.** |
| Discriminator | **`fetch size` = `200`** | **APPLIED.** CONFIRMED LIVE (real container): `fetch size` is a real, recognized keyword — a connection opens successfully with it explicitly set — and `200` is `FetchSize`'s own compiled-in default, so setting it explicitly is guaranteed behaviorally inert while differentiating the pool key text. Implemented as `InterBaseDialect.ReadOnlyPoolDiscriminatorSettingName`/`Value`. |
| Read-only (connection string) | none found | Re-confirmed live: the `IsReadOnly` property visible on the builder is `DbConnectionStringBuilder`'s own inherited base property (no setter, no effect on `ConnectionString` text) — not a real InterBase keyword. |
| Read-only (transaction level) | `IBTransactionOptions { TransactionBehavior = IBTransactionBehavior.Read \| Concurrency \| Wait }` via `IBConnection.BeginTransaction(IBTransactionOptions)` | **CONFIRMED LIVE the capability exists and works** (a write inside such a transaction throws `IBException: attempted update during read-only transaction`; reads succeed normally) — but **NOT integrated into pengdows.crud**. The originally-guessed `SET TRANSACTION READ ONLY` mid-transaction SQL statement (Oracle's approach) was tried and CONFIRMED to fail (`IBException: invalid transaction handle (expecting explicit transaction start)`) — InterBase's TPB-based transaction model requires the read-only flag at transaction-creation time, not as a follow-up statement, which doesn't fit `ISqlDialect.TryEnterReadOnlyTransaction`'s hook (runs after `TransactionContext` already opened the transaction via the ordinary `IsolationLevel`-based overload — see `TransactionContext.cs`'s private constructor). Implementing this for real requires a new pengdows.crud extension point letting a dialect override transaction *creation* itself, not just post-begin SQL — a genuine feature request, left open rather than forcing a broken partial implementation. See `InterBaseDialect.cs`'s file-level AI SUMMARY for the full trail. |

### Sybase ASE (`AdoNetCore.AseClient`) — the public builder is a thin wrapper; real keywords live in `AdoNetCore.AseClient.Internal.ConnectionParameters`

The public `AseConnectionStringBuilder` exposes zero usable properties beyond the
`DbConnectionStringBuilder` base — this driver parses connection strings loosely rather than via
a typed builder. Its real internal parser (`ConnectionParameters`/`IConnectionParameters`)
confirms:

| Capability | Real keyword | Status |
|---|---|---|
| Application Name | `ApplicationName` (string) | **Confirmed**, via the internal type. |
| Pooling | `Pooling` (bool), `MaxPoolSize`/`MinPoolSize` (`Int16`) | **CORRECTED** (this claim was wrong in the original pass — re-verified via direct reflection with default-value construction, not just a property-name scan, which is what missed it the first time): both exist on `ConnectionParameters`, same as every other driver here. **Default values: `Pooling=True`, `MinPoolSize=0`, `MaxPoolSize=100`** — matching the common convention, not an outlier. `ApplicationName` also confirmed to default to the current process name when unset. |
| Read-only (connection string) | none found | Confirmed via the same reflection pass — no read-only/intent/mode-named property anywhere on `ConnectionParameters`. |
| Read-only (session SQL) | **RESOLVED (2026-09-18, live against a real ASE 16.0 container)** — no usable per-connection mechanism exists | `SET TRANSACTION READ ONLY` is a flat SYNTAX ERROR on ASE (`"Incorrect syntax near the keyword 'READ'."`) — it doesn't even parse, worse than SQL Server's `ApplicationIntent=ReadOnly` (which at least parses as a non-enforcing hint). `EXEC sp_dboption <db>, 'read only', true` (run from `master`, then a `CHECKPOINT` against the target db) IS real, confirmed-live enforcement — a subsequent write anywhere in that database fails with `"Attempt to BEGIN TRANSACTION in database '<db>' failed because database is READ ONLY."` But it's a coarse, DATABASE-WIDE administrative toggle, not connection/session-scoped — wiring it into `GetReadOnlyConnectionParameter()` would make the writer connection unable to write too. Deliberately NOT implemented; `SybaseDialect.GetReadOnlyConnectionParameter()` stays at the base `null`. See `SybaseDialect.cs`'s file-level AI SUMMARY and `SybaseDialectTests.GetReadOnlyConnectionParameter_ReturnsNull_NoUsablePerConnectionMechanismExists`. |

Because `ApplicationName` is confirmed, no separate pool discriminator is needed once it's wired
up.

## Summary table

| Database | App Name keyword | Pooling on/off | Min/Max pool size | Default MinPoolSize / MaxPoolSize | Read-only (conn string) | Read-only (session, unverified) | Discriminator needed? |
|---|---|---|---|---|---|---|---|
| FlatFile | `applicationName` ✓ | n/a (no real pool concept) | n/a | n/a | `readonly=true` ✓ hard-enforced | — | No |
| Db2 | `ClientApplicationName` ✓ APPLIED | `Pooling` ✓ (default `True`) | `Min Pool Size`/`Max Pool Size` ✓ APPLIED (setting name was never wired up — real bug, now fixed) | `0` / `0` — driver doesn't enforce it at ANY value, live-confirmed | none | `SET TRANSACTION READ ONLY` — **REJECTED live, syntax error** | No |
| Sybase ASE | `ApplicationName` ✓ (internal type) | `Pooling` ✓ (default `True`) | `MinPoolSize`/`MaxPoolSize` ✓ (corrected — see below) | `0` / `100` | none | **RESOLVED**: `SET TRANSACTION READ ONLY` is a syntax error; `sp_dboption 'read only'` is real but database-wide, not usable here | No |
| SAP HANA | none | `Pooling` ✓ (default `True`) | `MinPoolSize`/`MaxPoolSize` ✓ | `0` / `100` | none | `SET TRANSACTION READ ONLY` (plausible) | **Yes — no safe candidate found yet** |
| Informix | none | `Pooling` ✓ (default `True`) | `MinPoolSize`/`MaxPoolSize` ✓ (+ secondary-endpoint `1`-suffixed variants) | `0` / `100` | none | `SET TRANSACTION READ ONLY` ✓ **CONFIRMED LIVE (2026-09-18), implemented** | **Applied — `LeaveTrailingSpaces=False`** |
| InterBase | none | `Pooling` ✓ (default `True`) | `MinPoolSize`/`MaxPoolSize` ✓ | `0` / `100` | none | Confirmed works via `IBTransactionOptions` (not `SET TRANSACTION READ ONLY`) but NOT wired into pengdows.crud — needs a new extension point | **Applied — `fetch size=200`** |

**On default pool sizes**: all default values above were read directly off a freshly-constructed
builder instance with no connection string set — the driver's actual compiled-in default, not a
guess. Four of five (HANA, Informix, InterBase, and implicitly Sybase which has no size property
at all) default to `MinPoolSize=0`/`MaxPoolSize=100`, matching the common ADO.NET-ecosystem
convention (SqlClient/Npgsql/MySqlConnector all default the same way) — and matching
pengdows.crud's own `SqlDialect.FallbackMaxPoolSize = 100` fallback used when a value can't be
discovered from the connection string. **Db2 is the outlier**: its default is `MaxPoolSize=0`,
not `100`. This needs live confirmation before drawing a conclusion — `0` most plausibly means
"no explicit .NET-side cap, defer to the provider's own internal default" rather than literally
zero pooled connections (since `Pooling=True` is still the default), but that interpretation is
unverified. If confirmed, `Db2Dialect` may need its own `DefaultMaxPoolSize` override rather than
inheriting `SqlDialect`'s `100` fallback, since assuming `100` when the driver's own default is
functionally different would be exactly the same class of unverified-assumption gap Access had.

## Next steps (Db2, Sybase ASE, InterBase, and Informix resolved 2026-09-18 — see their sections above; only HANA remains open)

1. Live-verify the remaining "plausible, not yet live-verified" session-SQL read-only claim
   (HANA) against a real server. (Db2 and Sybase ASE both REJECTED outright as syntax errors,
   contradicting the original ANSI-plausibility assumption for both. InterBase's read-only claim
   is CONFIRMED LIVE to work at the driver level, but NOT wired into pengdows.crud. Informix's is
   CONFIRMED LIVE and implemented. See each database's section above for the full trail.)
2. ~~Live-verify (or find documentation for) a genuinely inert, driver-recognized discriminator
   keyword for HANA and Informix~~ **Informix done (2026-09-19) — `LeaveTrailingSpaces=False`,
   matching its own driver default. HANA remains open.**
3. ~~Confirm what Db2's `MaxPoolSize=0` default actually means in practice~~ **Done (2026-09-18)
   — see the Db2 section above: pengdows.crud's own generic 0-handling logic was already safe,
   but the driver itself doesn't enforce ANY `MaxPoolSize` value as a real cap regardless (150
   concurrent opens succeeded with `Max Pool Size=5` explicitly set) — pengdows.crud's in-process
   `PoolGovernor` is Db2's sole real admission-control safety net. A real, separate
   `MaxPoolSizeSettingName`/`MinPoolSizeSettingName` wiring bug was found and fixed along the way
   (neither was ever set, so any caller-supplied `Max Pool Size` was silently ignored).**
4. Once verified, apply the dialect overrides via TDD, one database at a time, mirroring exactly
   how `AccessDialect.cs`/`Db2Dialect.cs`/`SybaseDialect.cs`/`InterBaseDialect.cs`/
   `InformixDialect.cs` were fixed this session.
5. Design a real pengdows.crud extension point for "a dialect needs to control how a transaction
   itself is created (not just what SQL runs after it begins)" — InterBase's confirmed-working
   `IBTransactionOptions`-based read-only transaction is blocked on this, not on missing research.
