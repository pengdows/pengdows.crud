CrudBenchmarks

- Purpose: microbenchmarks comparing pengdows.crud vs. Dapper for common CRUD paths over a Postgres Pagila-like schema.

- Prerequisites: Docker running locally. BenchmarkDotNet runs the suite in Release.

- Run all benchmarks:

  - dotnet run -c Release --project benchmarks/CrudBenchmarks --

- Run a subset (example):

  - dotnet run -c Release --project benchmarks/CrudBenchmarks -- --filter *GetFilmById*

- Notes:

  - Uses Testcontainers to start postgres:15-alpine, seed minimal data, and then executes benchmarks.
  - Dataset sizes are controlled by attributes in code: FilmCount=1000, ActorCount=200.
  - For identity retrieval performance, the benchmark uses a SingleWriter DatabaseContext.
  - Insert benchmarks perform an insert and then a matching delete to keep the dataset size stable.

