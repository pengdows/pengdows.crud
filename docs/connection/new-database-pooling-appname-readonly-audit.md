# Application Name / Pooling / Read-Only Audit — Databases Added Since 2.0.5 (`main`)

## Status: essentially complete

Of the six databases with a real, unexamined gap, five are now fully resolved (Access, Db2,
Sybase ASE, InterBase, SAP HANA all have a final answer for both the discriminator and read-only
questions, applied via TDD where a real mechanism existed). Only Informix's discriminator remains
genuinely open, and InterBase's confirmed-working read-only transaction support still needs a new
pengdows.crud extension point to actually wire in — see "Next steps" at the bottom.

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
"Next steps" below) — left open rather than forcing a broken partial fix.

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

**Firebird — a legacy database, NOT one of the "since main" databases this document was originally
scoped to — was found to have the identical class of gap and resolved the same day (2026-09-18,
live against a real `firebirdsql/firebird:5.0.2` container).** Prompted by a broader policy
statement (every database needs real pool separation, in priority order: connection-string
read-only > `ApplicationName`/similar > `Pooling=true` > session-SQL read-only fallback), a
repo-wide grep found `FirebirdDialect` was the only long-established, non-embedded database with
*zero* coverage on any of `ApplicationNameSettingName`/`GetReadOnlyConnectionParameter`/
`ReadOnlyPoolDiscriminatorSettingName`/`TryEnterReadOnlyTransaction` — its reader and writer
connections shared one physical pool with no database-level read-only enforcement at all. See its
own section below for the full findings (real `ApplicationName`, no connection-string read-only,
and a genuine-but-currently-unwireable TPB-level read-only transaction option — the same
extension-point blocker InterBase hit).

