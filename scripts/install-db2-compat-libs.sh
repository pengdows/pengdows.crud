#!/usr/bin/env bash
set -euo pipefail

# IBM's Linux clidriver shipped by Net.IBM.Data.Db2-lnx 8.0.0.500 is linked against
# libxml2.so.2 and ICU 72.  Newer Debian/Ubuntu images may only provide newer ABI names.
# Keep the compatibility libraries process-local and out of the host installation.

if [[ "$(uname -m)" != "x86_64" ]]; then
  echo "Db2 compatibility bootstrap supports x86_64 only (host: $(uname -m))." >&2
  exit 1
fi

temp_root="${RUNNER_TEMP:-${TMPDIR:-/tmp}}"
install_dir="$(mktemp -d "${temp_root}/pengdows-db2.XXXXXX")"
dependency_dir="${install_dir}/dependencies"
mkdir -p "${dependency_dir}"

cleanup() {
  rm -rf -- "${install_dir}"
}
trap cleanup EXIT

libxml_url="https://deb.debian.org/debian/pool/main/libx/libxml2/libxml2_2.9.14%2Bdfsg-1.3~deb12u6_amd64.deb"
libxml_sha256="4460e39dda10a815881374217cde08474747cfa018358cd8612c14b390eff53b"
icu_url="https://deb.debian.org/debian/pool/main/i/icu/libicu72_72.1-3%2Bdeb12u1_amd64.deb"
icu_sha256="f7f6f99c6d7b025914df2447fc93e11d22c44c0c8bdd8b6f36691c9e7ddcef88"

download_and_extract() {
  local url="$1" sha256="$2" file="$3"
  curl --fail --location --retry 3 --output "${install_dir}/${file}" "${url}"
  echo "${sha256}  ${install_dir}/${file}" | sha256sum --check --status
  dpkg-deb -x "${install_dir}/${file}" "${dependency_dir}"
}

download_and_extract "${libxml_url}" "${libxml_sha256}" libxml2.deb
download_and_extract "${icu_url}" "${icu_sha256}" libicu72.deb

lib_dir="${dependency_dir}/usr/lib/x86_64-linux-gnu"
test -f "${lib_dir}/libxml2.so.2"
test -f "${lib_dir}/libicuuc.so.72"
test -f "${lib_dir}/libicudata.so.72"

if [[ -n "${GITHUB_ENV:-}" ]]; then
  echo "DB2_COMPAT_RUNTIME_DIR=${install_dir}" >> "${GITHUB_ENV}"
  echo "DB2_COMPAT_LIB_DIR=${lib_dir}" >> "${GITHUB_ENV}"
else
  echo "DB2 compatibility libraries: ${lib_dir}"
fi

# The caller owns the directory lifetime.  This script is sourced through GITHUB_ENV by the
# integration runner, which removes DB2_COMPAT_RUNTIME_DIR on exit.
trap - EXIT
