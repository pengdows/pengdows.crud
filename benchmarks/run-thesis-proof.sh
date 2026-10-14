#!/bin/bash
# THESIS EVIDENCE BENCHMARK SUITE
# Runs the benchmarks that back the product thesis. Each reports what it measured; none is a proof:
# 1. Connection pool protection under a request storm
# 2. Broken mapping (a wrong read fails loudly or silently)
# 3. Equal-footing CRUD against Dapper and EF Core (SQLite in-memory)

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "========================================================================="
echo "PENGDOWS.CRUD THESIS PROOF BENCHMARK SUITE"
echo "========================================================================="
echo ""
echo "This suite runs:"
echo ""
echo "  1. ConnectionPoolProtectionBenchmarks - pool behavior under pressure"
echo "  2. BrokenMappingBenchmarks - what each library does with a wrong mapping"
echo "  3. EqualFootingCrudBenchmarks - CRUD time against Dapper and EF Core"
echo ""
echo "See benchmarks/README.md for the dated results and what each one does and does not show."
echo ""
echo "========================================================================="
echo ""
echo "Running benchmarks... (this will take ~10-15 minutes)"
echo ""

# Run all thesis-proof benchmarks
dotnet run -c Release -f "${BENCH_FRAMEWORK:-net10.0}" --project "${root}/CrudBenchmarks" -- --filter "*ConnectionPoolProtectionBenchmarks*" "*BrokenMappingBenchmarks*" "*EqualFootingCrudBenchmarks*"

echo ""
echo "========================================================================="
echo "BENCHMARK COMPLETE"
echo "========================================================================="
echo ""
echo "Read the results in BenchmarkDotNet.Artifacts/ against the dated numbers in benchmarks/README.md."
echo "Every ratio depends on the workload, the database and the hardware; quote none without its run."
echo ""
echo "========================================================================="
