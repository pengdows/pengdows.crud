#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

dotnet run -c Release --project "${root}/benchmarks/CrudBenchmarks" -- -j short --filter '*'
