# Source-Verified API Supplements

This page records public capabilities that are easy to miss because they are implemented on
concrete gateway/context types or as extension methods rather than highlighted in the main
three-tier API pages.

**SQL container fluent helpers and gateway/context diagnostics** (formerly documented here only)
now have a proper discoverable home: see
[`sql-container-composition.md`](./sql-container-composition.md) for `AppendName`/`AppendParam`/
fragment helpers, execution-overload selection, `BuildWhereByPrimaryKey`/`ClearCaches`, and pool/
session diagnostics.

## UUID7 byte formats

In addition to `NewUuid7()` and `TryNewUuid7(...)`, `Uuid7Optimized` provides:

- `NewUuid7Bytes(Span<byte>)`: writes 16 bytes in .NET `Guid` mixed-endian order.
- `NewUuid7RfcBytes(Span<byte>)`: writes 16 bytes in RFC/network big-endian order.

UUID7 monotonicity is per thread, not process-wide or cross-machine.

## `DataReaderMapper`: a separate, general-purpose mapper

`IDataReaderMapper`/`DataReaderMapper` (`DataReaderMapper.Instance`) are public and externally
usable — see [`data-reader-mapper.md`](./data-reader-mapper.md). They are **not** what
`TableGateway`/`PrimaryKeyTableGateway` use for entity hydration (that's a separate,
attribute-driven compiled-plan path, `GetOrBuildRecordsetPlan`/`MapReaderToObjectWithPlan`) — use
`DataReaderMapper` specifically for mapping an arbitrary SQL result (e.g. a stored procedure with
no corresponding entity) to any POCO by column-name matching.

`TypeCoercionOptions.TimePolicy` is read internally (gates `DateTime`→`DateTimeOffset` coercion)
but is not externally configurable through the normal gateway/context path — `BaseTableGateway`/
`SqlContainer` only ever override `TypeCoercionOptions.Provider`. `TypeCoercionOptions.JsonPreference`
was removed (2026-08-30) after confirming it was fully dead — never read anywhere, not even
internally.

Advanced value conversion is type-driven through the built-in coercion pipeline. Use the real
attributes in `pengdows.crud.attributes`—such as `[Json]`, `[Version]`, `[EnumColumn]`,
`[NonInsertable]`, and `[NonUpdateable]`—for mapping and persistence policies.
