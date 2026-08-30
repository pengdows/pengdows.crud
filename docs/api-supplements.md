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

## Declared but not wired features

`IDataReaderMapper`/`MapperOptions` are not externally usable because the only implementation is
internal, and gateway hydration uses a separate compiled-plan path. `TypeCoercionOptions.JsonPreference`
is not read; `TimePolicy` is not externally configurable through the normal gateway/context path.

Advanced value conversion is type-driven through the built-in coercion pipeline. Use the real
attributes in `pengdows.crud.attributes`—such as `[Json]`, `[Version]`, `[EnumColumn]`,
`[NonInsertable]`, and `[NonUpdateable]`—for mapping and persistence policies.
