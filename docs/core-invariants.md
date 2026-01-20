# Core Invariants and Gotchas (v1.1)

This is a compact, high-signal guide for maintainers and AI assistants. It captures the rules that, if broken, create correctness bugs or misleading documentation.

## Non-Negotiable Invariants

1. **DatabaseContext is a singleton per connection string.**
   - Do: register one instance per unique connection string.
   - Don't: use scoped or transient lifetimes.

2. **Transactions are operation-scoped.**
   - Do: `using var tx = context.BeginTransaction();` inside the operation.
   - Don't: inject or store TransactionContext as a field.

3. **Context lock is always NoOp.**
   - Serialization happens at the connection lock or transaction user lock.
   - If you add a context lock, you will serialize unrelated work and risk deadlocks.

4. **Connection lock is the real guard.**
   - Shared connections (SingleWriter/SingleConnection) must serialize through the connection lock.
   - Ephemeral connections (Standard/KeepAlive) use a NoOp lock for zero overhead.

5. **ITrackedReader is a lease, not just a wrapper.**
   - It pins the connection and holds the connection lock until disposal.
   - Long-lived readers = blocked writers in SingleWriter/SingleConnection modes.

6. **MetricsUpdated events must never re-enter DatabaseContext.**
   - Events are fired without locks by design.
   - Re-entrancy can deadlock or corrupt expectations.

7. **DbMode.Best can coerce unsafe choices.**
   - SQLite/DuckDB `:memory:` must be SingleConnection.
   - SQLite file defaults to SingleWriter (WAL-friendly).
   - SQL Server LocalDB coerces to KeepAlive.

8. **fakeDb is control-flow, not database semantics.**
   - It simulates provider behavior, not SQL execution or constraints.
   - Integration tests remain required for real database behavior.

## Common Failure Modes (Symptoms → Likely Cause)

- **SQLITE_BUSY or data not shared across requests** → scoped/transient DatabaseContext or wrong DbMode.
- **Throughput collapse under load** → reader not disposed (connection lock held).
- **Deadlocks in MetricsUpdated handler** → handler re-enters DatabaseContext.
- **Unexpected serialization in Standard mode** → added locks in context-level code.

## Design Boundaries to Preserve

- **DatabaseContext is orchestration, not state.** Mostly immutable configuration + atomic metrics; avoid adding shared mutable state (ProcWrappingStyle is a rare exception).
- **TransactionContext pins a single connection for its lifetime.** Its user lock serializes operations; the connection lock is still acquired per operation/reader.
- **SqlContainer owns SQL + parameters; it does not track entity state.**
- **Dialect selection is immutable post-initialization.**

## Safe Change Checklist

- **Concurrency**: Does this change introduce new shared state? If yes, how is it synchronized?
- **Locks**: Are you acquiring locks in a new order? Ensure no cycles.
- **Reader lifetime**: Any new reader APIs must be explicit about disposal/lease behavior.
- **DbMode**: If behavior depends on provider, confirm it with coercion rules and tests.
- **fakeDb**: If adding features that fakeDb can't simulate, extend fakeDb first.

## Pointers for Deeper Context

- `1.1/ARCHITECTURE.md` for the full locking model and lifecycle rationale.
- `1.1/CONNECTION-MODES.md` and `1.1/docs/connection-management-guide.md` for mode details.
- `1.1/pengdows.crud/wrappers/TrackedReader.cs` for reader-as-lease behavior.
