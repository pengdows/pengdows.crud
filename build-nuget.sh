          dotnet pack pengdows.crud.abstractions/pengdows.crud.abstractions.csproj -c Release \
            --no-build \
            -p:PackageVersion=1.0.0.1
          dotnet pack pengdows.crud.fakeDb/pengdows.crud.fakeDb.csproj -c Release \
            --no-build \
            -p:PackageVersion=1.0.0.1
          dotnet pack pengdows.crud/pengdows.crud.csproj -c Release \
            --no-build \
            -p:PackageVersion=1.0.0.1
