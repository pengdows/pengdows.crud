#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
results="${root}/TestResults/integration"
mkdir -p "${results}"

# Preflight (DEC-011): say what will run, what will be skipped and why, before any container
# starts. PENGDOWS_EXTRA_LIB_DIRS (colon-separated) adds native-library directories this host needs,
# e.g. a libxml2.so.2 compatibility directory for Db2 or InterBase's libgds.so; nothing local is
# hard-coded here. PREFLIGHT_ONLY=1 prints the report and exits.
if [[ -n "${PENGDOWS_EXTRA_LIB_DIRS:-}" ]]; then
  export LD_LIBRARY_PATH="${PENGDOWS_EXTRA_LIB_DIRS}${LD_LIBRARY_PATH:+:${LD_LIBRARY_PATH}}"
fi

has_library() {
  local name="$1" dir
  IFS=':' read -r -a dirs <<< "${LD_LIBRARY_PATH:-}"
  for dir in "${dirs[@]}"; do
    [[ -n "${dir}" && -e "${dir}/${name}" ]] && return 0
  done
  ldconfig -p 2>/dev/null | grep -q " ${name} " && return 0
  return 1
}

preflight() {
  local fatal=0
  echo "== integration preflight =="
  echo "INTEGRATION_ONLY: ${INTEGRATION_ONLY:-<all always-on databases>}"
  if docker info >/dev/null 2>&1; then
    echo "  ok      Docker is reachable"
  else
    echo "  PROBLEM Docker is not reachable: every container-backed database would fail"
    fatal=1
  fi
  if has_library libxml2.so.2; then
    echo "  ok      Db2: libxml2.so.2 resolves"
  else
    echo "  WARN    Db2: libxml2.so.2 not found; Db2 tests will fail to load the native client" \
         "(put a compatibility copy in PENGDOWS_EXTRA_LIB_DIRS)"
  fi
  local tfm informix_lib
  for tfm in ${INTEGRATION_FRAMEWORKS:-net8.0 net10.0}; do
    informix_lib="${root}/pengdows.crud.IntegrationTests/bin/Release/${tfm}/native/lib"
    if [[ -d "${informix_lib}" ]]; then
      echo "  ok      Informix native client present for ${tfm}"
    else
      echo "  info    Informix native client for ${tfm} is copied by the build; it appears after the first build"
    fi
  done
  if [[ "${INCLUDE_INTERBASE:-}" == "true" ]]; then
    if has_library libgds.so; then
      echo "  ok      InterBase: libgds.so resolves"
    else
      echo "  WARN    InterBase: libgds.so not found (add its directory to PENGDOWS_EXTRA_LIB_DIRS)"
    fi
    if docker ps -a --format '{{.Names}}' 2>/dev/null | grep -qx 'interbase-interbase-1'; then
      echo "  ok      InterBase: licensed container interbase-interbase-1 exists"
    else
      echo "  WARN    InterBase: container interbase-interbase-1 not found (see testbed/InterBase/InterBaseTestContainer.cs)"
    fi
  else
    echo "  skip    InterBase (opt-in: INCLUDE_INTERBASE=true)"
  fi
  if [[ "${INCLUDE_SNOWFLAKE:-}" == "true" ]]; then
    local missing="" var
    for var in SNOWFLAKE_ACCOUNT SNOWFLAKE_USER SNOWFLAKE_PASSWORD SNOWFLAKE_WAREHOUSE; do
      [[ -z "${!var:-}" ]] && missing="${missing} ${var}"
    done
    if [[ -z "${missing}" ]]; then
      echo "  ok      Snowflake credentials set"
    else
      echo "  WARN    Snowflake: missing${missing}"
    fi
  else
    echo "  skip    Snowflake (opt-in: INCLUDE_SNOWFLAKE=true)"
  fi
  if [[ "${INCLUDE_SAPHANA:-}" == "true" ]]; then
    local available_gb
    available_gb=$(awk '/MemAvailable/ {printf "%d", $2/1048576}' /proc/meminfo 2>/dev/null || echo 0)
    if (( available_gb >= 16 )); then
      echo "  ok      SAP HANA: ${available_gb} GB available"
    else
      echo "  WARN    SAP HANA: ${available_gb} GB available; its image needs 16-32 GB"
    fi
  else
    echo "  skip    SAP HANA (opt-in: INCLUDE_SAPHANA=true)"
  fi
  echo "Progress: ${root}/TestResults/integration/progress.log (summary-<tfm>.md at the end)"
  return "${fatal}"
}

if ! preflight; then
  echo "Preflight found a problem that would fail the run; stopping." >&2
  exit 1
fi
if [[ "${PREFLIGHT_ONLY:-}" == "1" ]]; then
  exit 0
fi

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
  # Provisioned here, so removed here: the runtime directory was left behind in /tmp on every run
  # (REV-066).
  # Only the directory install-firebird-embedded.sh created (its mktemp name), and the function
  # always succeeds so it never changes the script's exit status.
  firebird_runtime_dir="${FIREBIRD_RUNTIME_DIR:-}"
  cleanup_firebird_runtime() {
    if [[ -n "${firebird_runtime_dir}" && -d "${firebird_runtime_dir}" &&
          "${firebird_runtime_dir}" == */pengdows-firebird.* ]]; then
      rm -rf -- "${firebird_runtime_dir}"
    fi
  }
  trap cleanup_firebird_runtime EXIT
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
