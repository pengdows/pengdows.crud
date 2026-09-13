#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
results="${root}/TestResults/integration"
mkdir -p "${results}"

# pengdows.crud.IntegrationTests targets net8.0;net10.0, so a single `dotnet test` invocation
# below launches two full testhost processes concurrently, each independently standing up the
# ~12-provider IntegrationTestFixture. TESTBED_REUSE_CONTAINERS=true lets the 7 providers with
# cheap per-run database isolation (Postgres, MySQL, MariaDB, SQL Server, CockroachDB,
# YugabyteDB, TiDB - see testbed/TestContainerReuse.cs) share one physical container across both
# processes instead of each paying its own image-pull/boot cost, while each process still gets
# its own randomly-named database so the two runs never see each other's tables even though they
# execute at the same time. Containers left running by this (Ryuk's cleanup is disabled for a
# reused resource) are harmless leftovers - `docker container prune` clears them, or leave them
# for the next run to reuse again.
export TESTBED_REUSE_CONTAINERS=true

dotnet test "${root}/pengdows.crud.IntegrationTests/pengdows.crud.IntegrationTests.csproj" \
  -c Release \
  --results-directory "${results}" \
  --logger "trx;LogFileName=IntegrationTests.trx"

dotnet run -c Release --project "${root}/testbed"
