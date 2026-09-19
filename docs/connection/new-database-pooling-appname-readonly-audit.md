# Application Name / Pooling / Read-Only Audit — Databases Added Since 2.0.5 (`main`)

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
| Pooling | `Pooling` (bool) only | **Confirmed** the switch exists, but **no `MinPoolSize`/`MaxPoolSize`-equivalent property was found anywhere in the assembly** — this driver may only support pooling as a binary on/off, with no configurable sizing. **Default value: `Pooling=True`** (also `LoginTimeout=15`, unrelated to pool sizing). |
| Read-only (connection string) | none found | No dedicated keyword. |
| Read-only (session SQL) | uncertain — **not yet live-verified** | Sybase ASE is Transact-SQL family (closer to SQL Server than to ANSI-conformant engines like Db2/HANA/Informix); SQL Server itself has no real connection-string or session-level read-only enforcement (`ApplicationIntent=ReadOnly` is documented as a routing hint only, not enforcement) — ASE may be in the same position. This needs live confirmation more than any of the others; don't assume the ANSI `SET TRANSACTION READ ONLY` pattern transfers here. |

Because `ApplicationName` is confirmed, no separate pool discriminator is needed once it's wired
up.

## Summary table

| Database | App Name keyword | Pooling on/off | Min/Max pool size | Default MinPoolSize / MaxPoolSize | Read-only (conn string) | Read-only (session, unverified) | Discriminator needed? |
|---|---|---|---|---|---|---|---|
| FlatFile | `applicationName` ✓ | n/a (no real pool concept) | n/a | n/a | `readonly=true` ✓ hard-enforced | — | No |
| Db2 | `ClientApplicationName` ✓ | `Pooling` ✓ (default `True`) | `MinPoolSize`/`MaxPoolSize` ✓ | `0` / **`0`** ⚠️ outlier | none | `SET TRANSACTION READ ONLY` (plausible) | No |
| Sybase ASE | `ApplicationName` ✓ (internal type) | `Pooling` ✓ (default `True`) | **none found** | n/a | none | uncertain — may be SQL-Server-like (hint only) | No |
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

1. Live-verify the four "plausible, not yet live-verified" session-SQL read-only claims (Db2,
   HANA, Informix, InterBase) against real servers.
2. Live-verify (or find documentation for) a genuinely inert, driver-recognized discriminator
   keyword for HANA, Informix, and InterBase — the same way `Jet OLEDB:Database Locking Mode=1`
   was verified for Access (open a connection with vs. without it, confirm identical behavior,
   confirm the connection strings differ).
3. Confirm Sybase ASE's read-only situation one way or the other — don't assume either the ANSI
   pattern or the SQL-Server-hint-only pattern without checking.
4. Confirm what Db2's `MaxPoolSize=0` default actually means in practice (unbounded? provider-
   internal default? something else?) — if it's not equivalent to `100`, `Db2Dialect` likely
   needs its own `DefaultMaxPoolSize` override instead of inheriting `SqlDialect`'s `100`
   fallback.
5. Once verified, apply the dialect overrides via TDD, one database at a time, mirroring exactly
   how `AccessDialect.cs` was fixed this session.
