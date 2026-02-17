# Gemini Code Assistant Context

This document provides context for the `pengdows.crud` project to help the Gemini code assistant understand the codebase.

## Project Overview

`pengdows.crud` is a high-performance, SQL-first data access layer for .NET. It is designed to be a lightweight alternative to heavier ORMs like Entity Framework Core, while providing more features and better testability than micro-ORMs like Dapper.

The core philosophy of `pengdows.crud` is to give developers full control over their SQL, while providing a robust and testable framework for database interactions.

### Key Technologies

*   **.NET:** The project is built on .NET and targets `net8.0` and `net10.0`. The `global.json` specifies the use of .NET SDK version `10.0.100`.
*   **C#:** The language used for the project.
*   **Supported Databases:** The library supports a wide range of databases, including:
    *   SQL Server
    *   PostgreSQL
    *   MySQL / MariaDB
    *   Oracle
    *   SQLite
    *   Firebird
    *   DuckDB
    *   CockroachDB
    *   TimescaleDB

### Architecture

The project is structured into several key components:

*   `pengdows.crud`: The core library containing the main data access logic.
*   `pengdows.crud.abstractions`: Contains the public interfaces and abstractions for the library.
*   `pengdows.crud.fakeDb`: A fake database provider for unit testing. This is a key feature that allows for fast and isolated tests.
*   `pengdows.crud.Tests`: The unit test project, which uses the `fakeDb` provider.
*   `pengdows.crud.IntegrationTests`: The integration test project, which uses Testcontainers to test against real databases.
*   `benchmarks`: Contains performance benchmarks for the library.

The central class in the library is `DatabaseContext`. It is designed to be a **singleton** per connection string and is thread-safe. This is a critical distinction from Entity Framework's `DbContext`, which is typically scoped to a request.

The library features a sophisticated connection management system with different modes (`Standard`, `KeepAlive`, `SingleWriter`, `SingleConnection`) that are automatically selected based on the database provider and connection string.

## Building and Running

### Building the Project

To build the project and create NuGet packages, run the following command:

```bash
./build-packages.sh
```

This will create the packages in the `artifacts` directory.

### Running Tests

There are two types of tests in the project: unit tests and integration tests.

**Unit Tests:**

The unit tests are designed to be fast and run without a real database, using the `pengdows.crud.fakeDb` provider. To run the unit tests, use the following command:

```bash
./run-unit-tests.sh
```

This script will also collect and report code coverage.

**Integration Tests:**

The integration tests run against real databases using Testcontainers. To run the integration tests, use the following command:

```bash
./run-integration-tests.sh
```

## Development Conventions

*   **SQL-First:** The library is designed around the principle of writing raw SQL. It does not have a LINQ provider.
*   **Test-Driven Development (TDD):** The `README.md` states that TDD is mandatory for contributions.
*   **High Test Coverage:** The project aims for a high test coverage (minimum 83%).
*   **Singleton `DatabaseContext`:** As mentioned in the architecture section, `DatabaseContext` instances should be registered as singletons in the dependency injection container.
*   **Explicit Transactions:** Transactions are not implicit. They should be created explicitly using `context.BeginTransaction()`.
*   **Partial Classes:** The `DatabaseContext` class is split into multiple partial class files (`DatabaseContext.cs`, `DatabaseContext.Initialization.cs`, etc.) to organize its extensive functionality.
*   **Internal Documentation:** The `docs/ARCHITECTURE.md` file contains detailed information about the internal workings of the library. It is a valuable resource for understanding the design decisions and implementation details.
