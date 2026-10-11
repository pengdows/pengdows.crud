# 2.0.6 parameter-creation regression: found, fixed, re-measured against 2.0.5 (2026-10-11)

The tier-1 A/B (`ab-2.0.5-vs-2.0.6-tier1-2026-10-11.md`, Result 3) flagged two regressions to investigate before 2.0.6 ships. This is
what they were and what the fixes measured. Method for the "after" column: `benchmarks/run-main-vs-3.0.sh tier1 2` with
`BASE_REF=v2.0.5`, `FILTER='*BenchmarkGuidParameters* *ApplesToApplesDapperBenchmarks*'`, net10.0, two rounds, arms alternating,
the same harness in both arms (HEAD `e740432d`). `BenchmarkGuidParameters` is a short-run job: the same code swung by about
10 percentage points between runs on the Firebird rows, so read the time deltas as +/- 5 to 10%.

## Causes (all in the 2.0.6 parameter path, and all on every parameter, not only Guids)

1. **A 32 B closure in `DbTypeValidator.Mismatch` on every parameter.** An `Array.Exists(..., t => t.IsAssignableFrom(clrType) &&
   acceptable.Contains(t))` lambda (added for Stream/TextReader acceptance) captures `clrType` and `acceptable`; the compiler allocates
   its closure for the whole scope, even when the first check answers. Replaced by a loop. Measured on the optimized code: a string
   parameter 88 -> 56 B, int 112 -> 80 B, DateTime 136 -> 104 B, Guid 184 -> 120 B.
2. **`LargeObjectParameter.TryMaterialize(parameter, value)` boxed every value type** outside the primitive set (Guid, DateTime,
   DateTimeOffset, bool) to be told it is not a `Stream`. Skipped for value types.
3. **A value type boxed twice** (once for the mapped-type call, once for `PrepareParameterValue`). Boxed once and reused: Guid 120 -> 88 B,
   DateTime 104 -> 80 B.
4. **Firebird's override** called `IsZoned(object?)` first, which boxed the value and compared type-name strings on every parameter.
   Now decided once per `T`.
5. **Ten value-type tests per call** (RowVersion, DateOnly, TimeOnly, char, vectors, HierarchyId, Int128/UInt128) ran for every
   parameter, and for a reference-typed `T` each is a real type-handle compare (SQLite turns a Guid into a string and re-enters the
   base). Now skipped for a `T` that can never be one of them, decided once per `T`; the dialect-flag tests (virtual calls) follow
   the cheap type test instead of preceding it.

Tests: `GuidParameterAllocationTests` pins bytes per parameter (string 64, int 88, DateTime 88, Guid 96, Firebird binary 136 and
string 192 as ceilings) on the optimized code, and keeps the binary-Guid byte order and the Firebird zoned-value check.

## Result against 2.0.5 (HEAD `e740432d`)

| `BenchmarkGuidParameters` | 2.0.5 time | HEAD time | delta | 2.0.5 B | HEAD B | delta |
|---|---:|---:|---:|---:|---:|---:|
| SqlServer (pass-through) | 64.0 ns | 67.9 ns | +6.0% | 120 | 88 | -26.7% |
| DuckDB (string) | 79.3 ns | 81.7 ns | +3.0% | 216 | 184 | -14.8% |
| Oracle (string) | 80.8 ns | 87.6 ns | +8.4% (within noise) | 216 | 184 | -14.8% |
| SQLite (string) | 52.0 ns | 61.3 ns | +18.0% | 152 | 152 | 0% |
| SQLite, 1,000 Guid parameters | 65.6 us | 73.5 us | +12.0% | 232,000 | 232,000 | 0% |
| Firebird (binary) | 72.5 ns | 85.6 ns | +18.0% | 128 | 128 | 0% |
| Firebird (string) | 79.8 ns | 86.4 ns | +8.3% | 184 | 184 | 0% |

Before the fixes the same rows were +16% to +41% in time and +14% to +50% in bytes (Firebird binary 77.6 -> 109.8 ns, 128 -> 192 B).
**Allocation is now at or below 2.0.5 on every dialect. Time is back to within 3 to 18% of 2.0.5, which is 2 to 12 ns per parameter;
SQLite string and Firebird binary are the largest residuals.** A 100 microsecond database call is 0.01% of that. The residual is the
cost of the new capability checks (per-dialect type handling added in 2.0.6) and is not chased further.

## The second regression, `ReadSingle_Pengdows_PureMapping`: diagnosed, not changed

Still +56 B per read (1,871 -> 1,927 B, +3.0%), time within noise (+2.1%, spread 2.6%). A stage-by-stage benchmark on the real
SQLite provider, run in both arms of the A/B, found it: setting the parameter is identical (88 B); **opening and disposing the
reader is +80 B** (1,576 -> 1,656 B), and a full `LoadSingleAsync` +56 B (1,943 -> 1,999 B). The cause is the `TrackedReader`
object itself: 2.0.6 added about ten fields to it (the dialect's read-failure translator, the two Informix quirk flags, the
unreadable-stored-value predicate, the unresolved-column dialect and its flag, the coercion options, the lease and first-row
timestamps and the time provider, the context lock and the single-connection transaction gate), and every read allocates one.
They serve TYPE-005/016/008 and the reader metrics. The seven dialect-derived ones are constants per dialect and could sit behind
one shared per-dialect object (about 32 B saved of the 80); that touches a central class and its 58 test constructions for a 1.6%
allocation gain, so it is recorded here as an option, not done. For scale, Dapper allocates 1,839 B for the same read.
