#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
results="${root}/TestResults/integration/Snowflake"
mkdir -p "${results}"

require_env() {
  local name="$1"
  if [[ -z "${!name:-}" ]]; then
    echo "Error: ${name} must be set before running Snowflake integration tests." >&2
    exit 1
  fi
}

echo "Preparing to run Snowflake-only integration tests..."
require_env SNOWFLAKE_ACCOUNT
require_env SNOWFLAKE_USER
require_env SNOWFLAKE_PASSWORD
require_env SNOWFLAKE_WAREHOUSE

export INCLUDE_SNOWFLAKE=true
export INTEGRATION_ONLY=Snowflake
export TESTBED_ONLY=Snowflake

echo "Running Snowflake-only integration tests..."
dotnet test "${root}/pengdows.crud.IntegrationTests/pengdows.crud.IntegrationTests.csproj" \
  -c Release \
  --results-directory "${results}" \
  --logger "trx;LogFileName=SnowflakeIntegrationTests.trx"

# testbed is multi-targeted (net8.0;net10.0); run the matrix on each. Override with
# TESTBED_FRAMEWORKS="net10.0" to run just one.
for tfm in ${TESTBED_FRAMEWORKS:-net8.0 net10.0}; do
  dotnet run -c Release -f "${tfm}" --project "${root}/testbed"
done
