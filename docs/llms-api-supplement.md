# LLM API Supplement

For the machine-readable entry point, also read [`api-supplements.md`](api-supplements.md).

The current public surface includes fluent `SqlContainerExtensions` helpers (`AppendName`,
`AppendParam`, SQL fragment helpers, and `ExecuteReaderSingleRowAsync`),
`BaseTableGateway.BuildWhereByPrimaryKey`, `ClearCaches`, context isolation/session inspection,
pool snapshots, queue-depth controls, duplicate-context enforcement, and UUID7 output in both
.NET `Guid` and RFC/network byte order.

`IDataReaderMapper`/`DataReaderMapper.Instance` (public, `docs/data-reader-mapper.md`) are a
separate, general-purpose mapper for arbitrary SQL results with no corresponding entity — not
what `TableGateway`/`PrimaryKeyTableGateway` use for entity hydration. `TypeCoercionOptions.JsonPreference`
no longer exists (removed as dead code, 2026-08-30); `TimePolicy` is read internally but not
externally configurable through the normal gateway/context path.
