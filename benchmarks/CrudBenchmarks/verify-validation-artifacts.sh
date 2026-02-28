#!/usr/bin/env bash
set -euo pipefail

root="BenchmarkDotNet.Artifacts/validation"
variants=(
  "DirectView/ViewQuery"
  "AutoSubstitution/NoSetup"
  "AutoSubstitution/ManualSetup"
  "AutoSubstitution/PengdowsAuto"
)
missing=0
for variant in "${variants[@]}"; do
  echo "Checking $variant..."
  for file in plan.xml session-options.txt; do
    path="$root/$variant/$file"
    if [[ ! -f "$path" ]]; then
      echo "ERROR: Missing $path" >&2
      missing=1
    fi
  done
done
if [[ $missing -ne 0 ]]; then
  echo "Validation artifacts are incomplete. Run the indexed-view benchmarks to regenerate the plans + session dumps." >&2
  exit 1
fi
