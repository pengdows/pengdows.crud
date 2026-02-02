#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
results="${root}/TestResults/unit"
mkdir -p "${results}"

dotnet test "${root}/pengdows.crud.Tests/pengdows.crud.Tests.csproj" \
  -c Release \
  --results-directory "${results}" \
  --logger "trx;LogFileName=UnitTests.trx"
