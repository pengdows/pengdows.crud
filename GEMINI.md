# Gemini Code Assistant Context: pengdows.crud 2.0

This document provides a deep, contextual understanding of the `pengdows.crud` 2.0 framework for Gemini. It covers the project's core philosophy, its key differentiating features, and its intended use cases.

## Core Philosophy: The "Expert in a Box"

`pengdows.crud` is an opinionated, high-performance, SQL-first data access framework. It is designed to be more robust and feature-rich than a micro-ORM like Dapper, while retaining high performance and developer control, avoiding the pitfalls of heavier, abstraction-focused ORMs like EF Core.

The central design principle is to provide **"Prego features"**—expert-level, built-in solutions to difficult, real-world data access problems that developers often assume are handled by their tools but usually are not. The framework guides developers toward robust, scalable, and secure architectural patterns by default.

It is built on a **database-first** philosophy, treating the database schema as a primary, expertly-designed artifact.

## Key Differentiating Features

### 1. Advanced Connection Management (`DbMode`)

The framework provides intelligent, adaptive connection strategies to ensure optimal performance and resilience.

- **"Open Late, Close Early" Architecture:** In `Standard` mode (for server databases), connections are acquired from the provider's pool only at the moment of execution and released immediately after. This maximizes connection pool efficiency and prevents pool exhaustion under high load, a common issue with the "connection-per-request" pattern.
- **`SingleWriter` Mode:** For file-based databases like SQLite, this mode provides a unique, built-in solution for safe concurrent writes. An application-level "turnstyle" governor serializes write *tasks*, preventing database locking errors, while still using ephemeral connections for maximum efficiency.
- **`SingleConnection` Mode:** A dedicated mode for handling thread-safe access to a single, persistent connection, designed specifically for ephemeral `:memory:` databases, which is invaluable for testing.
- **`Best` Mode:** Automatically selects the safest and most performant `DbMode` based on the provider and connection string.

### 2. Intelligent Dialect System

A powerful abstraction layer that makes application code portable across different database vendors by handling database-specific quirks.

- **Portable Upsert:** Automatically translates a single `Upsert` command into the correct native SQL (`MERGE`, `INSERT ... ON CONFLICT`, etc.) for the target database.
- **Intelligent Prepared Statements:** Selectively enables or disables prepared statements based on what is most performant for the target database (e.g., ON for PostgreSQL, OFF for SQL Server).
- **Stored Procedure Wrapping (`ProcWrappingStyle`):** Automatically wraps stored procedure calls in the correct, vendor-specific syntax (`EXEC`, `CALL`, `BEGIN/END`, etc.).

### 3. Intent-Based Transaction Management (`IsolationProfile`)

Simplifies transaction management by allowing developers to specify their *intent* (e.g., `SafeNonBlockingReads`). The framework's `IsolationResolver` then maps this intent to the safest and most optimal `System.Data.IsolationLevel` for the target database.

### 4. Advanced Type System (`AdvancedTypeRegistry`)

A multi-layered, high-performance, and extensible type coercion system.
- Provides built-in support for advanced types like **JSON, spatial data, arrays, and network addresses**.
- Allows developers to register their own custom handlers and converters for domain-specific types, which are then used seamlessly for both parameter writing and data reading.

### 5. Robust Database-First Design Principles

The framework's design and tooling enforce sound database design.
- **`[Id]` vs. `[PrimaryKey]`:** A clear distinction is made between a surrogate key (`[Id]`, for stable foreign key references) and a natural/business primary key (`[PrimaryKey]`). This encourages correct normalization and ensures that core CRUD operations can always leverage an appropriate index.
- **`Uuid7Optimized`:** Provides a high-performance, RFC 9562-compliant UUIDv7 generator to create time-ordered, index-friendly surrogate keys.

### 6. Built-in Safety and Production-Ready Patterns

- **Resource Safety:** The strict use of `IAsyncDisposable` on `TransactionContext` and `SqlContainer` makes accidental connection leaks virtually impossible.
- **Audit Handling:** An `IAuditValueResolver` interface allows for easy, decoupled, and automatic population of audit columns (`CreatedBy`, `CreatedOn`, etc.).
- **Multi-Tenancy:** Offers first-class, out-of-the-box support for the robust **database-per-tenant** model via `ITenantContextRegistry`.

### 7. Comprehensive Metrics

Provides deep operational visibility by tracking detailed metrics for connections, contention (from the `PoolGovernor`), command timings, transactions, and more. This is invaluable for debugging, performance tuning, and production monitoring.

## Related Projects

- **`pengdows.poco.mint`:** A code generation tool that inspects a database schema and generates C# POCOs with the correct `[Table]`, `[Column]`, `[Id]`, and `[PrimaryKey]` attributes for use with `pengdows.crud`.
- **`pengdows.crud.fakeDb`:** A powerful, standalone NuGet package that provides a fake ADO.NET provider. It is essential for writing fast, isolated unit tests for any data access logic based on ADO.NET interfaces, including code that uses `pengdows.crud` or Dapper.

## Project Mandates

### 1. Test-Driven Development (TDD) Required
All new functionality, dialect additions, and bug fixes MUST be implemented using a TDD approach. Tests must be written and verified before or alongside the implementation.

### 2. High Coverage Standards
The project CI enforces a minimum of 83% line coverage. However, for all new 2.0 work, a target of **95% coverage** is expected. A change is not considered complete without corresponding unit tests (in `pengdows.crud.Tests`) and, where applicable, integration tests (in `testbed`).

### 3. Interface-First Design
All public APIs must be exposed via interfaces in `pengdows.crud.abstractions`. Implementation details should remain internal to `pengdows.crud` whenever possible.

## Building and Testing

- **Build:** `./build-packages.sh`
- **Unit Tests:** `./run-unit-tests.sh` (uses `fakeDb`)
- **Integration Tests:** `./run-integration-tests.sh` (uses Testcontainers)
- **Benchmarks:** `dotnet run --project benchmarks/CrudBenchmarks -- --filter '*MyBenchmark*'` (run from within the `2.0` directory).
