# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

pengdows.crud is a SQL-first, strongly-typed, testable data access layer for .NET 8. It's designed for developers who want full control over SQL without ORM magic. The project consists of multiple components:

- `pengdows.crud` - Core library with EntityHelper, DatabaseContext, and SQL dialects
- `pengdows.crud.abstractions` - Interfaces and enums
- `pengdows.crud.fakeDb` - Testing framework with fake database providers
- `pengdows.crud.Tests` - Comprehensive test suite
- `testbed` - Integration testing with real databases

## Core Architecture

The library follows a layered architecture with these key components:

### Main Entry Points
- **DatabaseContext**: Primary connection management class that wraps ADO.NET DbProviderFactory
- **EntityHelper<TEntity, TRowID>**: Generic CRUD operations for entities with strongly-typed row IDs
- **SqlContainer**: SQL query builder with parameterization support

### Key Patterns
- Entities use attributes for table/column mapping (`TableAttribute`, `ColumnAttribute`, `IdAttribute`)
- Audit fields supported via `CreatedBy/On`, `LastUpdatedBy/On` attributes
- SQL dialect abstraction supports multiple databases (SQL Server, PostgreSQL, Oracle, MySQL, SQLite, etc.)
- Connection strategies: Standard, KeepAlive, Shared, SingleWriter
- Multi-tenancy support via tenant resolution

### Directory Structure
- `pengdows.crud/` - Core implementation
  - `attributes/` - Entity mapping attributes
  - `dialects/` - Database-specific SQL generation
  - `connection/` - Connection management strategies  
  - `exceptions/` - Custom exception types
  - `isolation/` - Transaction isolation handling
  - `tenant/` - Multi-tenancy support
- `pengdows.crud.Tests/` - Unit and integration tests
- `testbed/` - Database provider testing infrastructure

## Development Commands

### Build and Test
```bash
# Build entire solution
dotnet build pengdows.crud.sln

# Run all tests
dotnet test pengdows.crud.Tests/pengdows.crud.Tests.csproj

# Run specific test by name
dotnet test --filter "MethodName=TestMethodName"

# Run tests for specific class
dotnet test --filter "ClassName=EntityHelperTests"

# Build with Release configuration
dotnet build -c Release
```

### Package Management
```bash
# Restore packages
dotnet restore

# Pack projects for NuGet
dotnet pack pengdows.crud/pengdows.crud.csproj -c Release
dotnet pack pengdows.crud.abstractions/pengdows.crud.abstractions.csproj -c Release
dotnet pack pengdows.crud.fakeDb/pengdows.crud.fakeDb.csproj -c Release
```

### Testing Infrastructure
The project includes extensive test coverage with both unit tests and integration tests. The `fakeDb` package provides mock database providers for testing without real database connections.

## Key Implementation Details

### Type Safety
- `TRowID` must be primitive integer, `Guid`, or `string` (nullable allowed)
- Automatic type coercion between .NET types and database types
- Enum support with configurable parsing behavior
- JSON serialization support for complex types

### SQL Generation
- Database-agnostic SQL with dialect-specific optimizations
- Automatic parameterization prevents SQL injection
- Support for MERGE statements where available (SQL Server, Oracle, Firebird, PostgreSQL 15+)
- Schema-aware operations with proper object name quoting

### Connection Management  
- Configurable connection lifecycle (New, Shared, KeepAlive)
- Transaction scoping via TransactionContext
- Isolation level management per database
- Connection strategy patterns for different use cases

## Working with the Codebase

When making changes:
- Follow existing patterns for SQL dialect implementations
- Use the attribute-based entity mapping consistently
- Ensure new features work across all supported database providers
- Add comprehensive tests including edge cases and error conditions
- Maintain backwards compatibility in public APIs