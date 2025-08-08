          dotnet pack pengdow.crud.abstractions/pengdow.crud.abstractions.csproj -c Release \
            --no-build \
            -p:PackageVersion=1.0.0.1
          dotnet pack pengdow.crud.fakeDb/pengdow.crud.fakeDb.csproj -c Release \
            --no-build \
            -p:PackageVersion=1.0.0.1
          dotnet pack pengdow.crud/pengdow.crud.csproj -c Release \
            --no-build \
            -p:PackageVersion=1.0.0.1
