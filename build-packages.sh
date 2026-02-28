#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
output="${root}/artifacts"

mkdir -p "${output}"

projects=(
  "pengdows.crud.abstractions/pengdows.crud.abstractions.csproj"
  "pengdows.crud/pengdows.crud.csproj"
  "pengdows.crud.fakeDb/pengdows.crud.fakeDb.csproj"
)

for project in "${projects[@]}"; do
  echo "Packing ${project}"
  dotnet pack "${root}/${project}" -c Release -o "${output}"
done

