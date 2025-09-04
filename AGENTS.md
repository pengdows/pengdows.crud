# Repository Guidelines

## Project Structure & Module Organization
- Source: `pengdows.crud/` (core), `pengdows.crud.abstractions/` (interfaces), `pengdows.crud.fakeDb/` (in-memory/fake provider), `testbed/` (integration test suite using Testcontainers and real providers).
- Tests: `pengdows.crud.Tests/` (xUnit). Coverage artifacts stored under `TestResults/`.
- Solution: `pengdows.crud.sln`. GitHub Actions workflow in `.github/workflows/deploy.yml`.

## Build, Test, and Development Commands
- Restore: `dotnet restore`
- Build: `dotnet build pengdows.crud.sln -c Release` (treats warnings as errors in library projects).
- Test (local): `dotnet test -c Release --results-directory TestResults --logger trx`.
- Test with coverage (like CI):
  `dotnet test -c Release --results-directory TestResults -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Exclude="[pengdows.crud.Tests]*;[pengdows.crud.abstractions]*;[pengdows.crud.fakeDb]*;[testbed]*"`
- Pack (NuGet): `dotnet pack <project>.csproj -c Release`.
 - Run integration suite: `dotnet run -c Release --project testbed` (requires Docker for Testcontainers).

## Coding Style & Naming Conventions
- C# 12 on `net8.0`; `Nullable` and `ImplicitUsings` enabled.
- Use file-scoped namespaces (e.g., `namespace pengdows.crud;`). Keep existing lowercase namespaces (`pengdows.crud.*`).
- Indentation: 4 spaces; braces per current file style; prefer expression-bodied members when clearer.
- Keep public APIs minimal and documented; internal where possible. No warnings allowed (`WarningsAsErrors=true`).
- Prefer small, focused files in domain folders: `attributes/`, `dialects/`, `connection/`, `threading/`, `exceptions/`.
- Casing: refer to the project as `fakeDb` (lowercase f, uppercase D) in docs/paths (e.g., `pengdows.crud.fakeDb`). Type names remain PascalCase (e.g., `FakeDbConnection`).

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
- Framework: xUnit (`[Fact]`, `[Theory]`). Mocks: Moq.
- Place tests in `pengdows.crud.Tests/` mirroring namespaces; name files `*Tests.cs`.
- Avoid real DBs in unit tests; prefer `pengdows.crud.fakeDb`. Use `testbed/` for integration against real engines via Testcontainers (Docker required).
- Maintain or improve coverage; CI uploads Cobertura to Codecov from `TestResults/**/coverage.cobertura.xml`.

## Commit & Pull Request Guidelines
- Commits: short, imperative subject lines. Optional prefixes: `feat:`, `fix:`, `refactor:`, `chore:` (seen in history).
- PRs: clear description, rationale, and scope; link issues; list behavioral changes; include tests; note provider impacts (SQL Server/PostgreSQL/etc.).
- Ensure `dotnet build` and `dotnet test` pass locally before requesting review.

## Security & Configuration Tips
- Never commit secrets or real connection strings. Use environment variables and local user-secrets.
- Strong-name signing can be enabled via `SNK_PATH`; do not commit keys.
- Never hardcode identifier quoting. Always use `WrapObjectName(...)` from `IDatabaseContext`/`ISqlContainer` and `CompositeIdentifierSeparator` for dotted names. Example: `var full = ctx.WrapObjectName("schema") + ctx.CompositeIdentifierSeparator + ctx.WrapObjectName("table");`
- Always parameterize via `AddParameterWithValue` or `CreateDbParameter`; avoid string interpolation for values.

