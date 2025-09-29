# Advanced Type Support – Remaining Work

## Build blockers
- Converter base now defines `TryConvertFromProvider`; all concrete converters still override the old `ConvertFromProvider`. Update overrides to call/implement `TryConvertFromProvider` (see `types/converters/InetConverter.cs:19`, `CidrConverter.cs:19`, `RowVersionConverter.cs:14`, etc.) or revert the interface change.
- Spatial converters use tuples returning `ReadOnlySpan<byte>` (`types/converters/GeometryConverter.cs`, `GeographyConverter.cs`). Ref structs cannot flow through tuples; refactor helpers to return arrays or plain structs.
- `PostgreSqlIntervalConverter` has helper code declared outside the class (`types/converters/PostgreSqlIntervalConverter.cs:152`). Move it inside the class or into a static helper.

## Runtime integration
- `TypeCoercionOptions` now carries `Provider`, but several `TypeCoercionHelper.Coerce` call sites still pass the older signature (`SqlContainer.cs`, `DataReaderMapper.cs`, `EntityHelper.Reader.cs`). Ensure options propagate with the active dialect.
- `_coercionOptions` in `EntityHelper.Core` is updated during `Initialize`, but double-check other entry points/factory paths so the provider is always set before use.

## Follow-up
- Once the build is green, add unit tests covering the new value objects and converters (spatial, network, intervals, rowversion, streaming LOBs).
- Re-run `dotnet build` (and targeted tests) after the above fixes.