**SAP HANA's questions are now also resolved (2026-09-19, live against a real
`saplabs/hanaexpress` container)**: `SET TRANSACTION READ ONLY` genuinely works (a write attempted
afterward fails with `HanaException` NativeError 129), now implemented via
`HanaDialect.TryEnterReadOnlyTransaction`/`TryEnterReadOnlyTransactionAsync`. Its discriminator
gap is also fixed: `ConnectionTimeout=15` (the driver's own compiled-in default), applied via the
same "matches-the-default" pattern InterBase used, but only after discovering that testing
candidates through `HanaConnectionStringBuilder`'s own typed properties gives a MISLEADING
answer — that builder silently omits any property set to its default from the serialized
connection string, which would make a discriminator chosen that way invisible to the pool key.
The real production path (`ConnectionPoolingConfiguration.ApplyPoolDiscriminator`'s generic
`DbConnectionStringBuilder`) does include it correctly — see HANA's section below for the full
methodological note. A second hazard was found and fixed along the way: HANA's read-only flag is
STICKY at the session level (persists past `COMMIT`, affecting the next transaction on the same
pooled connection) — fixed via `HanaDialect.GetBaseSessionSettings()` issuing `"SET TRANSACTION
READ WRITE"` as a per-checkout reset.

The only remaining gap is wiring InterBase's (and Firebird's) already-confirmed-working read-only
transaction support into pengdows.crud, blocked on a real extension-point design, not missing
research — see "Next steps" at the bottom for the full detail. (Informix's pool discriminator gap
may be resolved by separate, concurrent work not reflected in this paragraph yet — check
`InformixDialect.cs`'s own file header for the current, authoritative state.)

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

### SAP HANA (`Sap.Data.Hana.HanaConnectionStringBuilder`, confirmed via `sap.data.hana.net.v8.0` 2.29.27) — RESOLVED 2026-09-19 (live, real saplabs/hanaexpress container)

| Capability | Real keyword | Status |
|---|---|---|
| Application Name | none found | Not present among 72 inspected properties (re-confirmed live this pass — the earlier reflection-only pass found 62). |
| Pooling | `Pooling` (bool), `MaxPoolSize` (int), `MinPoolSize` (int) | **Confirmed**, full min/max sizing support. **Default values: `Pooling=True`, `MinPoolSize=0`, `MaxPoolSize=100`.** |
| Discriminator | **`ConnectionTimeout` = `15`** | **APPLIED.** CONFIRMED LIVE via the actual production mechanism (`ConnectionPoolingConfiguration.ApplyPoolDiscriminator`'s generic `DbConnectionStringBuilder`, not the HANA-specific typed builder — see the note below on why that distinction mattered here). `15` is `ConnectionTimeout`'s own compiled-in default, so setting it explicitly is guaranteed behaviorally inert. Implemented as `HanaDialect.ReadOnlyPoolDiscriminatorSettingName`/`Value`. |
| Read-only (connection string) | none found | No dedicated keyword. |
| Read-only (transaction level) | `SET TRANSACTION READ ONLY` | **CONFIRMED LIVE, real enforcement, implemented.** A write attempted afterward fails with `HanaException` NativeError 129 (`"...please use \"SET TRANSACTION READ WRITE\" statement first"`); a read inside the same read-only transaction succeeds normally. Implemented via `HanaDialect.TryEnterReadOnlyTransaction`/`TryEnterReadOnlyTransactionAsync` (the same `TryExecuteReadOnlySql` shared helper Oracle/Informix use). **Unlike Db2 and Sybase ASE this same session, the ANSI pattern genuinely works here** — don't assume it transfers uniformly across dialects either way; verify each one live. |

**A genuine methodological pitfall, worth flagging for future discriminator research on any
dialect**: testing candidate properties by constructing `HanaConnectionStringBuilder` (the
HANA-specific typed builder) and reading back its own `.ConnectionString` is misleading — that
builder silently OMITS any property explicitly set to its own default value from the serialized
text (confirmed for every property tried: `Distribution`, `SplitBatchCommands`,
`ConnectDiagnosticInfo`, `Reconnect`, `NodeConnectTimeout`, `PacketSize`, `MaxPoolSize`,
`Prefetch`, `SSLSNIRequest`, `Locale`), which would make a discriminator chosen this way silently
ineffective — the pool key text would be byte-identical to the baseline. The ACTUAL production
mechanism, `ConnectionPoolingConfiguration.ApplyPoolDiscriminator`, uses a **generic**
`System.Data.Common.DbConnectionStringBuilder` instead (`builder[key] = value`), which has no
such canonicalization and DOES include the literal `key=value` text regardless of whether it
matches the driver's semantic default. Always test a candidate discriminator through the real
`ApplyPoolDiscriminator`-shaped mechanism, not just the target driver's own typed builder — the
two can give opposite answers to "does this actually change the connection string text?"

**A second genuine hazard found and fixed along the way, not originally in scope**: HANA's `SET
TRANSACTION READ ONLY` is STICKY at the session level — CONFIRMED LIVE that it persists past
`COMMIT` and affects the next transaction on the same physical connection (mark read-only, commit
with no write, then a fresh transaction with no explicit `SET` statement at all still rejects a
write with the identical NativeError 129). Left unaddressed, a pooled physical connection marked
read-only by one caller's transaction could reject an unrelated later caller's unrelated WRITE,
unpredictably. Fixed via `HanaDialect.GetBaseSessionSettings()` returning `"SET TRANSACTION READ
WRITE"` as a per-checkout reset (CONFIRMED LIVE safe to run as a bare preamble with no active
transaction, and confirmed to correctly restore write access after a stuck read-only commit).

### Informix (`Informix.Net.Core.IfxConnectionStringBuilder`, confirmed via `informix.net.core-lnx` 4.1501.2.2026)

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

**Discriminator: investigated, deliberately NOT implemented.** No `ApplicationName` keyword exists.
Two candidates were found and confirmed to connect successfully when added to a live connection
string — `Optofc=1` ("Optimize Open Cursor", a real CSDK cursor-handling switch) and
`DelimIdent=true` — but neither meets the bar `AccessDialect`'s `Jet OLEDB:Database Locking
Mode=1` or `OracleDialect`'s `Metadata Pooling=false` do: `DelimIdent` is already part of every
connection string this dialect builds (setting it again wouldn't differentiate reader vs. writer
pools at all), and `Optofc`'s actual behavioral effect (does it change real cursor semantics, or
is `1` already the driver's implicit default?) was not confirmed — "connects without error" is
not the same bar as "confirmed behaviorally inert," which is exactly the distinction Access's
original `SupportsExternalPooling`/`PoolingSettingName` bug blurred (a property that connects
fine is not automatically safe to use as a silent discriminator). `ReadOnlyPoolDiscriminatorSettingName`
stays at the `SqlDialect` base default (`null`) until a genuinely inert candidate is found —
reader and writer connections share one physical pool for this dialect for now.

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

### Firebird (`FirebirdSql.Data.FirebirdClient.FbConnectionStringBuilder`, confirmed via `10.3.3`) — LEGACY DATABASE, not part of the original "since main" scope, RESOLVED 2026-09-18 (live, real `firebirdsql/firebird:5.0.2` container)

Firebird predates every other database in this document — it's long-established and always part
of the default testbed matrix, not one of the databases added since `main`. It's included here
because a repo-wide grep (prompted by an explicit policy statement: every database needs real
read/write pool separation, in priority order — connection-string read-only > `ApplicationName`/
similar > `Pooling=true` > session-SQL read-only fallback) found it was the ONLY long-established,
non-embedded, client/server-capable dialect with zero coverage on any of the four mechanisms this
document tracks.

| Capability | Real keyword | Status |
|---|---|---|
| Application Name | `ApplicationName` | **APPLIED.** CONFIRMED LIVE: a real, working property — round-trips to `application name=...` in the connection string, and a live connection with it set succeeds normally. Implemented as `FirebirdDialect.ApplicationNameSettingName`. |
| Pooling | `Pooling` (bool), `MaxPoolSize`/`MinPoolSize` (int) | **Confirmed**, matches the `SqlDialect` base `"Pooling"` keyword exactly — no override needed. **Default values: `Pooling=True`, `MinPoolSize=0`, `MaxPoolSize=100`.** |
| Read-only (connection string) | none found | `FbConnectionStringBuilder.IsReadOnly` is confirmed (via `DeclaredOnly` reflection) to be the INHERITED base `System.Data.Common.DbConnectionStringBuilder.IsReadOnly` (no setter, no effect on `ConnectionString` text) — not a real Firebird keyword. Same trap InterBase's identically-named property hit. |
| Read-only (transaction level) | `FbTransactionOptions { TransactionBehavior = FbTransactionBehavior.Read \| Concurrency \| Wait }` via `FbConnection.BeginTransaction(FbTransactionOptions)` | **CONFIRMED LIVE the capability exists and works** (a write inside such a transaction throws `FbException: attempted update during read-only transaction`; reads succeed normally) — but **NOT integrated into pengdows.crud**, for the identical reason as InterBase: a mid-transaction `SET TRANSACTION READ ONLY` SQL statement was tried first and CONFIRMED to fail (`FbException: invalid transaction handle (expecting explicit transaction start)`) — Firebird's TPB model, like InterBase's, requires the read-only flag at transaction-*creation* time, which doesn't fit `ISqlDialect.TryEnterReadOnlyTransaction`'s post-begin hook. Blocked on the same new pengdows.crud extension point identified for InterBase (see "Next steps" item 5) — left open rather than forcing a broken partial implementation. |

Since `ApplicationName` is confirmed, no separate pool discriminator is needed.

## Summary table

| Database | App Name keyword | Pooling on/off | Min/Max pool size | Default MinPoolSize / MaxPoolSize | Read-only (conn string) | Read-only (session, unverified) | Discriminator needed? |
|---|---|---|---|---|---|---|---|
| FlatFile | `applicationName` ✓ | n/a (no real pool concept) | n/a | n/a | `readonly=true` ✓ hard-enforced | — | No |
| Db2 | `ClientApplicationName` ✓ APPLIED | `Pooling` ✓ (default `True`) | `Min Pool Size`/`Max Pool Size` ✓ APPLIED (setting name was never wired up — real bug, now fixed) | `0` / `0` — driver doesn't enforce it at ANY value, live-confirmed | none | `SET TRANSACTION READ ONLY` — **REJECTED live, syntax error** | No |
| Sybase ASE | `ApplicationName` ✓ (internal type) | `Pooling` ✓ (default `True`) | `MinPoolSize`/`MaxPoolSize` ✓ (corrected — see below) | `0` / `100` | none | **RESOLVED**: `SET TRANSACTION READ ONLY` is a syntax error; `sp_dboption 'read only'` is real but database-wide, not usable here | No |
| SAP HANA | none | `Pooling` ✓ (default `True`) | `MinPoolSize`/`MaxPoolSize` ✓ | `0` / `100` | none | `SET TRANSACTION READ ONLY` ✓ **CONFIRMED LIVE, implemented** | **Applied — `ConnectionTimeout=15`** |
| Informix | none | `Pooling` ✓ (default `True`) | `MinPoolSize`/`MaxPoolSize` ✓ (+ secondary-endpoint `1`-suffixed variants) | `0` / `100` | none | `SET TRANSACTION READ ONLY` ✓ **CONFIRMED LIVE (2026-09-18), implemented** | **Investigated, deliberately unimplemented — see Informix section (candidates connect but not confirmed inert)** |
| InterBase | none | `Pooling` ✓ (default `True`) | `MinPoolSize`/`MaxPoolSize` ✓ | `0` / `100` | none | Confirmed works via `IBTransactionOptions` (not `SET TRANSACTION READ ONLY`) but NOT wired into pengdows.crud — needs a new extension point | **Applied — `fetch size=200`** |
| Firebird (legacy, not "since main") | `ApplicationName` ✓ APPLIED | `Pooling` ✓ (default `True`, no override needed) | `MinPoolSize`/`MaxPoolSize` ✓ | `0` / `100` | none | Confirmed works via `FbTransactionOptions` (not `SET TRANSACTION READ ONLY`) but NOT wired into pengdows.crud — same new extension point as InterBase | No — `ApplicationName` already covers it |

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

## Next steps (Db2, Sybase ASE, InterBase, Informix, Firebird, and SAP HANA all resolved 2026-09-18/19 — see each section above; only one item remains genuinely open)

Every session-SQL read-only claim and every discriminator question originally listed here has now
been live-verified, one way or another:

- **Read-only**: Db2 and Sybase ASE both REJECTED the ANSI `SET TRANSACTION READ ONLY` pattern
  outright as a syntax error. InterBase, Firebird, Informix, and SAP HANA all confirmed it (or an
  equivalent) genuinely works — InterBase's and Firebird's are confirmed at the driver level but
  NOT yet wired into pengdows.crud (both blocked on the same extension-point item below);
  Informix's and HANA's are both confirmed live AND implemented.
- **Discriminators**: Access, InterBase, SAP HANA, and (per a separate, concurrent follow-up —
  check `InformixDialect.cs`'s own file header for the authoritative current state)
  possibly Informix now all have a confirmed-safe, applied discriminator
  (`Jet OLEDB:Database Locking Mode=1`, `fetch size=200`, `ConnectionTimeout=15` respectively).
  Db2, Sybase ASE, and Firebird didn't need one — each has a real `ApplicationName`-equivalent
  keyword.

Remaining work:

1. Design a real pengdows.crud extension point for "a dialect needs to control how a transaction
   itself is created (not just what SQL runs after it begins)" — InterBase's AND Firebird's
   confirmed-working TPB-based (`IBTransactionOptions`/`FbTransactionOptions`) read-only
   transactions are both blocked on this, not on missing research.
2. If Informix's discriminator search above did not resolve it, find a genuinely inert,
   driver-recognized discriminator keyword for Informix, or definitively establish none exists —
   the same bar `Jet OLEDB:Database Locking Mode=1`/`fetch size=200`/`ConnectionTimeout=15` met (a
   candidate whose value matches the driver's own compiled-in default is the strongest form of
   this; test it through `ConnectionPoolingConfiguration.ApplyPoolDiscriminator`'s actual
   generic-`DbConnectionStringBuilder` mechanism, not just the target driver's own typed builder —
   see SAP HANA's section above for why those two can give opposite answers to "does this actually
   change the connection string text?").
