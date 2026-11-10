#!/usr/bin/env bash
# Tests tools/docker-run-cleanup.sh against a stub `docker` that reads its state from files, so no Docker is needed.
set -uo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "${here}/../.." && pwd)"
cleanup_script="${root}/tools/docker-run-cleanup.sh"
failures=0
tmp="$(mktemp -d)"
trap 'rm -rf "${tmp}"' EXIT

# A stub docker: containers and volumes are files, dangling volumes are a third file, and every removal is logged.
cat > "${tmp}/docker" <<'STUB'
#!/usr/bin/env bash
state="${STUB_STATE:?}"
case "$*" in
  "ps -aq --filter label=org.testcontainers=true") cat "${state}/containers" 2>/dev/null ;;
  "volume ls -q") cat "${state}/volumes" 2>/dev/null ;;
  "volume ls -q -f dangling=true") cat "${state}/dangling" 2>/dev/null ;;
  "rm -fv "*) id="${3}"; echo "rm ${id}" >> "${state}/log"; grep -vx "${id}" "${state}/containers" > "${state}/c.tmp"; mv "${state}/c.tmp" "${state}/containers" ;;
  "volume rm "*) name="${3}"; echo "volume rm ${name}" >> "${state}/log"; grep -vx "${name}" "${state}/volumes" > "${state}/v.tmp"; mv "${state}/v.tmp" "${state}/volumes" ;;
  *) echo "stub: unexpected docker $*" >&2; exit 2 ;;
esac
STUB
chmod +x "${tmp}/docker"

fresh_state() { # dir
  rm -rf "$1"; mkdir -p "$1"; : > "$1/log"
}

check() { # description, expected-log-line-or-empty, log file, grep mode
  local description="$1" expected="$2" log="$3"
  if [[ -z "${expected}" ]]; then
    if [[ -s "${log}" ]]; then echo "FAIL: ${description}: expected nothing removed, got: $(tr '\n' ';' < "${log}")"; failures=$((failures+1)); fi
  elif ! grep -qxF "${expected}" "${log}"; then
    echo "FAIL: ${description}: expected '${expected}', got: $(tr '\n' ';' < "${log}")"; failures=$((failures+1))
  fi
}
check_absent() { # description, line that must not be removed, log file
  if grep -qxF "$2" "$3"; then echo "FAIL: $1: '$2' was removed but must be kept"; failures=$((failures+1)); fi
}

old_c="aaaa1111aaaa1111aaaa1111aaaa1111aaaa1111aaaa1111aaaa1111aaaa1111"
new_c="bbbb2222bbbb2222bbbb2222bbbb2222bbbb2222bbbb2222bbbb2222bbbb2222"
old_v="cccc3333cccc3333cccc3333cccc3333cccc3333cccc3333cccc3333cccc3333"
new_v="dddd4444dddd4444dddd4444dddd4444dddd4444dddd4444dddd4444dddd4444"
linked_v="eeee5555eeee5555eeee5555eeee5555eeee5555eeee5555eeee5555eeee5555"
named="liquibase_wiki_postgres_data"

# Case 1: a run created one container and two volumes; the machine already had one container, one volume, and a named volume.
S="${tmp}/case1"; fresh_state "${S}"
printf '%s\n' "${old_c}" > "${S}/containers"; printf '%s\n' "${old_v}" "${named}" > "${S}/volumes"
cp "${S}/containers" "${S}/before-containers"; cp "${S}/volumes" "${S}/before-volumes"
printf '%s\n' "${new_c}" >> "${S}/containers"; printf '%s\n' "${new_v}" >> "${S}/volumes"
printf '%s\n' "${old_v}" "${named}" "${new_v}" > "${S}/dangling"
STUB_STATE="${S}" PATH="${tmp}:${PATH}" DOCKER="${tmp}/docker" bash -c "source '${cleanup_script}'; docker_cleanup_since '${S}/before-containers' '${S}/before-volumes'" 2>"${S}/err" || true
check "a container the run created is removed" "rm ${new_c}" "${S}/log"
check "an anonymous volume the run created and nothing uses is removed" "volume rm ${new_v}" "${S}/log"
check_absent "a container that existed before the run is kept" "rm ${old_c}" "${S}/log"
check_absent "an anonymous volume that existed before the run is kept" "volume rm ${old_v}" "${S}/log"
check_absent "a named volume is never removed" "volume rm ${named}" "${S}/log"

# Case 2: a new anonymous volume that a running container still uses is not removed.
S="${tmp}/case2"; fresh_state "${S}"
: > "${S}/containers"; printf '%s\n' "${linked_v}" > "${S}/volumes"; : > "${S}/before-containers"; : > "${S}/before-volumes"
: > "${S}/dangling"   # in use, so not dangling
STUB_STATE="${S}" PATH="${tmp}:${PATH}" DOCKER="${tmp}/docker" bash -c "source '${cleanup_script}'; docker_cleanup_since '${S}/before-containers' '${S}/before-volumes'" 2>"${S}/err" || true
check_absent "a volume still in use is not removed" "volume rm ${linked_v}" "${S}/log"

# Case 3: nothing new since the run started, so nothing is removed.
S="${tmp}/case3"; fresh_state "${S}"
printf '%s\n' "${old_c}" > "${S}/containers"; printf '%s\n' "${old_v}" > "${S}/volumes"; printf '%s\n' "${old_v}" > "${S}/dangling"
cp "${S}/containers" "${S}/before-containers"; cp "${S}/volumes" "${S}/before-volumes"
STUB_STATE="${S}" PATH="${tmp}:${PATH}" DOCKER="${tmp}/docker" bash -c "source '${cleanup_script}'; docker_cleanup_since '${S}/before-containers' '${S}/before-volumes'" 2>"${S}/err" || true
check "nothing new means nothing removed" "" "${S}/log"

if (( failures > 0 )); then echo "${failures} failure(s)"; exit 1; fi
echo "docker-run-cleanup: all cases passed"
