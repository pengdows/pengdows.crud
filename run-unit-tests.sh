#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
results="${root}/TestResults/unit"
mkdir -p "${results}"

dotnet test "${root}/pengdows.crud.Tests/pengdows.crud.Tests.csproj" \
  -c Release \
  --results-directory "${results}" \
  --logger "trx;LogFileName=UnitTests.trx" \
  --collect "XPlat Code Coverage" \
  --settings "${root}/coverage.runsettings"

coverage_file="$(
  find "${results}" -type f -name "coverage.cobertura.xml" -printf "%T@ %p\n" \
    | sort -nr \
    | head -n 1 \
    | cut -d' ' -f2-
)"

if [[ -z "${coverage_file}" ]]; then
  echo "Coverage file not found under ${results}" >&2
  exit 1
fi

package_line="$(grep -m1 '<package name="pengdows.crud"' "${coverage_file}" || true)"
if [[ -n "${package_line}" ]]; then
  line_rate="$(sed -n 's/.*line-rate="\\([0-9.]*\\)".*/\\1/p' <<< "${package_line}")"
else
  line_rate="$(grep -m1 -o 'line-rate="[^"]*"' "${coverage_file}" | head -n 1 | cut -d'"' -f2)"
fi

if [[ -z "${line_rate}" ]]; then
  echo "Unable to determine line coverage from ${coverage_file}" >&2
  exit 1
fi

line_pct="$(awk -v rate="${line_rate}" 'BEGIN { printf "%.1f", rate * 100 }')"
echo "Line coverage (pengdows.crud): ${line_pct}%"
