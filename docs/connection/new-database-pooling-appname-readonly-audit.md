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

The remaining gaps (four session-SQL read-only claims, three missing pool discriminators for
HANA/Informix/InterBase, and the Db2 `MaxPoolSize=0` question) still require live server
verification — see "Next steps" at the bottom for what's still open.

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

### Informix (`Informix.Net.Core.IfxConnectionStringBuilder`, confirmed via `informix.net.core-lnx` 4.1501.2.2026 — Linux-targeted package; loaded here via pure reflection only, not executed)

| Capability | Real keyword | Status |
|---|---|---|
| Application Name | none found | Not present among 45 inspected properties. |
| Pooling | `Pooling` (bool), `MaxPoolSize`/`MaxPoolSize1` (int), `MinPoolSize`/`MinPoolSize1` (int) | **Confirmed.** The "1"-suffixed variants are almost certainly for a secondary/failover server endpoint (Informix HDR/RSS pattern), not a second pool tier. **Default values: `Pooling=True`, `MinPoolSize`/`MinPoolSize1=0`, `MaxPoolSize`/`MaxPoolSize1=100`.** |
| Read-only (connection string) | none found | No dedicated keyword; `Exclusive`/`Exclusive1` exist but relate to exclusive access mode, not read-only. |
| Read-only (session SQL) | `SET TRANSACTION READ ONLY` or Informix's own `SET ISOLATION`/lock-mode statements — **not yet live-verified** | Plausible (Informix has standard-SQL transaction support) but unconfirmed. |

Same discriminator gap as HANA: no `ApplicationName` keyword means a
`ReadOnlyPoolDiscriminatorSettingName` fallback is needed, with no confirmed-safe candidate
identified yet.

### InterBase (`InterBaseSql.Data.InterBaseClient.IBConnectionStringBuilder`, confirmed via `interbasesql.data.interbaseclient` 10.0.3)

| Capability | Real keyword | Status |
|---|---|---|
| Application Name | none found | Not present among 28 inspected properties. |
| Pooling | `Pooling` (bool), `MaxPoolSize` (int), `MinPoolSize` (int) | **Confirmed.** **Default values: `Pooling=True`, `MinPoolSize=0`, `MaxPoolSize=100`.** |
| Read-only (connection string) | none found | No dedicated keyword. |
| Read-only (session/transaction level) | `SET TRANSACTION READ ONLY` — **not yet live-verified, but high confidence** | InterBase's direct descendant Firebird genuinely supports transaction-level `READ ONLY` as a core TPB (Transaction Parameter Block) option going back decades; InterBase, as Firebird's ancestor, almost certainly has the equivalent. Check whether `FirebirdDialect.cs` already implements something analogous to mirror the exact API shape (`ITransactionContext`-level, not connection-string). |

Same discriminator gap: no `ApplicationName` keyword, no confirmed-safe candidate identified yet.

### Sybase ASE (`AdoNetCore.AseClient`) — the public builder is a thin wrapper; real keywords live in `AdoNetCore.AseClient.Internal.ConnectionParameters`

The public `AseConnectionStringBuilder` exposes zero usable properties beyond the
`DbConnectionStringBuilder` base — this driver parses connection strings loosely rather than via
a typed builder. Its real internal parser (`ConnectionParameters`/`IConnectionParameters`)
confirms:

| Capability | Real keyword | Status |
|---|---|---|
| Application Name | `ApplicationName` (string) | **Confirmed**, via the internal type. |
| Pooling | `Pooling` (bool), `MaxPoolSize`/`MinPoolSize` (`Int16`) | **CORRECTED** (this claim was wrong in the original pass — re-verified via direct reflection with default-value construction, not just a property-name scan, which is what missed it the first time): both exist on `ConnectionParameters`, same as every other driver here. **Default values: `Pooling=True`, `MinPoolSize=0`, `MaxPoolSize=100`** — matching the common convention, not an outlier. `ApplicationName` also confirmed to default to the current process name when unset. |
| Read-only (connection string) | none found | No dedicated keyword. |
| Read-only (session SQL) | uncertain — **not yet live-verified** | Sybase ASE is Transact-SQL family (closer to SQL Server than to ANSI-conformant engines like Db2/HANA/Informix); SQL Server itself has no real connection-string or session-level read-only enforcement (`ApplicationIntent=ReadOnly` is documented as a routing hint only, not enforcement) — ASE may be in the same position. This needs live confirmation more than any of the others; don't assume the ANSI `SET TRANSACTION READ ONLY` pattern transfers here. |

