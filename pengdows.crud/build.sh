#!/bin/bash
set -e

# Example: YYYY.MM.DD.BUILDNUM
#BUILD_DATE=$(date +%Y.%m.%d)
#BUILD_NUM=$(git rev-list --count HEAD)  # Optional, gives you incrementing number
#VERSION="${BUILD_DATE}.${BUILD_NUM}"
# Version format: 1.0.<epoch-seconds>
EPOCH=$(date +%s)
VERSION="1.0.${EPOCH}.0"

echo "Building pengdows.crud version $VERSION"

# Update the .csproj with the new version
sed -i "s|<Version>.*</Version>|<Version>$VERSION</Version>|" pengdows.crud.csproj

# Build and pack
dotnet pack -c Release
dotnet nuget push ./bin/Release/pengdows.crud.1.0.${EPOCH}.nupkg --api-key $(cat ~/token.txt) --source https://api.nuget.org/v3/index.json

