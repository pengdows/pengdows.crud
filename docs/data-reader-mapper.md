# DataReaderMapper: Mapping Arbitrary Results to Arbitrary POCOs

`TableGateway<TEntity, TRowID>`/`PrimaryKeyTableGateway<TEntity>` hydrate entities through an
attribute-driven, compiled-expression-tree mapper (`[Table]`/`[Column]`/`[Id]`/`[PrimaryKey]`) —
see [`entity-mapping.md`](./entity-mapping.md). That path assumes the result set corresponds to a
real, mappable table or view. `IDataReaderMapper`/`DataReaderMapper` is a separate, general-purpose
mapper for everything else: **any `ITrackedReader`/`DbDataReader` result, hydrated into any POCO,
by column-name matching, with no attributes required.**

The canonical use case: a stored procedure whose result shape has no corresponding table —
`pengdows.poco.mint`'s schema inspection has nothing to generate a POCO from, since the shape only
exists as that procedure's output. `DataReaderMapper` lets you hand-write a plain class and hydrate
it directly from the reader the procedure call returns.

## Basic usage

```csharp
public class OrderSummary
{
    public int OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalDue { get; set; }
}

using var sc = context.CreateSqlContainer("get_order_summary");
sc.AddParameterWithValue("customerId", DbType.Int32, 42);
await using var reader = await sc.ExecuteReaderAsync(CommandType.StoredProcedure);

var summaries = await DataReaderMapper.LoadAsync<OrderSummary>(reader);
```

`OrderSummary` has no `[Table]`/`[Column]`/`[Id]` attributes at all — every public settable
property is matched to a reader column by name, case-insensitively (`StringComparer.OrdinalIgnoreCase`),
via plain reflection (`GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.SetProperty)`).
No entity registration, no `TypeMapRegistry` involvement.

Both a static, allocation-free entry point (`DataReaderMapper.LoadAsync<T>(...)`,
`DataReaderMapper.StreamAsync<T>(...)`) and a DI-friendly singleton instance
(`IDataReaderMapper mapper = DataReaderMapper.Instance;`) are available — same behavior, pick
whichever fits the call site. `DataReaderMapper`'s constructor is `internal`; `Instance` is the
only way to obtain one.

## API surface

```csharp
// Static, on DataReaderMapper directly:
ValueTask<List<T>> LoadObjectsFromDataReaderAsync<T>(ITrackedReader reader, CancellationToken ct = default);
ValueTask<List<T>> LoadAsync<T>(ITrackedReader reader, IMapperOptions? options = null, CancellationToken ct = default);
IAsyncEnumerable<T> StreamAsync<T>(ITrackedReader reader, IMapperOptions? options = null, CancellationToken ct = default);

// Instance, via IDataReaderMapper (DataReaderMapper.Instance):
ValueTask<List<T>> LoadAsync<T>(ITrackedReader reader, CancellationToken ct = default);
ValueTask<List<T>> LoadAsync<T>(ITrackedReader reader, IMapperOptions options, CancellationToken ct = default);
IAsyncEnumerable<T> StreamAsync<T>(ITrackedReader reader, IMapperOptions options, CancellationToken ct = default);
```

Every `T` must satisfy `class, new()` — a public parameterless constructor, matching the same
constraint every other hydration path in the library uses.

`StreamAsync` reads one row at a time without buffering the whole result into a `List<T>` first —
use it for potentially large ad-hoc result sets, same rationale as
[`streaming-queries.md`](./streaming-queries.md) covers for the entity path.

## `MapperOptions`

```csharp
public sealed record MapperOptions(
    bool Strict = false,
    bool ColumnsOnly = false,
    Func<string, string>? NamePolicy = null,
    EnumParseFailureMode EnumMode = EnumParseFailureMode.Throw) : IMapperOptions
{
    public static readonly MapperOptions Default = new();
}
```

| Option | Effect |
|---|---|
| `Strict` | Throws if a reader column has no matching property on `T`. Default `false`: unmatched columns are silently ignored. |
| `ColumnsOnly` | When `true`, `NamePolicy` is not applied — only exact/case-insensitive column-to-property name matches count. |
| `NamePolicy` | A `string -> string` transform applied to each reader column name before matching. The most common use: bridging a stored procedure's naming convention to C# property names — e.g. stripping underscores so `order_id` matches `OrderId` (`OrdinalIgnoreCase` means casing doesn't need to match, only the transformed string shape does). |
| `EnumMode` | `EnumParseFailureMode` — `Throw` (default), `SetDefaultValue`, or `SetNullAndLog` when a column value can't parse into a target enum property. `SetNullAndLog` logs via `TypeCoercionHelper.Logger` — see that type's XML doc for the process-wide (not per-tenant) scope of that logger. |

```csharp
// Stored proc returns snake_case columns; POCO uses PascalCase properties.
var options = new MapperOptions(NamePolicy: name => name.Replace("_", ""));
var rows = await DataReaderMapper.LoadAsync<OrderSummary>(reader, options);
```

## What this is *not*

- **Not a replacement for gateway hydration.** `TableGateway`/`PrimaryKeyTableGateway` never use
  `DataReaderMapper` — their own compiled-plan mechanism (`GetOrBuildRecordsetPlan`) is unrelated
  and stays attribute-driven. Use `DataReaderMapper` only for results that don't correspond to a
  registered entity.
- **Not schema-aware.** There's no validation that a mapped property's CLR type is compatible with
  the reader's actual column type beyond what `TypeCoercionHelper`'s normal coercion rules already
  handle for any hydration path in the library.
- **Not part of `pengdows.poco.mint`.** `pengdows.poco.mint` generates POCOs from *inspectable
  schema*; `DataReaderMapper` is the tool for results that have no schema to inspect in the first
  place (arbitrary stored procedures, ad-hoc projections).
