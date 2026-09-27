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

# Informix.Net.Core-lnx's native client (libthcli15a.so) resolves its own dependencies through
# LD_LIBRARY_PATH, which glibc reads only at process start, and reads INFORMIXDIR (its CSDK tree:
# GLS locale and message files) and INFORMIXSQLHOSTS through native getenv(), which never sees
# values a .NET process sets for itself. The testbed re-executes itself with all three; vstest's
# testhost cannot, so export them here. The native tree is identical for both target frameworks.
# Db2 needs nothing here: its bootstrap loads libdb2.so by absolute path.
for tfm in net8.0 net10.0; do
  informix_lib="${root}/pengdows.crud.IntegrationTests/bin/Release/${tfm}/native/lib"
  LD_LIBRARY_PATH="${informix_lib}${LD_LIBRARY_PATH:+:${LD_LIBRARY_PATH}}"
done
export LD_LIBRARY_PATH
export INFORMIXDIR="${INFORMIXDIR:-${root}/pengdows.crud.IntegrationTests/bin/Release/net10.0/native}"

# One target framework at a time, each with its own sqlhosts file. The Informix client resolves the
# server through INFORMIXSQLHOSTS, and each test process writes its own container's port there;
# with both frameworks running at once on one shared file, the last write won and both processes
# used the same Informix database, dropping and recreating each other's tables (confirmed:
# "table not in the database" / unique-violation failures only on Informix). Sequential runs also
# halve the peak Docker load. Override with INTEGRATION_FRAMEWORKS="net10.0" to run just one.
for tfm in ${INTEGRATION_FRAMEWORKS:-net8.0 net10.0}; do
  INFORMIXSQLHOSTS="${INFORMIXSQLHOSTS_BASE:-${TMPDIR:-/tmp}/pengdows-informix-sqlhosts}-${tfm}" \
    dotnet test "${root}/pengdows.crud.IntegrationTests/pengdows.crud.IntegrationTests.csproj" \
      -c Release -f "${tfm}" \
      --results-directory "${results}" \
      --logger "trx;LogFileName=IntegrationTests-${tfm}.trx"
done

# testbed is multi-targeted (net8.0;net10.0); run the matrix on each. Override with
# TESTBED_FRAMEWORKS="net10.0" to run just one.
for tfm in ${TESTBED_FRAMEWORKS:-net8.0 net10.0}; do
  dotnet run -c Release -f "${tfm}" --project "${root}/testbed"
done
