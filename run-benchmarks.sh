#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Docker-dependent benchmarks (SQL Server, PostgreSQL) are opt-in and excluded by default.
# To include them: CRUD_BENCH_INCLUDE_OPT_IN=1 ./run-benchmarks.sh
dotnet run -c Release --project "${root}/benchmarks/CrudBenchmarks" -- -j short --filter '*'
