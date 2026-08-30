# Streaming Queries (`LoadStreamAsync` / `RetrieveStreamAsync`)

Both gateway families expose streaming counterparts to their list-returning methods:

```csharp
IAsyncEnumerable<TEntity> LoadStreamAsync(ISqlContainer sc);
IAsyncEnumerable<TEntity> LoadStreamAsync(ISqlContainer sc, CancellationToken cancellationToken);
IAsyncEnumerable<TEntity> RetrieveStreamAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null,
    CancellationToken cancellationToken = default); // TableGateway<TEntity,TRowID> only
```

Use these instead of `LoadListAsync`/`RetrieveAsync` when you want to process a large result set
row-by-row without materializing the whole thing into a `List<TEntity>` first — memory use stays
proportional to one row at a time, not to the row count.

## How it's implemented (why the semantics below follow from it)

`LoadStreamAsync` (`BaseTableGateway.Core.cs`) is a plain `async IAsyncEnumerable<TEntity>` C#
iterator method — no custom buffering, no prefetch queue:

```csharp
public async IAsyncEnumerable<TEntity> LoadStreamAsync(ISqlContainer sc,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    await using var reader = await sc.ExecuteReaderAsync(CommandType.Text, cancellationToken)
        .ConfigureAwait(false);
    HybridRecordsetPlan? plan = null;
    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
    {
        plan ??= GetOrBuildRecordsetPlan(reader);
        var obj = MapReaderToObjectWithPlan(reader, plan);
        if (obj != null) yield return obj;
    }
}
```

`RetrieveStreamAsync` (`TableGateway.Core.cs`) builds its own container internally
(`GetRetrieveContainer`, disposed via `await using`) and delegates to `LoadStreamAsync`.

Every behavior below is a direct consequence of this being an ordinary C# async iterator, not a
special streaming subsystem with its own lifecycle rules.

## Query-per-enumeration: not repeatable in the "cached results" sense

Because `LoadStreamAsync`/`RetrieveStreamAsync` are iterator methods, the query does not run when
you call the method — it runs when you start enumerating the returned `IAsyncEnumerable<TEntity>`
(the first `MoveNextAsync()` call). Calling `GetAsyncEnumerator()` on the **same** returned
`IAsyncEnumerable<TEntity>` a second time (e.g. two separate `await foreach` loops over one stored
reference) starts a **new** reader and re-executes the query from scratch — it does not replay
cached rows from the first pass. "Repeatable" here means the operation is safe and correct to
re-enumerate, not that results are cached across enumerations:

```csharp
var stream = gateway.LoadStreamAsync(sc); // nothing has executed yet

await foreach (var row in stream) { /* first pass: query runs once */ }
await foreach (var row in stream) { /* second pass: query runs AGAIN, from row 1 */ }
```

If you need the results more than once, materialize with `LoadListAsync` instead, or explicitly
cache the streamed output yourself.

## Cancellation

Only the overload that declares `[EnumeratorCancellation] CancellationToken cancellationToken` as
a parameter actually threads an external token into the iterator body. `RetrieveStreamAsync`
always has this. `LoadStreamAsync` has two overloads:

- `LoadStreamAsync(sc)` — no cancellation parameter; internally passes `CancellationToken.None` to
  `ExecuteReaderAsync`/`ReadAsync`. Calling `.WithCancellation(token)` on the `IAsyncEnumerable`
  this returns has **no effect** — without a `[EnumeratorCancellation]` parameter on the iterator
  method itself, `WithCancellation`'s token is never delivered into the method body. Use the other
  overload if you need real cancellation.
- `LoadStreamAsync(sc, cancellationToken)` — the token is captured directly into the async state
  machine; an ordinary `await foreach (var row in gateway.LoadStreamAsync(sc, token))` already
  honors it, no `.WithCancellation()` needed.

A cancellation observed mid-stream propagates as `OperationCanceledException` unwrapped from
`ReadAsync`/`ExecuteReaderAsync` — matching this project's general exception-hierarchy convention
that cancellation is never translated into a `DatabaseException`.

## Early termination and cleanup

`await using var reader = ...` inside the iterator method is a real `await using`, not just
syntax that happens to compile — when a consumer breaks out of `await foreach` before reaching the
end of the stream, the compiler-generated async iterator's `DisposeAsync()` runs the pending
`await using`, which disposes the reader exactly as if you'd read to EOF:

```csharp
await foreach (var row in gateway.LoadStreamAsync(sc))
{
    if (ShouldStop(row)) break; // reader is still disposed correctly — no leak
    Process(row);
}
```

This gives the same reader-ownership guarantee documented in
[`connection/ownership-and-shutdown.md`](./connection/ownership-and-shutdown.md)'s `ITrackedReader`
section (reader, command, connection/permit, and lock layers all released) — streaming doesn't
relax or bypass that contract, it just reaches it via the iterator's own disposal path instead of
an explicit `reader.Dispose()` call.

## Transaction use

There is nothing streaming-specific about using these methods inside a `TransactionContext` —
pass the transaction as the container's context like any other operation. The consequence that
*does* matter: per
[`transactions.md`](./transactions.md)'s concurrency contract, an open reader holds the
transaction's internal lock for as long as it stays open. Streaming inside a transaction means
**the transaction is locked against every other operation — including `CommitAsync`,
`RollbackAsync`, and any other command — for the entire time you're enumerating the stream**, not
just for the initial query execution. Fully consume (or explicitly dispose) the stream before
issuing any other operation on that transaction, exactly as you would for a plain
`ExecuteReaderAsync` result.

## List vs. stream

| | `LoadListAsync`/`RetrieveAsync` | `LoadStreamAsync`/`RetrieveStreamAsync` |
|---|---|---|
| Memory | Whole result set materialized into a `List<TEntity>` | One row's worth of state at a time |
| When the query runs | Immediately (`await` returns after full materialization) | Deferred until enumeration starts |
| Re-use | The returned `List<TEntity>` can be iterated any number of times for free | Each enumeration re-executes the query (see above) |
| Best for | Small-to-moderate result sets, or when you need random access / multiple passes | Large result sets, one-pass processing, or exporting/piping rows without holding them all in memory |
| Transaction interaction | Reader closes before the method returns | Reader — and the transaction's lock, if used inside one — stays open for the whole enumeration |
