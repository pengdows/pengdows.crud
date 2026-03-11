#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
bench_dir="${root}/benchmarks/CrudBenchmarks/bin/Release/net8.0"
bench_exe="${bench_dir}/CrudBenchmarks"

build_benchmarks() {
    dotnet build "${root}/benchmarks/CrudBenchmarks/CrudBenchmarks.csproj" -c Release
}

verify_benchmark_binaries() {
    local pairs=(
        "pengdows.crud.dll:${root}/pengdows.crud/bin/Release/net8.0/pengdows.crud.dll:${bench_dir}/pengdows.crud.dll"
        "pengdows.crud.abstractions.dll:${root}/pengdows.crud.abstractions/bin/Release/net8.0/pengdows.crud.abstractions.dll:${bench_dir}/pengdows.crud.abstractions.dll"
        "pengdows.crud.fakeDb.dll:${root}/pengdows.crud.fakeDb/bin/Release/net8.0/pengdows.crud.fakeDb.dll:${bench_dir}/pengdows.crud.fakeDb.dll"
        "pengdows.stormgate.dll:${root}/pengdows.stormgate/bin/Release/net8.0/pengdows.stormgate.dll:${bench_dir}/pengdows.stormgate.dll"
    )

    local entry name source target source_hash target_hash
    for entry in "${pairs[@]}"; do
        IFS=":" read -r name source target <<< "${entry}"

        if [[ ! -f "${source}" ]]; then
            echo "Missing source assembly: ${source}" >&2
            exit 1
        fi

        if [[ ! -f "${target}" ]]; then
            echo "Missing benchmark assembly: ${target}" >&2
            exit 1
        fi

        source_hash="$(sha256sum "${source}" | awk '{print $1}')"
        target_hash="$(sha256sum "${target}" | awk '{print $1}')"

        if [[ "${source_hash}" != "${target_hash}" ]]; then
            echo "Benchmark binary mismatch for ${name}" >&2
            echo "  source: ${source}" >&2
            echo "  target: ${target}" >&2
            echo "  source sha256: ${source_hash}" >&2
            echo "  target sha256: ${target_hash}" >&2
            exit 1
        fi
    done
}

# Docker-dependent benchmarks (SQL Server, PostgreSQL) are opt-in and excluded by default.
# To include them: CRUD_BENCH_INCLUDE_OPT_IN=1 ./run-benchmarks.sh
#
# Run in-process by default so BenchmarkDotNet does not spawn a temporary benchmark project
# that immediately performs a fresh restore/build. That temp restore is a frequent local blocker
# on offline or sandboxed machines even when the benchmark project is already built.
build_benchmarks
verify_benchmark_binaries

if [[ -x "${bench_exe}" ]]; then
    (
        cd "${bench_dir}"
        CRUD_BENCH_INPROC=1 "${bench_exe}" -j short --filter '*'
    )
else
    CRUD_BENCH_INPROC=1 dotnet run -c Release --project "${root}/benchmarks/CrudBenchmarks" -- -j short --filter '*'
fi