Because `ApplicationName` is confirmed, no separate pool discriminator is needed once it's wired
up.

## Summary table

| Database | App Name keyword | Pooling on/off | Min/Max pool size | Default MinPoolSize / MaxPoolSize | Read-only (conn string) | Read-only (session, unverified) | Discriminator needed? |
|---|---|---|---|---|---|---|---|
| FlatFile | `applicationName` ✓ | n/a (no real pool concept) | n/a | n/a | `readonly=true` ✓ hard-enforced | — | No |
| Db2 | `ClientApplicationName` ✓ APPLIED | `Pooling` ✓ (default `True`) | `Min Pool Size`/`Max Pool Size` ✓ APPLIED (setting name was never wired up — real bug, now fixed) | `0` / `0` — driver doesn't enforce it at ANY value, live-confirmed | none | `SET TRANSACTION READ ONLY` — **REJECTED live, syntax error** | No |
| Sybase ASE | `ApplicationName` ✓ (internal type) | `Pooling` ✓ (default `True`) | `MinPoolSize`/`MaxPoolSize` ✓ (corrected — see below) | `0` / `100` | none | uncertain — may be SQL-Server-like (hint only) | No |
| SAP HANA | none | `Pooling` ✓ (default `True`) | `MinPoolSize`/`MaxPoolSize` ✓ | `0` / `100` | none | `SET TRANSACTION READ ONLY` (plausible) | **Yes — no safe candidate found yet** |
| Informix | none | `Pooling` ✓ (default `True`) | `MinPoolSize`/`MaxPoolSize` ✓ (+ secondary-endpoint `1`-suffixed variants) | `0` / `100` | none | `SET TRANSACTION READ ONLY` (plausible) | **Yes — no safe candidate found yet** |
| InterBase | none | `Pooling` ✓ (default `True`) | `MinPoolSize`/`MaxPoolSize` ✓ | `0` / `100` | none | `SET TRANSACTION READ ONLY` (high confidence, Firebird lineage) | **Yes — no safe candidate found yet** |

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

## Next steps (not yet done — research only, per instruction)

1. ~~Live-verify the four "plausible, not yet live-verified" session-SQL read-only claims (Db2,
   HANA, Informix, InterBase) against real servers.~~ **Db2 and Sybase ASE done (2026-09-18) —
   both rejected outright as syntax errors, contradicting the plausibility assumption. HANA and
   Informix remain open.**
2. Live-verify (or find documentation for) a genuinely inert, driver-recognized discriminator
   keyword for HANA, Informix, and InterBase — the same way `Jet OLEDB:Database Locking Mode=1`
   was verified for Access (open a connection with vs. without it, confirm identical behavior,
   confirm the connection strings differ).
3. ~~Confirm Sybase ASE's read-only situation one way or the other~~ **Done (2026-09-18) — see
   the Sybase ASE section above.**
4. ~~Confirm what Db2's `MaxPoolSize=0` default actually means in practice~~ **Done (2026-09-18)
   — see the Db2 section above: already handled safely by generic logic, but the driver doesn't
   enforce ANY MaxPoolSize value as a real cap regardless — pengdows.crud's own PoolGovernor is
   the sole real protection.** A real, separate `MaxPoolSizeSettingName` wiring bug was found and
   fixed along the way.
5. Once verified, apply the dialect overrides via TDD, one database at a time, mirroring exactly
   how `AccessDialect.cs` was fixed this session. **Db2 and Sybase ASE done; HANA and Informix
   remain.**
