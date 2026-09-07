# Adding a Database Provider

This is the end-to-end checklist for adding a database engine to `pengdows.crud`. A provider is
complete only when the runtime, provider package, detection, connection lifecycle, SQL behavior,
testbed, and documentation agree.

## Definition of done

- Add the engine to `SupportedDatabase` and detect it from live metadata and the provider factory.
- Register a dedicated dialect in `SqlDialectFactory`.
- Make explicit decisions for quoting, parameters, types, isolation, session hygiene, generated
  keys, upsert, paging, procedures, read-only behavior, pooling, and exception classification.
- Add provider package/native bootstrap wiring to the smallest testbed/integration project.
- Add dialect unit tests, detection/factory tests, telemetry tests, and live provider tests.
- Add a reproducible Docker container and always-on testbed registration unless the engine is
  genuinely cloud-only or has no usable image.
- Update support, capability, session, exception, generated-key, procedure, type, coverage,
  testbed, telemetry, and release documentation.

## Research before coding

Record supported engine versions, ADO.NET provider/factory/assembly, native dependencies, RID and
license constraints; identifier quoting and case folding; parameter markers, duplicate markers,
prepare/null/precision/scale behavior; integer/decimal/text/unicode/binary/Boolean/GUID/date-time/
JSON/array/interval/spatial types; transactions, isolation, snapshots, savepoints, read-only and
failed-write behavior; generated-key and same-connection semantics; upsert, batch, paging, CTE,
window and version-gated features; procedure/function, OUT/INOUT and return-value syntax; pooled
session state that must be reset; provider error codes/SQLSTATEs; pooling, DDL metadata invalidation,
idle unload, and native startup requirements.

For SAP HANA, verify these independently rather than copying Db2 or PostgreSQL: snapshot/isolation
mapping, `LIMIT`/`OFFSET`, identity/sequence and generated-key syntax, parameter behavior, session
reset statements, procedure calls, and the available HANA container or external test harness.

## Runtime source wiring

1. `pengdows.crud.abstractions/enums/SupportedDatabase.cs`: append a unique flags value; preserve
   existing numeric values.
2. `pengdows.crud/pengdows.crud/dialects/<Name>Dialect.cs`: implement explicit behavior; derive from
   another dialect only where compatibility is proven.
3. `pengdows.crud/pengdows.crud/dialects/SqlDialectFactory.cs`: add `CreateDialectForType` case.
4. `pengdows.crud/pengdows.crud/internal/DatabaseDetectionService.cs`: add schema-name and
   factory-type tokens and any flavor probe.
5. `pengdows.crud/pengdows.crud/dialects/SqlDialect.cs`: inspect product inference and every
   `DatabaseType ==`, `DatabaseType !=`, and `switch (DatabaseType)` branch, especially generated
   keys, first-row lookup, paging, read-only SQL, and special SQL.
6. `pengdows.crud/pengdows.crud/DatabaseContext.Initialization.cs`: check topology detection. Client/
   server-vs-embedded classification, in-memory detection, and `DbMode` coercion (including what
   `DbMode.Best` resolves to) are all owned by `ISqlDialect.IsClientServerDatabase` /
   `DetectInMemoryKind` / `CoerceConnectionMode` now (base defaults: client-server, never in-memory,
   honor any explicit mode and resolve `Best` to `Standard`) — no separate switch to update here.
   Override only for a genuinely restricted engine (embedded single-writer: SQLite/DuckDB; a forced
   topology-specific mode: SQL Server LocalDB).

The dialect must explicitly decide standard compliance and capabilities; quoting and qualification;
markers, named parameters, limits, names, prepare and cloning; isolation profiles, savepoints,
read-only, explicit-isolation rejection and failed-write rollback; base/read-only/final session
settings; identity/returning/wrapped statements/last-ID/generated-key plan; merge/on-conflict/
on-duplicate/batching; paging and first-row syntax; procedures; GUID/date-time/decimal/binary/text
coercion; pooling/read-only connection-string rewriting/application name/DDL pool reset; and
exception analysis plus typed translator classification.

## Provider package and native setup

Normally add the ADO.NET package to `testbed/testbed.csproj` and/or integration tests, not the core
library. Check net8/net10 compatibility, factory initialization, connection-string builder,
transitive native assets, dynamic provider loading, package/no-vendor checks, and signing/packing.
If native initialization is order-sensitive, add a bootstrap like
`testbed/Db2/Db2NativeLibraryBootstrap.cs` and call it before factory discovery.

## Tests

Add `pengdows.crud.Tests/dialects/<Name>DialectTests.cs` covering non-default SQL, capabilities,
quoting, markers, limits, cloning, types, isolation, session SQL, keys, upsert, paging, savepoints,
procedures, versions, detection, and exceptions. Add factory/detection tests preventing fallback to
`Sql92Dialect`, plus telemetry `db.system.name` coverage.

Add `testbed/<Name>/<Name>TestContainer.cs` and `<Name>TestProvider.cs`, plus targeted live tests for
startup/detection, fresh and reused pooled sessions, transactions/isolation/savepoints, generated
IDs, binding and type round trips, upsert, procedures/OUT/INOUT/return values, typed exceptions,
read-only behavior, connection health, and DDL cleanup/pool reset. A feature is not supported until
live evidence proves it; unsupported features need an explicit capability and documented reason.

## Docker and testbed

The container must define image/tag matrix, credentials/license/environment, ports, readiness and
provider-specific retries, mapped-port connection string, initialization, and disposal. Update
`testbed/ParallelTestOrchestrator.cs` in `CreateContainerAsync`, `GetTestConfigurations`, startup
weight, and `TestbedImageMatrix.Defaults`; preserve image overrides and filters. Update every shared
DDL/type/capability switch in `testbed/TestProvider.cs`, and inspect `TestHierarchicalIdRetrieval.cs`,
`DatabaseSchemaHelper`, and provider discovery. Docker-capable databases are always-on.

## Documentation and gates

Update `docs/supported-databases.md`, `docs/planning/db-coverage-ledger.md`, `docs/session-settings.md`,
`docs/exception-analysis.md`, `docs/generated-keys.md`, `docs/stored-procedures.md`,
`docs/advanced-types.md`, `testbed/readme.md`, `DbSystemNameMapper.cs` and its tests, README/package
claims, and skill references if `check-skill-drift.sh` requires it.

Run from `pengdows.crud`: build the solution, unit tests, integration tests, and
`dotnet run -c Release --project testbed -- --only <NewDatabase>`, then the full testbed and package,
no-vendor, and API checks. Record exact image, provider package, engine version, OS/RID, results,
and remaining gaps in the coverage ledger.
