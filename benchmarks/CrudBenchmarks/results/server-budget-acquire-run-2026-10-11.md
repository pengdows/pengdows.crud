# Per-server budget acquire cost, 2026-10-11

> The benchmarks and the budget they measure live on the local branch `pool-003-shared-budget` (commit `cdf00222` and after);
> POOL-003 phase 1 was withdrawn from 2.0.6 (see `docs/FUTURE_WORK.md`).

POOL-003 phase 1 (`cdf00222`). BenchmarkDotNet 0.14.0, net10.0, Ryzen 9 5950X (8 logical cores used), 3 warmup and 12
measured iterations, machine otherwise quiet. Commands:

```text
dotnet run -c Release --framework net10.0 --project benchmarks/CrudBenchmarks -- --filter '*ServerBudgetAcquireBenchmarks*'
dotnet run -c Release --framework net10.0 --project benchmarks/CrudBenchmarks -- --filter '*ServerBudgetContendedAcquireBenchmarks*'
```

## Uncontended: one caller, one 8-slot governor (`ServerBudgetAcquireBenchmarks`)

| Path | No budget | Budget registered, disengaged | Budget engaged |
|---|---:|---:|---:|
| Blocking | 24.95 ns | 25.30 ns (+0.35 ns, +1.4%) | 38.33 ns (+13.4 ns, +54%) |
| Async | 30.74 ns | 32.12 ns (+1.4 ns, +4.5%) | 44.34 ns (+13.6 ns, +44%) |

Allocation is 48 B in every row (unchanged: the budget allocates nothing per lease). The disengaged cost is the one
volatile read; the engaged cost is one more gate acquire and release (about 13 ns).

## Contended: 8 threads, each on its own governor, all drawing on one engaged budget (`ServerBudgetContendedAcquireBenchmarks`)

Wall-clock time per operation over all threads' operations (400,000 per invocation); nothing ever queues.

| Variant | Mean | Per thread-operation (x8) |
|---|---:|---:|
| No budget (8 independent governors) | 6.42 ns | about 51 ns |
| Engaged budget (8 governors, one budget) | 145.7 ns | about 1,170 ns |

**This misses the acceptance bar** recorded in `docs/FUTURE_WORK.md` (POOL-003): engaged and contended is about 23x
slower than independent governors. The cause is the shared gate's release path, which takes a lock every time
(`PoolGovernorConcurrencyGate.ReleaseUnderLock`), so all threads' releases serialize on one lock and one cache line.
It is a tight loop with no work between acquires, the worst case. In absolute terms the engaged budget caps throughput
at about 6.9 million acquires per second across the whole process, against operations that cost 100 microseconds or more
on a client-server database (the only place the budget engages), so it is about 0.15% of a 100 microsecond call. It is
still a regression this change introduced and the fix is known (below), not something to wave away.

## What would close it

A release that takes no lock when nobody is waiting: decrement the active count, increment the available count, then
re-check the waiter count and only take the lock to promote a waiter if there is one (the re-check after publishing is
what prevents a lost wakeup). This changes the gate every governor uses, so it needs the gate's race tests and a fresh
run of `PoolGovernorAcquireBenchmarks` (it may also speed up the existing uncontended path, which pays an uncontended
lock on every release today). Not done: decision pending.
