#!/bin/bash
set -e

# Version format: 1.0.<epoch-seconds>
EPOCH=$(date +%s)
VERSION="1.0.${EPOCH}.0"

echo "Building pengdows.crud version $VERSION"

# Update the .csproj with the new version
sed -i "s|<Version>.*</Version>|<Version>$VERSION</Version>|" pengdows.crud.csproj

# Build and pack with updated version
dotnet pack -c Debug
dotnet nuget push ./bin/Debug/pengdows.crud.1.0.${EPOCH}.nupkg --api-key $(cat ~/token.txt) --source https://api.nuget.org/v3/index.json
