#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
results="${root}/TestResults/integration"
mkdir -p "${results}"

# FirebirdEmbeddedConnectionTests run in-process against a real Firebird Embedded engine. Provision
# the pinned runtime (no system-wide install) unless the caller already did.
if [[ -z "${FIREBIRD_EMBEDDED_CLIENT_LIBRARY:-}" ]]; then
  firebird_env="$(mktemp)"
  GITHUB_ENV="${firebird_env}" "${root}/scripts/install-firebird-embedded.sh"
  set -a
  # shellcheck disable=SC1090
  source "${firebird_env}"
  set +a
  rm -f "${firebird_env}"
fi

dotnet test "${root}/pengdows.crud.IntegrationTests/pengdows.crud.IntegrationTests.csproj" \
  -c Release \
  --results-directory "${results}" \
  --logger "trx;LogFileName=IntegrationTests.trx"

# testbed is multi-targeted (net8.0;net10.0); run the matrix on each. Override with
# TESTBED_FRAMEWORKS="net10.0" to run just one.
for tfm in ${TESTBED_FRAMEWORKS:-net8.0 net10.0}; do
  dotnet run -c Release -f "${tfm}" --project "${root}/testbed"
done
