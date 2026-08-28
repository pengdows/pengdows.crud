# Source-Verified API Supplements

This page records public capabilities that are easy to miss because they are implemented on
concrete gateway/context types or as extension methods rather than highlighted in the main
three-tier API pages.

## SQL container helpers

`SqlContainerExtensions` adds fluent helpers for custom SQL:

- `AppendQuery(string)` appends literal SQL text.
- `AppendName(string)` and `AppendName(alias, name)` quote identifiers through the active dialect.
- `AppendParam(...)` appends a dialect-formatted placeholder without adding a parameter.
- `AppendWhere()`, `AppendAnd()`, `AppendEquals()`, `AppendIn()`, `AppendComma()`, and
  `AppendCloseParen()` append common SQL fragments.
- `ExecuteReaderSingleRowAsync(...)` requests `CommandBehavior.SingleRow` where supported.

`AppendQuery` is deliberately an escape hatch: values must still be parameters and identifiers
should use `AppendName` or `WrapObjectName`.

## Gateway and context diagnostics

`BaseTableGateway<TEntity>.BuildWhereByPrimaryKey(...)` adds a parameterized natural-key
predicate to an existing container. `ClearCaches()` clears compiled reader plans, column lists,
query templates, and cached WHERE parameter names after a schema or mapping change.

`IDatabaseContext` exposes `GetSupportedIsolationLevels()`, `GetBaseSessionSettings()`, and
`GetReadOnlySessionSettings()` for diagnostics and provider-specific integration checks.

`GetPoolStatisticsSnapshot(PoolLabel)` reports slot usage, queue depth, turnstile waits, timeouts,
cancellations, and whether a pool is disabled or forbidden. `DatabaseContext` also exposes
connection-created/reused/failure counters and `ConnectionPoolEfficiency`.

`MaxQueuedReads` and `MaxQueuedWrites` independently bound admission queues. `0` disables
queueing for that role; `null` uses the governor default. `EnforceUniqueConnectionString` upgrades
duplicate live contexts sharing a pool from a warning into a construction-time exception.

## UUID7 byte formats

In addition to `NewUuid7()` and `TryNewUuid7(...)`, `Uuid7Optimized` provides:

- `NewUuid7Bytes(Span<byte>)`: writes 16 bytes in .NET `Guid` mixed-endian order.
- `NewUuid7RfcBytes(Span<byte>)`: writes 16 bytes in RFC/network big-endian order.

UUID7 monotonicity is per thread, not process-wide or cross-machine.

## Declared but not wired features

`IDataReaderMapper`/`MapperOptions` are not externally usable because the only implementation is
internal, and gateway hydration uses a separate compiled-plan path. `TypeCoercionOptions.JsonPreference`
is not read; `TimePolicy` is not externally configurable through the normal gateway/context path.

Advanced value conversion is type-driven through the built-in coercion pipeline. Use the real
attributes in `pengdows.crud.attributes`—such as `[Json]`, `[Version]`, `[EnumColumn]`,
`[NonInsertable]`, and `[NonUpdateable]`—for mapping and persistence policies.
