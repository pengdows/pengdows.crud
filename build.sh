#!/bin/bash
set -euo pipefail

if [ $# -lt 1 ]; then
  echo "Usage: $0 <nuget-api-key>" >&2
  exit 1
fi

API_KEY="$1"
EPOCH=$(date +%s)
VERSION="1.0.${EPOCH}.0"
VERSION_SHORT="1.0.${EPOCH}"

update_version() {
  local csproj=$1
  sed -i "s|<Version>.*</Version>|<Version>${VERSION}</Version>|" "$csproj"
}

update_version pengdow.crud.abstractions/pengdow.crud.abstractions.csproj
update_version pengdow.crud.fakeDb/pengdow.crud.fakeDb.csproj
update_version pengdow.crud/pengdow.crud.csproj

# Build and pack each project
for proj in pengdow.crud.abstractions pengdow.crud.fakeDb pengdow.crud; do
  dotnet pack "$proj/$proj.csproj" -c Release
  pkg="${proj}/bin/Release/${proj}.${VERSION_SHORT}.nupkg"
  dotnet nuget push "$pkg" --source https://api.nuget.org/v3/index.json --api-key "$API_KEY"
done

# Roll back project changes
git reset --hard

# Tag the release and push
git tag -a "v${VERSION_SHORT}" -m "Release ${VERSION_SHORT}"
git push origin "v${VERSION_SHORT}"
