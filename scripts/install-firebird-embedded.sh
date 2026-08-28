#!/usr/bin/env bash
set -euo pipefail

# Keep this pinned. The complete distribution is required because Firebird Embedded
# loads the client, engine plugin, configuration, ICU, authentication, and support
# files as one installation.
version="5.0.4"
build="5.0.4.1812-0"
archive="Firebird-${build}-linux-x64.tar.gz"
url="https://github.com/FirebirdSQL/firebird/releases/download/v${version}/${archive}"
sha256="ab6a15a0258f38b022be496bb5e038c14e8628ce9acd0f9a06288a3baedd917b"
temp_root="${RUNNER_TEMP:-${TMPDIR:-/tmp}}"
install_dir=$(mktemp -d "${temp_root}/pengdows-firebird.XXXXXX")
download="${install_dir}/${archive}"
extract_dir="${install_dir}/archive"
runtime_dir="${install_dir}/runtime"
firebird_dir="${runtime_dir}/firebird"
dependency_dir="${install_dir}/dependencies"
lock_dir="${install_dir}/lock"
temp_dir="${install_dir}/tmp"

# The directory intentionally remains available after this script exits so the test
# process can use it. The workflow cleanup step removes this exact directory afterward.
trap 'find "${install_dir}" -depth -type f -name "*.deb" -delete 2>/dev/null || true' EXIT

curl --fail --location --retry 3 --output "${download}" "${url}"
echo "${sha256}  ${download}" | sha256sum --check --status
mkdir -p "${extract_dir}" "${runtime_dir}" "${dependency_dir}" "${lock_dir}" "${temp_dir}"
tar -xzf "${download}" --strip-components=1 -C "${extract_dir}"

# Do not invoke install.sh: it is intentionally root-oriented and modifies /opt,
# /etc, service registration, and system library links. Extracting its buildroot is
# the complete runtime without any machine-wide installation.
tar -xzf "${extract_dir}/buildroot.tar.gz" --strip-components=2 -C "${runtime_dir}"

# Firebird's distribution leaves TomMath and TomCrypt as host dependencies. Download
# and unpack them into this temporary tree instead of installing them system-wide.
cd "${install_dir}"
apt-get download libtommath1 libtomcrypt1 >/dev/null
for package in libtommath1_*.deb libtomcrypt1_*.deb; do
    dpkg-deb -x "${package}" "${dependency_dir}"
done

# Firebird Embedded uses a process-local lock directory. Keep it in the same temporary
# tree rather than touching a shared /tmp/firebird directory owned by a system install.
sed -i 's/^#\?Providers[[:space:]]*=.*/Providers = Engine13/' "${firebird_dir}/firebird.conf"

env_file="${GITHUB_ENV:-}"
if [[ -n "${env_file}" ]]; then
    {
        echo "FIREBIRD=${firebird_dir}"
        echo "FIREBIRD_EMBEDDED_CLIENT_LIBRARY=${firebird_dir}/lib/libfbclient.so"
        echo "FIREBIRD_RUNTIME_DIR=${install_dir}"
        echo "FIREBIRD_LOCK=${lock_dir}"
        echo "FIREBIRD_TMP=${temp_dir}"
        echo "LD_LIBRARY_PATH=${firebird_dir}/lib:${firebird_dir}/plugins:${dependency_dir}/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:${LD_LIBRARY_PATH}}"
        # Embedded connections use an explicit test credential; no default masterkey
        # is assumed by the .NET tests.
        echo "FIREBIRD_TEST_PASSWORD=${FIREBIRD_TEST_PASSWORD:-pengdows-firebird-test}"
    } >> "${env_file}"
else
    cat <<EOF
Firebird runtime: ${firebird_dir}
Run tests with FIREBIRD=${firebird_dir}, FIREBIRD_LOCK=${lock_dir},
FIREBIRD_TMP=${temp_dir}, and LD_LIBRARY_PATH=${firebird_dir}/lib:${firebird_dir}/plugins:${dependency_dir}/usr/lib/x86_64-linux-gnu.
Remove ${install_dir} after testing.
EOF
fi

test -f "${firebird_dir}/lib/libfbclient.so"
test -f "${firebird_dir}/plugins/libEngine13.so"
test -f "${firebird_dir}/firebird.conf"
test -f "${firebird_dir}/security5.fdb"
