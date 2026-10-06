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

# The suite runs one database per test process, at most two at a time (INTEGRATION_PARALLEL), like
# the testbed's two-slot dispatcher: every database up at once saturated the host and ran Oracle
# Free out of server processes (ORA-12516). The queue is longest first by expected seconds, so the
# slow databases start at once and the short ones fill the other slot instead of one slow database
# finishing alone at the end. The weights start from the testbed's StartupWeightSeconds; the timing
# table printed at the end is the measurement to tune them with. INTEGRATION_ONLY limits the queue.
#
# always_on_databases must match IntegrationTestConfiguration.BaseProviders
# (IntegrationTestConfigurationTests.RunScriptBatches_AreTheAlwaysOnProviders).
always_on_databases=(Sqlite PostgreSql SqlServer MySql MariaDb Firebird CockroachDb DuckDB Oracle YugabyteDb TiDb FlatFile Db2 Informix SybaseASE Spanner SingleStore)
declare -A integration_weights=(
  [SapHana]=300 [Db2]=60 [Oracle]=45 [SybaseASE]=45 [Spanner]=30 [SqlServer]=25 [TiDb]=20
  [YugabyteDb]=20 [Informix]=20 [SingleStore]=15 [CockroachDb]=12 [MySql]=8 [MariaDb]=8
  [Firebird]=8 [PostgreSql]=5 [Snowflake]=5 [InterBase]=5 [Access]=5 [Sqlite]=1 [DuckDB]=1
  [FlatFile]=1
)

if [[ -n "${INTEGRATION_ONLY:-}" ]]; then
  IFS=',' read -r -a requested <<< "${INTEGRATION_ONLY// /}"
else
  requested=("${always_on_databases[@]}")
  [[ "${INCLUDE_SNOWFLAKE:-}" == "true" ]] && requested+=(Snowflake)
  [[ "${INCLUDE_SAPHANA:-}" == "true" ]] && requested+=(SapHana)
  [[ "${INCLUDE_INTERBASE:-}" == "true" ]] && requested+=(InterBase)
  [[ "${OSTYPE:-}" == msys* || "${OSTYPE:-}" == cygwin* ]] && requested+=(Access)
fi
mapfile -t queue < <(for db in "${requested[@]}"; do echo "${integration_weights[$db]:-10} ${db}"; done |
  sort -k1,1nr -k2,2 | awk '{print $2}')
parallel="${INTEGRATION_PARALLEL:-2}"
failed=()

run_database() {
  local tfm="$1" db="$2" started=${SECONDS} status=0
  INTEGRATION_ONLY="${db}" \
  INFORMIXSQLHOSTS="${INFORMIXSQLHOSTS_BASE:-${TMPDIR:-/tmp}/pengdows-informix-sqlhosts}-${tfm}-${db}" \
    dotnet test "${root}/pengdows.crud.IntegrationTests/pengdows.crud.IntegrationTests.csproj" \
      -c Release -f "${tfm}" --no-build \
      --results-directory "${results}" \
      --logger "trx;LogFileName=IntegrationTests-${tfm}-${db}.trx" \
      > "${results}/IntegrationTests-${tfm}-${db}.log" 2>&1 || status=$?
  echo "${db} $((SECONDS - started)) ${status}" >> "${results}/timings-${tfm}.txt"
  return "${status}"
}

# One target framework at a time: each process still gets its own sqlhosts file (the Informix
# client resolves its server through INFORMIXSQLHOSTS, and a shared file let concurrent processes
# use each other's Informix database). Override with INTEGRATION_FRAMEWORKS="net10.0" for one.
for tfm in ${INTEGRATION_FRAMEWORKS:-net8.0 net10.0}; do
  dotnet build "${root}/pengdows.crud.IntegrationTests/pengdows.crud.IntegrationTests.csproj" -c Release -f "${tfm}"
  : > "${results}/timings-${tfm}.txt"
  declare -A running=()
  for db in "${queue[@]}"; do
    while (( ${#running[@]} >= parallel )); do
      wait -n || true
      for pid in "${!running[@]}"; do
        if ! kill -0 "${pid}" 2>/dev/null; then
          wait "${pid}" || failed+=("${tfm} ${running[$pid]}")
          unset "running[$pid]"
        fi
      done
    done
    echo "[${tfm}] starting ${db}"
    run_database "${tfm}" "${db}" &
    running[$!]="${db}"
  done
  for pid in "${!running[@]}"; do
    wait "${pid}" || failed+=("${tfm} ${running[$pid]}")
  done
  unset running

  echo "== ${tfm}: seconds per database (tune integration_weights with these) =="
  sort -k2,2nr "${results}/timings-${tfm}.txt" |
    awk '{printf "  %-12s %6ss  %s\n", $1, $2, ($3 == 0 ? "passed" : "FAILED (exit " $3 ")")}'
done

if (( ${#failed[@]} > 0 )); then
  echo "Integration suite failed on: ${failed[*]} (logs: ${results}/IntegrationTests-<tfm>-<db>.log)" >&2
  exit 1
fi

# testbed is multi-targeted (net8.0;net10.0); run the matrix on each. Override with
# TESTBED_FRAMEWORKS="net10.0" to run just one. It runs the databases INTEGRATION_ONLY names (the
# testbed takes the same names), so INTEGRATION_ONLY=Oracle is an Oracle-only run end to end;
# TESTBED_ONLY overrides that for the matrix alone.
for tfm in ${TESTBED_FRAMEWORKS:-net8.0 net10.0}; do
  TESTBED_ONLY="${TESTBED_ONLY:-${INTEGRATION_ONLY:-}}" \
    dotnet run -c Release -f "${tfm}" --project "${root}/testbed"
done
