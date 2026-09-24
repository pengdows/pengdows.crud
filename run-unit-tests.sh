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

reported_any=0

# pengdows.crud.Tests is multi-targeted (net8.0;net10.0), so a single `dotnet test` run here
# executes both TFM legs — and the VSTest "XPlat Code Coverage" collector writes each leg's
# result to its own GUID-named subdirectory under $results, every one named the bare,
# unsuffixed coverage.cobertura.xml. A plain "pick the newest match" loop here used to
# silently report only one leg's number, chosen by a filesystem mtime race, while the other
# leg's (possibly different) number sat right next to it unreported. Report every matching
# file's number, labeled by which one it came from, instead of picking one arbitrarily.
while IFS= read -r candidate; do
  package_line="$(grep -m1 '<package name="pengdows.crud"' "${candidate}" || true)"
  if [[ -n "${package_line}" ]]; then
    candidate_line_rate="$(sed -E -n 's/.*line-rate="([0-9.]+)".*/\1/p' <<< "${package_line}")"
    candidate_branch_rate="$(sed -E -n 's/.*branch-rate="([0-9.]+)".*/\1/p' <<< "${package_line}")"
  else
    candidate_line_rate="$(grep -m1 -o 'line-rate="[^"]*"' "${candidate}" | head -n 1 | cut -d'"' -f2)"
    candidate_branch_rate="$(grep -m1 -o 'branch-rate="[^"]*"' "${candidate}" | head -n 1 | cut -d'"' -f2)"
  fi

  if [[ -z "${candidate_line_rate}" ]]; then
    continue
  fi

  reported_any=1

  # coverage.net8.0.cobertura.xml -> "net8.0" (coverlet.msbuild's own naming, if this script
  # is ever pointed at a directory that has it). The VSTest "XPlat Code Coverage" collector
  # used below always names its output the bare, unsuffixed coverage.cobertura.xml regardless
  # of TFM — one per GUID-named run subdirectory — so that case is labeled by its relative
  # path under $results instead, which IS unique per run even though the filename isn't.
  filename="$(basename "${candidate}")"
  if [[ "${filename}" =~ ^coverage\.(.+)\.cobertura\.xml$ ]]; then
    label="${BASH_REMATCH[1]}"
  else
    label="${candidate#"${results}"/}"
  fi

  line_pct="$(awk -v rate="${candidate_line_rate}" 'BEGIN { printf "%.1f", rate * 100 }')"
  echo "Line coverage (pengdows.crud) [${label}]: ${line_pct}%"

  if [[ -n "${candidate_branch_rate}" ]]; then
    branch_pct="$(awk -v rate="${candidate_branch_rate}" 'BEGIN { printf "%.1f", rate * 100 }')"
    echo "Branch coverage (pengdows.crud) [${label}]: ${branch_pct}%"
  fi
done < <(
  find "${results}" -type f -name "coverage*.cobertura.xml" -printf "%T@ %p\n" \
    | sort -nr \
    | cut -d' ' -f2-
)

if [[ "${reported_any}" -eq 0 ]]; then
  echo "Coverage file not found (or none contained a determinable line-rate) under ${results}" >&2
  exit 1
fi
