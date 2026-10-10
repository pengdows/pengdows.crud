#!/usr/bin/env bash
# Behavior tests for run-integration-tests.sh's Db2 compatibility-library handling. Each case runs the
# runner in PREFLIGHT_ONLY mode with a stub installer, so nothing is downloaded and no container starts.
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf -- "${work}"' EXIT
failures=0

fail() { echo "FAIL: $1"; failures=$((failures + 1)); }
pass() { echo "ok:   $1"; }

# A stub installer: records that it ran and exports a compatibility directory the way the real one does.
write_stub() { # exit-code
  cat > "${work}/installer.sh" <<STUB
#!/usr/bin/env bash
touch "${work}/installer-ran"
echo "DB2_COMPAT_RUNTIME_DIR=${work}/pengdows-db2.stub" >> "\${GITHUB_ENV}"
echo "DB2_COMPAT_LIB_DIR=/compat/lib" >> "\${GITHUB_ENV}"
exit $1
STUB
}

run_preflight() { # INTEGRATION_ONLY value; extra env passed through the caller's environment
  rm -f "${work}/installer-ran"
  PREFLIGHT_ONLY=1 INTEGRATION_ONLY="$1" DB2_COMPAT_INSTALLER="${work}/installer.sh" \
    bash "${root}/run-integration-tests.sh" 2>&1
}

# The library probe falls back to `ldconfig -p`; an empty stub makes "the host lacks the Db2 libraries" true
# on every machine, so the cases below do not depend on what the host happens to have installed.
mkdir -p "${work}/bin"
printf '#!/usr/bin/env bash\nexit 0\n' > "${work}/bin/ldconfig"
chmod +x "${work}/bin/ldconfig"
export PATH="${work}/bin:${PATH}"

write_stub 0

out="$(run_preflight Sqlite)"
[[ ! -e "${work}/installer-ran" ]] && pass "a run that does not include Db2 never provisions its libraries" \
  || fail "provisioned Db2 libraries for INTEGRATION_ONLY=Sqlite"
grep -q "skip    Db2 (not in INTEGRATION_ONLY)" <<< "${out}" && pass "the preflight says Db2 is out of scope" \
  || fail "no out-of-scope Db2 line for INTEGRATION_ONLY=Sqlite"

out="$(run_preflight "Sqlite, Db2")"
[[ -e "${work}/installer-ran" ]] && pass "a run that names Db2 provisions the libraries" \
  || fail "did not provision for INTEGRATION_ONLY=\"Sqlite, Db2\""

out="$(LD_LIBRARY_PATH=/host/lib run_preflight Db2)"
grep -q "^LD_LIBRARY_PATH: /host/lib:/compat/lib" <<< "${out}" \
  && pass "the compatibility directory is appended, so the host's libraries keep precedence" \
  || fail "LD_LIBRARY_PATH was not host-first: $(grep '^LD_LIBRARY_PATH' <<< "${out}")"

write_stub 1
out="$(run_preflight Db2)"; status=$?
[[ ${status} -eq 0 ]] && grep -q "WARN    Db2: could not provision" <<< "${out}" \
  && pass "a failed installer warns and the run continues" \
  || fail "a failed installer aborted the run (exit ${status})"

echo
[[ ${failures} -eq 0 ]] && echo "all passed" || { echo "${failures} failed"; exit 1; }
