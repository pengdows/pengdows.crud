#!/usr/bin/env bash
# REL-008 release gate for the commit being released. Run from the repository root.
#   tools/release-gate.sh              unit tests, API baseline, package validation
#   RUN_INTEGRATION=true tools/release-gate.sh   also the full integration matrix (long; needs Docker)
# Refuses a dirty tree, so every result belongs to one commit.
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ -n "$(git status --porcelain)" ]]; then
  echo "release-gate: working tree is not clean; commit or stash first" >&2
  git status --short >&2
  exit 1
fi
HEAD_SHA="$(git rev-parse --short HEAD)"
echo "release-gate: commit ${HEAD_SHA}"

dotnet build pengdows.crud.sln -c Release

# Unit tests only: never the solution-level `dotnet test`, which pulls in the integration project.
for tfm in net8.0 net10.0; do
  dotnet test pengdows.crud.Tests/pengdows.crud.Tests.csproj -c Release --no-build -f "$tfm" -p:CollectCoverage=false
done

dotnet run --project tools/interface-api-check/InterfaceApiCheck.csproj -c Release -- \
  --verify \
  --baseline pengdows.crud.abstractions/ApiBaseline/interfaces.txt \
  --assembly pengdows.crud.abstractions/bin/Release/net8.0/pengdows.crud.abstractions.dll

# Package validation against the PackageValidationBaselineVersion (2.0.5) runs as part of pack.
OUT="$(mktemp -d)"
for p in pengdows.crud pengdows.crud.abstractions pengdows.crud.fakeDb; do
  dotnet pack "$p/$p.csproj" -c Release --no-build -o "$OUT"
done
rm -rf "$OUT"

if [[ "${RUN_INTEGRATION:-false}" == "true" ]]; then
  bash run-integration-tests.sh
  python3 tools/record-integration-run.py
else
  echo "release-gate: integration matrix skipped (set RUN_INTEGRATION=true); docs/integration-status.md must be green on ${HEAD_SHA} before release"
fi
echo "release-gate: PASSED for ${HEAD_SHA}"
