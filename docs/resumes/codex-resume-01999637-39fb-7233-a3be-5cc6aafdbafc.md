# Codex Resume: pengdows.crud

## Identifier
- Resume ID: 01999637-39fb-7233-a3be-5cc6aafdbafc

## Project Overview
- **Product**: `pengdows.crud` — a SQL-first, strongly-typed data access layer targeting .NET 8 consumers who want deterministic SQL without ORM abstraction.
- **Architecture**: Modular solution split into the core library, abstractions, and a fake database provider, with integration scaffolding housed in the `testbed` project.
- **Philosophy**: Favor explicit SQL composition, predictable behavior across providers, and comprehensive audit/transaction support.

## Highlights
- Multi-database compatibility via dialect abstractions covering SQL Server, PostgreSQL, Oracle, MySQL/MariaDB, SQLite, Firebird, and CockroachDB.
- Entity mapping powered by attributes (`Table`, `Column`, `Id`, etc.) and helper classes that build composable, parameterized SQL containers.
- Built-in audit field resolution, connection mode strategies, and UUIDv7 generation for high-throughput identifier creation.
- Extensive documentation and conventions enforcing parameterization, UTC auditing, and rigorous transaction handling.

## Key Artifacts
- **Core Library** (`pengdows.crud/`): Contains `DatabaseContext`, `EntityHelper`, SQL container utilities, dialect implementations, and type coercion helpers.
- **Abstractions** (`pengdows.crud.abstractions/`): Interface definitions enabling consumer customization and external provider support.
- **Fake Database Provider** (`pengdows.crud.fakeDb/`): In-memory provider for fast, deterministic unit testing without real connections.
- **Tests** (`pengdows.crud.Tests/`): xUnit-based suite with positive and negative coverage expectations.
- **Integration Harness** (`testbed/`): Testcontainers-driven scenarios for cross-provider verification.
- **Docs & Guides** (`docs/`): Connection management rules, parameter naming conventions, and this resume.

## Quality & Tooling
- Primary workflows use `dotnet build`/`dotnet test` (Release configuration) with coverage publishing via Codecov and benchmarks under `benchmarks/CrudBenchmarks/`.
- Repository enforces clean architecture practices, comprehensive unit and integration testing, and adherence to SQL parameterization best practices.
- FakeDb infrastructure supports advanced failure simulation to harden connection handling paths.

## Usage Snapshot
```csharp
var context = new DatabaseContext("your-connection-string", SqlClientFactory.Instance);
var helper = new EntityHelper<TestTable, long>(context);
var insert = helper.BuildCreate(row);
await insert.ExecuteNonQueryAsync();
var found = await helper.RetrieveOneAsync(row.Id);
```

## Contact Notes
- Audit value resolution hooks (`IAuditValueResolver`) allow injecting user/time metadata.
- Transaction scopes (`TransactionContext`) coordinate shared connections across read/write operations.
- Extension points include SQL dialect customization, provider loaders, and tenant-aware helpers.
