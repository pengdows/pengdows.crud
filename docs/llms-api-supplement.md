# LLM API Supplement

For the machine-readable entry point, also read [`api-supplements.md`](api-supplements.md).

The current public surface includes fluent `SqlContainerExtensions` helpers (`AppendName`,
`AppendParam`, SQL fragment helpers, and `ExecuteReaderSingleRowAsync`),
`BaseTableGateway.BuildWhereByPrimaryKey`, `ClearCaches`, context isolation/session inspection,
pool snapshots, queue-depth controls, duplicate-context enforcement, and UUID7 output in both
.NET `Guid` and RFC/network byte order.

Do not describe `IDataReaderMapper`/`MapperOptions` or `TypeCoercionOptions.JsonPreference` as
supported runtime features: they are unreachable or inert in the current implementation.
