CrudBenchmarks

- Purpose: microbenchmarks comparing pengdows.crud vs. Dapper vs. Entity Framework for common CRUD paths and advanced database features.

- Prerequisites:
  - Docker running locally (for PostgreSQL benchmarks)
  - SQL Server LocalDB (for indexed view benchmarks)
  - BenchmarkDotNet runs the suite in Release.

- Run all benchmarks:

  - dotnet run -c Release --project benchmarks/CrudBenchmarks --

- Run specific benchmark suites:

  - PostgreSQL CRUD: `dotnet run -c Release -- --filter *Pagila*`
  - Indexed Views: `dotnet run -c Release -- --filter *IndexedView*`
  - SQL Generation: `dotnet run -c Release -- --filter *SqlGeneration*`

- Notable benchmarks:

  - **PagilaBenchmarks**: Basic CRUD operations vs Dapper/EF using PostgreSQL
  - **IndexedViewBenchmarks**: Demonstrates pengdows.crud's indexed view advantages over EF
  - **SqlServerBenchmarks**: SQL Server specific features and optimizations
  - **IsolationBenchmarks**: Transaction isolation level performance

- Notes:

  - PostgreSQL benchmarks use Testcontainers with postgres:15-alpine
  - Dataset sizes controlled by attributes: FilmCount=1000, ActorCount=200
  - Indexed view benchmarks require SQL Server LocalDB (Windows)
  - Insert benchmarks perform insert+delete to maintain stable dataset sizes

