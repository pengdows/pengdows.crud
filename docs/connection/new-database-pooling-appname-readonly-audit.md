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

### Db2 (`IBM.Data.Db2.DB2ConnectionStringBuilder`, confirmed via `net.ibm.data.db2` 8.0.0.400)

| Capability | Real keyword | Status |
|---|---|---|
| Application Name | `ClientApplicationName` | **Confirmed** in the connection-string builder. (`ProgramName` also exists as a second, lower-level candidate — `ClientApplicationName` is the one that surfaces in Db2's own connection-monitoring views, e.g. `SYSIBMADM.APPLICATIONS`.) |
| Pooling | `Pooling` (bool), `MaxPoolSize` (int), `MinPoolSize` (int) | **Confirmed**, full min/max sizing support, not just an on/off switch. **Default values (fresh builder, no connection string set): `Pooling=True`, `MinPoolSize=0`, `MaxPoolSize=0`.** `MaxPoolSize=0` is the one outlier among all six databases here — every other driver defaults to `100`. `0` most likely means "no explicit cap on the .NET builder side, provider decides internally" rather than literally zero connections, but this needs live confirmation before trusting it — don't assume it behaves like the others' `100` default. |
| Read-only (connection string) | none found | No dedicated keyword among 89 inspected properties. |
| Read-only (session SQL) | `SET TRANSACTION READ ONLY` — **not yet live-verified** | Standard ANSI SQL:1999; Db2 LUW is known for strong SQL-standard conformance, so this is plausible but unconfirmed against a real server. |

Recommendation once live-verified: prefer the connection string if a property is found on a
closer look at IBM's own docs (none found here); otherwise the session-SQL fallback is
acceptable per the stated priority (connection string > session > nothing). Since
`ApplicationNameSettingName` would be set, no separate pool discriminator is needed.

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
| Read-only (connection string) | none found | No dedicated keyword. |
| Read-only (session SQL) | uncertain — **not yet live-verified** | Sybase ASE is Transact-SQL family (closer to SQL Server than to ANSI-conformant engines like Db2/HANA/Informix); SQL Server itself has no real connection-string or session-level read-only enforcement (`ApplicationIntent=ReadOnly` is documented as a routing hint only, not enforcement) — ASE may be in the same position. This needs live confirmation more than any of the others; don't assume the ANSI `SET TRANSACTION READ ONLY` pattern transfers here. |

Because `ApplicationName` is confirmed, no separate pool discriminator is needed once it's wired
up.

## Summary table

| Database | App Name keyword | Pooling on/off | Min/Max pool size | Default MinPoolSize / MaxPoolSize | Read-only (conn string) | Read-only (session, unverified) | Discriminator needed? |
|---|---|---|---|---|---|---|---|
| FlatFile | `applicationName` ✓ | n/a (no real pool concept) | n/a | n/a | `readonly=true` ✓ hard-enforced | — | No |
| Db2 | `ClientApplicationName` ✓ | `Pooling` ✓ (default `True`) | `MinPoolSize`/`MaxPoolSize` ✓ | `0` / **`0`** ⚠️ outlier | none | `SET TRANSACTION READ ONLY` (plausible) | No |
| Sybase ASE | `ApplicationName` ✓ (internal type) | `Pooling` ✓ (default `True`) | `MinPoolSize`/`MaxPoolSize` ✓ (corrected — see below) | `0` / `100` | none | uncertain — may be SQL-Server-like (hint only) | No |
| SAP HANA | none | `Pooling` ✓ (default `True`) | `MinPoolSize`/`MaxPoolSize` ✓ | `0` / `100` | none | `SET TRANSACTION READ ONLY` (plausible) | **Yes — no safe candidate found yet** |
| Informix | none | `Pooling` ✓ (default `True`) | `MinPoolSize`/`MaxPoolSize` ✓ (+ secondary-endpoint `1`-suffixed variants) | `0` / `100` | none | `SET TRANSACTION READ ONLY` (plausible) | **Yes — no safe candidate found yet** |
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

## Next steps (InterBase resolved 2026-09-18 — see its section above; the rest still open)

1. Live-verify the three remaining "plausible, not yet live-verified" session-SQL read-only
   claims (Db2, HANA, Informix) against real servers. (InterBase's read-only claim is now
   CONFIRMED LIVE to work at the driver level, but NOT wired into pengdows.crud — see its section
   above; this is a distinct, separately-tracked gap from "unverified.")
2. Live-verify (or find documentation for) a genuinely inert, driver-recognized discriminator
   keyword for HANA and Informix — the same way `Jet OLEDB:Database Locking Mode=1` was verified
   for Access and `fetch size=200` was verified for InterBase (open a connection with vs. without
   it, confirm identical behavior, confirm the connection strings differ; prefer a candidate that
   matches the driver's own compiled-in default over an arbitrary non-default knob, so the "inert"
   claim doesn't rest only on empirical observation).
3. Confirm Sybase ASE's read-only situation one way or the other — don't assume either the ANSI
   pattern or the SQL-Server-hint-only pattern without checking.
4. Confirm what Db2's `MaxPoolSize=0` default actually means in practice (unbounded? provider-
   internal default? something else?) — if it's not equivalent to `100`, `Db2Dialect` likely
   needs its own `DefaultMaxPoolSize` override instead of inheriting `SqlDialect`'s `100`
   fallback.
5. Once verified, apply the dialect overrides via TDD, one database at a time, mirroring exactly
   how `AccessDialect.cs`/`InterBaseDialect.cs` were fixed this session.
6. Design a real pengdows.crud extension point for "a dialect needs to control how a transaction
   itself is created (not just what SQL runs after it begins)" — InterBase's confirmed-working
   `IBTransactionOptions`-based read-only transaction is blocked on this, not on missing research.
