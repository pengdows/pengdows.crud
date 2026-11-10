#!/usr/bin/env bash
# Removes the Docker resources a test run created and left behind, and nothing else.
#
#   snapshot:   docker_snapshot <dir>                 records the Testcontainers containers and the anonymous volumes that exist
#   cleanup:    docker_cleanup_since <containers> <volumes>
#                 removes a Testcontainers container (with its anonymous volumes, docker rm -v) that is not in the snapshot,
#                 and an anonymous volume that is not in the snapshot and that no container uses.
#
# Only containers labelled org.testcontainers=true are considered, so a container another project started is never removed.
# Only volumes with an anonymous (64 hex digit) name are considered, so a named volume (a database's data) is never removed.
# Anything that existed before the run is kept. DOCKER overrides the docker command (the tests use a stub).
DOCKER="${DOCKER:-docker}"

docker_snapshot() { # dir
  local dir="$1"
  mkdir -p "${dir}"
  "${DOCKER}" ps -aq --filter label=org.testcontainers=true > "${dir}/containers" 2>/dev/null || : > "${dir}/containers"
  "${DOCKER}" volume ls -q 2>/dev/null | grep -E '^[0-9a-f]{64}$' > "${dir}/volumes" || : > "${dir}/volumes"
}

docker_cleanup_since() { # containers-snapshot volumes-snapshot
  local before_containers="$1" before_volumes="$2" id name

  while read -r id; do
    [[ -z "${id}" ]] && continue
    grep -qxF "${id}" "${before_containers}" && continue
    "${DOCKER}" rm -fv "${id}" >/dev/null 2>&1 || echo "warning: could not remove container ${id}" >&2
  done < <("${DOCKER}" ps -aq --filter label=org.testcontainers=true 2>/dev/null)

  local dangling
  dangling="$("${DOCKER}" volume ls -q -f dangling=true 2>/dev/null)"
  while read -r name; do
    [[ -z "${name}" ]] && continue
    grep -qxF "${name}" "${before_volumes}" && continue
    grep -qxF "${name}" <<< "${dangling}" || continue
    "${DOCKER}" volume rm "${name}" >/dev/null 2>&1 || echo "warning: could not remove volume ${name}" >&2
  done < <("${DOCKER}" volume ls -q 2>/dev/null | grep -E '^[0-9a-f]{64}$')
}
