# Repository Guidelines

## Project Structure & Module Organization
- Source: `pengdows.crud/` (core), `pengdows.crud.abstractions/` (interfaces), `pengdows.crud.fakeDb/` (in-memory provider), `testbed/` (integration suite via Testcontainers).
- Tests: `pengdows.crud.Tests/` (xUnit). Coverage and TRX under `TestResults/`.
- Solution: `pengdows.crud.sln`. CI: `.github/workflows/deploy.yml`.

## Build, Test, and Development Commands
- Restore: `dotnet restore`
- Build: `dotnet build pengdows.crud.sln -c Release` (treats warnings as errors for libraries).
- Test (local): `dotnet test -c Release --results-directory TestResults --logger trx`
- Test with coverage (CI-like):
  `dotnet test -c Release --results-directory TestResults -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Exclude="[pengdows.crud.Tests]*;[pengdows.crud.abstractions]*;[pengdows.crud.fakeDb]*;[testbed]*"`
- Pack (NuGet): `dotnet pack <project>.csproj -c Release`
- Integration suite (requires Docker): `dotnet run -c Release --project testbed`

## Coding Style & Naming Conventions
- C# 12 on `net8.0`; `Nullable` and `ImplicitUsings` enabled.
- File-scoped namespaces; keep lowercase namespaces (`pengdows.crud.*`).
- Indentation: 4 spaces; follow existing brace style; prefer expression-bodied members when clearer.
- Minimize public APIs; make types/members `internal` when possible. `WarningsAsErrors=true`.
- Organize by domain folders: `attributes/`, `dialects/`, `connection/`, `threading/`, `exceptions/`.
- Refer to the project as `fakeDb` (lowercase f, uppercase D) in paths/docs.

## Interfaces & Extension Points
- `IDatabaseContext`: entry point. Create via DI or `new DatabaseContext(connStr, DbProviderFactory)`. Builds `ISqlContainer`, formats names/params, and controls connections/transactions.
- `ISqlContainer`: compose SQL safely and execute.
  Example: `var sc = ctx.CreateSqlContainer("SELECT 1"); var v = await sc.ExecuteScalarAsync<int>();`
- `IEntityHelper<TEntity, TRowID>`: SQL-first CRUD with inspectable containers.
  Example: `var sc = helper.BuildRetrieve(new[] { id }); var e = await helper.LoadSingleAsync(sc);`
- `ITransactionContext`: `using var tx = ctx.BeginTransaction(); ... tx.Commit();` Pass `tx` to helper methods when you want execution inside the transaction.
- `IAuditValueResolver`: implement to supply user/time; register in DI so audit fields populate consistently.
- Advanced: implement `ISqlDialect`/`IDbProviderLoader` to add/override provider behavior.

## Testing Guidelines
- Framework: xUnit; mocks: Moq. Name files `*Tests.cs` and mirror source namespaces.
- Prefer `pengdows.crud.fakeDb` for unit tests; avoid real DBs. Use `testbed/` for integration via Testcontainers.
- Coverage artifacts live in `TestResults/`; CI publishes Cobertura from `TestResults/**/coverage.cobertura.xml`.

## Commit & Pull Request Guidelines
- Commits: short, imperative; optional prefixes `feat:`, `fix:`, `refactor:`, `chore:`.
- PRs: clear description, rationale, scope; link issues; list behavioral/provider impacts; include tests.
- Before review: ensure `dotnet build` and `dotnet test` pass locally.

## Security & Configuration Tips
- Never commit secrets or real connection strings; use environment variables and user-secrets. Strong-name via `SNK_PATH` (do not commit keys).
- Do not hardcode identifier quoting. Use `WrapObjectName(...)` and `CompositeIdentifierSeparator` (e.g., ``var full = ctx.WrapObjectName("schema") + ctx.CompositeIdentifierSeparator + ctx.WrapObjectName("table");``).
- Always parameterize values (`AddParameterWithValue`, `CreateDbParameter`); avoid string interpolation for SQL.

