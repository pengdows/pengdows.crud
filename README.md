# pengdows.crud
[![NuGet](https://img.shields.io/nuget/v/pengdows.crud.svg)](https://www.nuget.org/packages/pengdows.crud)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Build](https://github.com/pengdows/pengdows.crud/actions/workflows/build.yml/badge.svg)](https://github.com/pengdows/pengdows.crud/actions/workflows/build.yml)

**Expert-level, high-performance, SQL-first data access for .NET 8+.**

`pengdows.crud` is an opinionated framework designed for developers who want the full power of SQL without the overhead or "magic" of a heavy ORM. It provides robust, production-ready patterns for connection management, concurrency, and multi-tenancy.

---

## 🚀 Key Features

- **SQL-First Philosophy**: You control the SQL; the framework handles the plumbing.
- **High Performance**: Compiled IL mappers, zero-reflection hydration, and optimized parameter binding.
- **Adaptive Connection Management**: 
  - **SingleWriter** Mode: Built-in turnstile for safe concurrent SQLite/DuckDB writes.
  - **Open Late, Close Early**: Maximizes connection pool efficiency.
- **Intelligent Dialect System**: Portable Upserts, Batching, and Paging across 13 databases.
- **Advanced Type System**: Native support for JSON, Spatial data, UUIDv7, and custom value objects.
- **Production Observability**: Built-in metrics for connection pools, command latency, and transaction health.
- **Multi-Tenancy**: First-class support for robust database-per-tenant architectures.

---

## 📦 Supported Databases

Tested and tuned for **13 directly supported databases**:

- **SQL Server** / Express / LocalDB
- **PostgreSQL** / Aurora PostgreSQL / TimescaleDB
- **MySQL** / Aurora MySQL
- **MariaDB**
- **Oracle**
- **SQLite**
- **Firebird**
- **DuckDB**
- **CockroachDB**
- **YugabyteDB**
- **TiDB**
- **Snowflake**
- **SQL-92** (Standard Fallback)

---

## 🛠 Quick Start

### 1. Define your Entity

```csharp
[Table("orders")]
public class Order
{
    [Id] // Surrogate Row ID
    [Column("id")]
    public long Id { get; set; }

    [PrimaryKey(1)] // Business Primary Key
    [Column("order_number")]
    public string OrderNumber { get; set; }

    [Column("details", DbType.Object)]
    [Json] // Automatic JSON serialization
    public OrderDetails Details { get; set; }

    [Version] // Optimistic Concurrency
    [Column("version")]
    public int Version { get; set; }
}
```

### 2. Basic CRUD

```csharp
// Singletons - Register once in DI
var gateway = new TableGateway<Order, long>(context, auditResolver);

// Create
var order = new Order { OrderNumber = "ORD-123", Details = new() { ... } };
await gateway.CreateAsync(order);

// Retrieve by Id
var result = await gateway.RetrieveOneAsync(12345);

// Update with Optimistic Concurrency
result.Details.Notes = "Updated";
int affected = await gateway.UpdateAsync(result); 
if (affected == 0) throw new ConcurrencyException();

// High-Performance Batching (New in 2.0)
await gateway.BatchCreateAsync(newListOfOrders);
```

---

## 📊 Observability & Metrics

`pengdows.crud` provides 36 top-level metrics out of the box. You can hook into the `MetricsUpdated` event or use the `Metrics` property on the context.

```csharp
context.MetricsUpdated += (s, metrics) => {
    Console.WriteLine($"P99 latency: {metrics.P99CommandMs}ms");
    Console.WriteLine($"Failed commands: {metrics.CommandsFailed}");
};
```

---

## 📖 Documentation

Full documentation is available in the [Wiki](https://github.com/pengdows/pengdows.crud/wiki).

### Core Concepts
- [TableGateway (CRUD API)](https://github.com/pengdows/pengdows.crud/wiki/v2-TableGateway)
- [Connection Management & DbMode](https://github.com/pengdows/pengdows.crud/wiki/v2-Connection-Management-and-DbMode)
- [Advanced Type System](https://github.com/pengdows/pengdows.crud/wiki/v2-Type-System)
- [Multi-Tenancy Guide](https://github.com/pengdows/pengdows.crud/wiki/v2-Multi-Tenancy)

---

## ⚖️ License

Licensed under the [MIT License](LICENSE).
