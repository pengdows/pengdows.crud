#!/usr/bin/env bash
# A/B benchmark: one harness (this working tree's benchmarks/CrudBenchmarks), two library refs.
#
# Each arm is a clean git worktree at its ref with the CURRENT harness overlaid, so methodology is
# identical and only pengdows.crud differs. The baseline arm gets a BASELINE_2X compile define
# (guards 3.0-only APIs such as DatabaseContextConfiguration.MaxQueuedWrites).
#
# Usage: benchmarks/run-main-vs-3.0.sh <tier1|tier2|tier3> [rounds]
#   tier1  no DB: hydration, equal-footing SQLite, Dapper apples-to-apples, guid, broken-mapping
#   tier2  embedded concurrency: SQLite write contention/concurrency, pool protection, DuckDB
#   tier3  Docker/Testcontainers: PostgreSQL + SQL Server equal-footing and hydration
# Env: BASE_REF (origin/main) NEW_REF (HEAD) JOB (unset; --job ADDS to class-defined jobs, use only for smoke tests) WORK (/tmp/crud-bench-ab) BASE_NAME (main) NEW_NAME (3.0)
# Arms alternate order each round (A B, then B A, ...) to expose position bias.
set -euo pipefail

TIER="${1:?usage: $0 <tier1|tier2|tier3> [rounds]}"
ROUNDS="${2:-2}"
BASE_REF="${BASE_REF:-origin/main}"
NEW_REF="${NEW_REF:-HEAD}"
BASE_NAME="${BASE_NAME:-main}"
NEW_NAME="${NEW_NAME:-3.0}"
JOB="${JOB:-}"   # empty = use each class's own [SimpleJob] config (the intended methodology)
WORK="${WORK:-/tmp/crud-bench-ab}"

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/.." && pwd)"
harness="$here/CrudBenchmarks"
out="$WORK/out/$TIER"

case "$TIER" in
  tier1) filters=('*.HydrationHotPathBenchmarks.*' '*EqualFootingCrudBenchmarks*' '*ApplesToApplesDapperBenchmarks*' '*BenchmarkGuidParameters*' '*BrokenMappingBenchmarks*') ;;
  tier2) filters=('*SQLiteWriteContentionBenchmarks*' '*SqliteConcurrencyBenchmark*' '*ConnectionPoolProtectionBenchmarks*' '*DuckDbEqualFootingBenchmarks*' '*DuckDbReadBenchmarks*') ;;
  tier3) filters=('*PostgreSqlEqualFootingBenchmarks*' '*SqlServerEqualFootingBenchmarks*' '*SqlServerHydrationHotPathBenchmarks*') ;;
  *) echo "unknown tier: $TIER" >&2; exit 2 ;;
esac

if [ -n "${FILTER:-}" ]; then read -r -a filters <<<"$FILTER"; fi   # override, e.g. FILTER='*ApplesToApples*'

mkdir -p "$WORK" "$out"

prepare_arm() { # name ref baseline(0/1)
  local name="$1" ref="$2" baseline="$3" wt="$WORK/wt-$1"
  git -C "$repo" worktree prune
  if [ -d "$wt" ]; then git -C "$repo" worktree remove --force "$wt"; fi
  git -C "$repo" worktree add --detach "$wt" "$ref" >/dev/null
  cp "$repo/global.json" "$wt/global.json"   # same SDK/compiler for both arms
  rm -rf "$wt/benchmarks/CrudBenchmarks"
  mkdir -p "$wt/benchmarks/CrudBenchmarks"
  # Overlay the canonical harness (no build output, no old artifacts/results).
  (cd "$harness" && tar --exclude=bin --exclude=obj --exclude=BenchmarkDotNet.Artifacts --exclude=results -cf - .) \
    | (cd "$wt/benchmarks/CrudBenchmarks" && tar -xf -)
  if [ "$baseline" = 1 ]; then
    sed -i 's|<Nullable>enable</Nullable>|<Nullable>enable</Nullable>\n        <DefineConstants>$(DefineConstants);BASELINE_2X</DefineConstants>|' \
      "$wt/benchmarks/CrudBenchmarks/CrudBenchmarks.csproj"
  fi
  echo "building arm $name @ $(git -C "$wt" rev-parse --short HEAD)"
  dotnet build "$wt/benchmarks/CrudBenchmarks/CrudBenchmarks.csproj" -c Release -v q -nologo >"$WORK/build-$name.log" 2>&1 \
    || { tail -20 "$WORK/build-$name.log"; echo "BUILD FAILED for $name" >&2; exit 1; }
}

run_arm() { # name round
  local name="$1" round="$2" dir="$out/$1/round$2" proj="$WORK/wt-$1/benchmarks/CrudBenchmarks"
  mkdir -p "$dir"
  echo "=== $TIER $name round $round ($(date +%H:%M:%S)) ==="
  # Out-of-process BenchmarkDotNet locates the csproj by searching up from the CWD, so run from
  # inside the arm's harness dir and collect its artifacts afterwards.
  rm -rf "$proj/BenchmarkDotNet.Artifacts"
  local jobargs=(); [ -n "$JOB" ] && jobargs=(--job "$JOB")
  (cd "$proj" && dotnet run -c Release --no-build -- \
      --filter "${filters[@]}" "${jobargs[@]}" --exporters json) >"$dir/console.log" 2>&1 \
    || echo "WARN: $name round $round exited non-zero (see $dir/console.log)" >&2
  [ -d "$proj/BenchmarkDotNet.Artifacts" ] && mv "$proj/BenchmarkDotNet.Artifacts" "$dir/BenchmarkDotNet.Artifacts"
  return 0
}

{
  echo "date: $(date -Is)"
  echo "tier: $TIER  job: $JOB  rounds: $ROUNDS"
  echo "base: $BASE_NAME = $(git -C "$repo" rev-parse "$BASE_REF")"
  echo "new:  $NEW_NAME = $(git -C "$repo" rev-parse "$NEW_REF")"
  echo "sdk(repo global.json): $(dotnet --version)  runtime: $(dotnet --list-runtimes | grep 'NETCore.App 8' | tail -1)"
  echo "cpu: $(grep -m1 'model name' /proc/cpuinfo | cut -d: -f2) x$(nproc)"
  echo "governor: $(cat /sys/devices/system/cpu/cpu0/cpufreq/scaling_governor 2>/dev/null || echo n/a)"
  echo "loadavg-at-start: $(cut -d' ' -f1-3 /proc/loadavg)"
} | tee "$out/env.txt"

prepare_arm "$BASE_NAME" "$BASE_REF" 1
prepare_arm "$NEW_NAME" "$NEW_REF" 0

for r in $(seq 1 "$ROUNDS"); do
  if [ $((r % 2)) -eq 1 ]; then first="$BASE_NAME"; second="$NEW_NAME"; else first="$NEW_NAME"; second="$BASE_NAME"; fi
  run_arm "$first" "$r"
  run_arm "$second" "$r"
done

echo "done. results: $out"
echo "summarize: python3 $here/summarize-ab.py $out $BASE_NAME $NEW_NAME"
