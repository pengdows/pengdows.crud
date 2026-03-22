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

coverage_file=""
line_rate=""
branch_rate=""

while IFS= read -r candidate; do
  package_line="$(grep -m1 '<package name="pengdows.crud"' "${candidate}" || true)"
  if [[ -n "${package_line}" ]]; then
    candidate_line_rate="$(sed -E -n 's/.*line-rate="([0-9.]+)".*/\1/p' <<< "${package_line}")"
    candidate_branch_rate="$(sed -E -n 's/.*branch-rate="([0-9.]+)".*/\1/p' <<< "${package_line}")"
  else
    candidate_line_rate="$(grep -m1 -o 'line-rate="[^"]*"' "${candidate}" | head -n 1 | cut -d'"' -f2)"
    candidate_branch_rate="$(grep -m1 -o 'branch-rate="[^"]*"' "${candidate}" | head -n 1 | cut -d'"' -f2)"
  fi

  if [[ -n "${candidate_line_rate}" ]]; then
    coverage_file="${candidate}"
    line_rate="${candidate_line_rate}"
    branch_rate="${candidate_branch_rate}"
    break
  fi
done < <(
  find "${results}" -type f -name "coverage.cobertura.xml" -printf "%T@ %p\n" \
    | sort -nr \
    | cut -d' ' -f2-
)

if [[ -z "${coverage_file}" ]]; then
  echo "Coverage file not found under ${results}" >&2
  exit 1
fi

if [[ -z "${line_rate}" ]]; then
  echo "Unable to determine line coverage from ${coverage_file}" >&2
  exit 1
fi

line_pct="$(awk -v rate="${line_rate}" 'BEGIN { printf "%.1f", rate * 100 }')"
echo "Line coverage (pengdows.crud): ${line_pct}%"

if [[ -n "${branch_rate}" ]]; then
  branch_pct="$(awk -v rate="${branch_rate}" 'BEGIN { printf "%.1f", rate * 100 }')"
  echo "Branch coverage (pengdows.crud): ${branch_pct}%"
fi
