#!/usr/bin/env bash
# Port-pressure benchmark: does an uncontrolled connection storm, or a governed one with the default pool,
# leave the client host's ephemeral ports in TIME_WAIT, and does the governor avoid that when its pool is capped
# at the server's connection limit?
#
# Needs Docker, the Linux `ss` tool, and a free local TCP port. Starts a throwaway PostgreSQL 16 container with
# max_connections=25, runs each arm REPS times, samples TIME_WAIT once a second, and removes the container on exit.
#
#   benchmarks/PortPressure/run-port-pressure.sh            # 3 repetitions
#   REPS=1 benchmarks/PortPressure/run-port-pressure.sh     # quick check
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
reps="${REPS:-3}"
port="${PORT:-15432}"
name="pengdows-port-pressure-$$"
max_connections=25
out_dir="$(mktemp -d "${TMPDIR:-/tmp}/pengdows-port-pressure.XXXXXX")"

command -v docker >/dev/null || { echo "docker is required" >&2; exit 1; }
command -v ss >/dev/null || { echo "ss (iproute2) is required" >&2; exit 1; }

cleanup() {
  docker rm -f "$name" >/dev/null 2>&1 || true
  rm -rf "$out_dir"
}
trap cleanup EXIT

time_wait() { ss -Htan state time-wait | wc -l; }

quiet() {
  for _ in $(seq 1 400); do
    [ "$(time_wait)" -lt 50 ] && return 0
    sleep 1
  done
  echo "TIME_WAIT did not clear (still $(time_wait))" >&2
  return 1
}

docker run -d --name "$name" -e POSTGRES_PASSWORD=probe -e POSTGRES_DB=probe -p "$port:5432" \
  postgres:16-alpine -c "max_connections=$max_connections" >/dev/null
for _ in $(seq 1 120); do
  docker exec "$name" psql -U postgres -d probe -tAc "SELECT 1" >/dev/null 2>&1 && break
  sleep 1
done
docker exec "$name" psql -U postgres -d probe -qc \
  "CREATE TABLE port_pressure (id int primary key, value int not null); INSERT INTO port_pressure SELECT g, 0 FROM generate_series(1,100) g;"

conn="Host=localhost;Port=$port;Database=probe;Username=postgres;Password=probe"
(cd "$here" && dotnet build -c Release -f net10.0 >/dev/null)
probe="$here/bin/Release/net10.0/PortPressure"

echo "server max_connections=$(docker exec "$name" psql -U postgres -d probe -tAc 'SHOW max_connections')"
for rep in $(seq 1 "$reps"); do
  for arm in uncontrolled governed-default governed-capped; do
    quiet
    samples="$out_dir/$arm-$rep.csv"
    echo "second,time_wait" > "$samples"
    ( for t in $(seq 0 120); do echo "$t,$(time_wait)" >> "$samples"; sleep 1; done ) &
    sampler=$!
    result="$("$probe" "$arm" "$conn")"
    wait "$sampler"
    peak="$(awk -F, 'NR>1{if($2>m)m=$2}END{print m+0}' "$samples")"
    echo "rep=$rep $result peak_time_wait=$peak"
  done
done
