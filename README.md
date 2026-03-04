# pengdows.crud
[![NuGet](https://img.shields.io/nuget/v/pengdows.crud.svg)](https://www.nuget.org/packages/pengdows.crud)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Build](https://github.com/pengdows/pengdows.crud/actions/workflows/deploy.yml/badge.svg)](https://github.com/pengdows/pengdows.crud/actions)
[![Coverage](https://codecov.io/gh/pengdows/pengdows.crud/branch/main/graph/badge.svg)](https://codecov.io/gh/pengdows/pengdows.crud)

**pengdows.crud** is a SQL-first, strongly-typed, testable data access layer for .NET. It's built for developers who want **full control** over SQL, **predictable behavior** across databases, and **no ORM magic**.

> No LINQ. No tracking. No surprises.

---

## 🔍 Why pengdows.crud?

- **SQL-first**: You write the SQL, we provide safety, parameterization, and cross-database portability
- **Multi-tenant SaaS ready**: Each tenant can use a **different database type** (PostgreSQL for one, SQLite for another, SQL Server for another)
- **Advanced type support**: Spatial types, network types, intervals, ranges, JSON, streaming LOBs—14 specialized converters
- **Production observability**: 23+ real-time metrics (P95/P99 latency, connection tracking, query performance)
- **Memory-efficient streaming**: Process millions of rows without loading into memory via `IAsyncEnumerable<T>`
- **Superior testability**: Complete fakeDb ADO.NET provider for fast unit tests + Testcontainers for integration tests
- **Automatic optimization**: Database-specific features detected and used (MERGE, ON CONFLICT, set-valued params, RETURNING clauses)

---

## 🎯 Unique Features (vs EF Core & Dapper)

### Multi-Tenancy with Mixed Database Types

**pengdows.crud EXCLUSIVE**: Each tenant can use a completely different database engine.

```csharp
// Tenant A uses SQL Server
services.AddKeyedSingleton<DatabaseContext>("enterprise-client", sp => {
    var config = new DatabaseContextConfiguration {
        ConnectionString = "Server=azure.com;Database=enterprise;",
        ProviderName = "System.Data.SqlClient"
    };
    return new DatabaseContext(config, SqlClientFactory.Instance);
});

// Tenant B uses PostgreSQL
services.AddKeyedSingleton<DatabaseContext>("startup-client", sp => {
    var config = new DatabaseContextConfiguration {
        ConnectionString = "Host=localhost;Database=startup;",
        ProviderName = "Npgsql"
    };
    return new DatabaseContext(config, NpgsqlFactory.Instance);
});

// Tenant C uses SQLite
services.AddKeyedSingleton<DatabaseContext>("embedded-client", sp => {
    var config = new DatabaseContextConfiguration {
        ConnectionString = "Data Source=embedded.db",
        ProviderName = "Microsoft.Data.Sqlite"
    };
    return new DatabaseContext(config, SqliteFactory.Instance);
});

// Or use built-in multi-tenant registry
services.AddMultiTenancy(configuration);  // Loads from appsettings.json

var registry = services.GetRequiredService<ITenantContextRegistry>();
var enterpriseCtx = registry.GetContext("enterprise-client");  // SQL Server
var startupCtx = registry.GetContext("startup-client");        // PostgreSQL
```

**Neither EF Core nor Dapper has this.** Build true multi-tenant SaaS with database flexibility per customer.

### Advanced Type Support

**14 specialized type converters** for database-specific types:

```csharp
// Spatial types (SQL Server, PostgreSQL, MySQL, Oracle)
public class Location
{
    [Column("point", DbType.Object)]
    public Geometry Point { get; set; }  // WKT, WKB, EWKB support

    [Column("region", DbType.Object)]
    public Geography Area { get; set; }
}

// PostgreSQL network types
public class NetworkDevice
{
    [Column("ip", DbType.Object)]
    public Inet IpAddress { get; set; }  // inet type

    [Column("mac", DbType.Object)]
    public MacAddress MacAddress { get; set; }

    [Column("subnet", DbType.Object)]
    public Cidr Subnet { get; set; }  // CIDR notation
}

// PostgreSQL intervals and ranges
public class Booking
{
    [Column("duration", DbType.Object)]
    public PostgreSqlInterval Duration { get; set; }

    [Column("dates", DbType.Object)]
    public Range<DateOnly> BookingPeriod { get; set; }
}

// Streaming LOBs (memory-efficient)
public class Document
{
    [Column("binary_content", DbType.Object)]
    public Stream BlobContent { get; set; }  // Streams without loading into memory

    [Column("text_content", DbType.Object)]
    public TextReader ClobContent { get; set; }
}

// Optimistic concurrency
public class Entity
{
    [Version]
    [Column("row_version", DbType.Binary)]
    public RowVersion Version { get; set; }
}
```

**EF Core** has limited spatial support. **Dapper** has none. **pengdows.crud** supports all of these natively.

### Real-Time Metrics & Observability

**23+ metrics available** (enable metrics in context configuration):

```csharp
var config = new DatabaseContextConfiguration
{
    ConnectionString = connectionString,
    EnableMetrics = true
};
var context = new DatabaseContext(config, factory);

// Get real-time snapshot
var metrics = context.Metrics;

Console.WriteLine($"Current connections: {metrics.ConnectionsCurrent}");
Console.WriteLine($"Peak open connections: {metrics.PeakOpenConnections}");
Console.WriteLine($"Avg command duration: {metrics.AvgCommandMs}ms");
Console.WriteLine($"P95 latency: {metrics.P95CommandMs}ms");
Console.WriteLine($"P99 latency: {metrics.P99CommandMs}ms");
Console.WriteLine($"Failed commands: {metrics.CommandsFailed}");
Console.WriteLine($"Rows read: {metrics.RowsReadTotal}");
Console.WriteLine($"Read connections: {metrics.Read.ConnectionsCurrent}");
Console.WriteLine($"Write connections: {metrics.Write.ConnectionsCurrent}");

// Subscribe to metrics updates
context.MetricsUpdated += (sender, m) =>
{
    if (m.AvgCommandMs > 1000)
        _logger.LogWarning("Slow queries detected!");

    if (m.ConnectionsCurrent > 50)
        _logger.LogWarning("High connection count!");
};
```

**Metrics tracked:**
- Connection lifecycle (opened, closed, current, max, reused, long-lived)
- Connection timing (avg hold time, open time, close time)
- Command execution (total, failed, timed-out, cancelled)
- Command latency (average, P95, P99 percentiles)
- Row operations (total read, total affected)
- Prepared statement cache (cached, evicted, total)
- Transaction tracking (active, max, average duration)
- Read/write split metrics (per-role counts and timings)

**Neither EF Core nor Dapper has built-in metrics.**

### Memory-Efficient Streaming

Process millions of rows without memory pressure:

```csharp
// Stream 10 million orders - only 1 in memory at a time
await foreach (var order in helper.RetrieveStreamAsync(orderIds, cancellationToken))
{
    await ExportToFileAsync(order);

    if (++count % 10000 == 0)
        _logger.LogInformation($"Processed {count} orders");
}

// Stream with custom SQL
var container = helper.BuildBaseRetrieve("o");
container.Query.Append(" WHERE status = 'Active' ORDER BY created_at");

await foreach (var order in helper.LoadStreamAsync(container, cancellationToken))
{
    await ProcessAsync(order);
}
```

**EF Core** has `IAsyncEnumerable` but with tracking overhead. **Dapper** has no streaming—it loads everything into memory.

### Intelligent Connection Strategies

**4 connection modes with automatic detection:**

```csharp
// Production servers - Standard mode (auto-detected)
var ctx = new DatabaseContext(pgConnectionString, NpgsqlFactory.Instance);
// Each operation gets ephemeral connection from pool

// SQL Server LocalDB - KeepAlive mode (auto-detected)
var ctx = new DatabaseContext(localDbConnectionString, SqlClientFactory.Instance);
// Sentinel connection prevents database unload

// SQLite file - SingleWriter mode (auto-detected)
var ctx = new DatabaseContext("Data Source=mydb.db", SqliteFactory.Instance);
// Per-operation connections with a single-writer permit
// Prevents SQLITE_BUSY errors

// SQLite :memory: - SingleConnection mode (REQUIRED, auto-detected)
var ctx = new DatabaseContext("Data Source=:memory:", SqliteFactory.Instance);
// All operations through single connection
// Each :memory: connection = separate database
```

**Benefits:**
- Prevents `SQLITE_BUSY` errors automatically
- Handles `:memory:` correctly (each connection = separate database)
- LocalDB unload prevention
- Optimal connection pooling per database type

#### Shared connection locking & timeouts
`SingleWriter` and `SingleConnection` (and the sentinel connection kept open in `KeepAlive`) rely on `RealAsyncLocker` to serialize access to the pinned connection. The lock has a default `ModeLockTimeout` of 30 seconds (`DatabaseContextConfiguration.ModeLockTimeout` / `IDatabaseContextConfiguration.ModeLockTimeout`), and if an operation cannot grab the shared lock within that window a `ModeContentionException` is thrown with a contention snapshot. You can tune or disable the timeout to match your workload; the lock statistics remain available for logging/metrics so you can detect backpressure before it impacts throughput.

#### Connection pool governance
`DatabaseContext` installs lightweight `PoolGovernor` instances for the read and write pools in every mode except `SingleConnection`, enforcing a `PoolAcquireTimeout` of 5 seconds (`DatabaseContextConfiguration.PoolAcquireTimeout`). Each governor tracks active requests and hands out `PoolSlot` tokens before opening provider connections; the KeepAlive sentinel retains its slot while pinned. If the governor cannot deliver a slot within the timeout, a `PoolSaturatedException` is raised along with a snapshot of the queue and slot counts, so you fail fast instead of exhausting the provider pool. Override `MaxConcurrentReads`/`MaxConcurrentWrites` (legacy `ReadPoolSize`/`WritePoolSize`) to tune effective limits.

**Neither EF Core nor Dapper has connection strategy abstraction.**

### Database-Specific Optimizations (Automatic)

pengdows.crud **detects and uses native features** automatically:

```csharp
// Same code, different optimizations per database

// SQL Server: Uses MERGE
await helper.UpsertAsync(entity);
// MERGE INTO table USING (VALUES ...) ON (id) WHEN MATCHED THEN UPDATE ...

// PostgreSQL: Uses INSERT ... ON CONFLICT
await helper.UpsertAsync(entity);
// INSERT INTO table ... ON CONFLICT (id) DO UPDATE SET ...

// MySQL: Uses INSERT ... ON DUPLICATE KEY UPDATE
await helper.UpsertAsync(entity);
// INSERT INTO table ... ON DUPLICATE KEY UPDATE ...

// PostgreSQL: Set-valued parameters (1 param instead of 1000!)
var orders = await helper.RetrieveAsync(new[] { 1, 2, 3, ..., 1000 });
// SELECT * FROM orders WHERE id = ANY($1::int[])
// Single array parameter instead of 1000 individual parameters
```

**12 database dialects** with feature detection:
- SQL Server (version-specific features)
- PostgreSQL (15+, 18+ capabilities)
- MySQL / MariaDB
- Oracle
- SQLite (3.35+, 3.45+ features)
- Firebird
- DuckDB
- CockroachDB
- TimescaleDB

---

-## ✅ Core Features

> **Note:** Version 2.0 removes the legacy compatibility alias; use `TableGateway<TEntity, TRowID>` directly for CRUD.
- **`TableGateway<TEntity, TRowID>`**: Automatic CRUD with SQL customization points
- **`SqlContainer`**: Safe SQL composition with parameterization
- **Flexible audit tracking**: Choose which fields (CreatedBy/On, LastUpdatedBy/On), their types (string, int, Guid), and how to populate them
- **Automatic optimistic locking**: Version fields with automatic increment and concurrency conflict detection
- **Portable isolation levels**: `IsolationProfile` enum maps to database-specific levels
- **Stored procedure wrapping**: 6 dialect-specific strategies (EXEC, CALL, EXECUTE, Oracle, PostgreSQL)
- **Type coercion**: Automatic conversion between CLR and database types
- **JSON support**: `JsonValue` struct with PreferDocument/PreferText modes
- **Time mapping**: `TimeMappingPolicy` (PreferDateTimeOffset vs ForceUtcDateTime)
- **Enum handling**: Configurable parsing (Throw vs Default on failure)
- **Parameter caching**: Predictable parameter naming for SQL container reuse
- **Prepared statement caching**: Automatic statement preparation and reuse
- **Connection pooling detection**: Automatic pool size detection across all providers

---

## 🧩 Supported Databases

Tested and tuned for 13 directly supported databases:

- **SQL Server** / Express / LocalDB
- **PostgreSQL** / Aurora PostgreSQL / TimescaleDB
- **MySQL** / MariaDB / Aurora MySQL
- **Oracle**
- **SQLite**
- **Firebird**
- **DuckDB**
- **CockroachDB**
- **YugabyteDB**
- **TiDB**
- **Snowflake**

> All tested against .NET 8 with native ADO.NET providers. Must support `DbProviderFactory` and `GetSchema("DataSourceInformation")`.


### Default Pool Sizes (Provider vs Practical)

| SupportedDatabase | Default Max Pool Size (provider) | Practical / Recommended Max Pool Size | Key Practical Limits & Advice |
|-------------------|----------------------------------|---------------------------------------|-------------------------------|
| SqlServer (Microsoft.Data.SqlClient) | 100 | 50-200 (often 100-150 safe) | Per app instance rarely >200; total server connections limited by memory (approx 10-20 KB per conn + query plans). Rule of thumb: 2-4x CPU cores per app instance, or 100-300 total cluster-wide. Large pools (>500) often cause context switching thrash on DB server. |
| PostgreSql (Npgsql) | 100 (since ~3.1) | 20-100 per app instance (often 30-80 optimal) | Strong consensus: 2-4x CPU cores on the DB server. Each conn ~1-3 MB RAM on Postgres side. >100-150 often overloads small/medium instances. Use PgBouncer if >50-100 needed per app; set app pool to 20-50 and let PgBouncer multiplex. |
| MySql / MariaDb (MySqlConnector / MySql.Data) | 100 | 50-200 (often 100-150) | Similar to SqlServer: 100 is safe default. Threads are lighter than Postgres but still ~1-2 MB per conn. Practical ceiling often 200-500 before thread contention or memory pressure. ProxySQL or MySQL Router recommended beyond ~200. |
| Oracle (Oracle.ManagedDataAccess) | 100 | 50-200 | Sessions are heavier (few MB each). Practical max often 100-300 before session/memory limits kick in. Enterprise tuning often caps at 100-150 per instance. |
| Sqlite (Microsoft.Data.Sqlite) | Effectively unlimited (pooling enabled by default since v6, no hard max) | 1-20 (or unlimited for in-memory) | Single-writer lock means >1-4 concurrent writers kills perf. Practical: keep pool small (5-20) or disable pooling for high concurrency. In-memory/shared can handle more, but still file-lock limited on disk. |
| DuckDb (.NET DuckDB) | Effectively unlimited (no hard pool limit in most impls) | 1-8 (or up to threads count) | Embedded: connection creation is cheap. Practical: single connection often best; multiple only if parallelizing queries. Limit to CPU cores or threads setting. No real pool exhaustion; bottleneck is CPU/RAM for queries, not connections. |
| Sql92 fallback / unknown | 100 | 50-100 | Conservative defaults for generic relational DBs. |

---

## ❌ Not Supported

Due to missing or outdated .NET providers:

- TimesTen
- DB2 (not planned)
- Informix
- Sybase ASE (not planned)
- SQL Anywhere

Want support? Ask the vendor to ship a **real** ADO.NET provider.

---

## 🚫 Not an ORM — On Purpose

`pengdows.crud` doesn't:
- Track entities
- Auto-generate complex queries
- Obfuscate SQL
- Use lazy loading
- Implement identity maps

Instead, it helps you write **real SQL** that's:
- **Predictable** - You know exactly what SQL executes
- **Testable** - Complete fakeDb provider for fast unit tests
- **Secure** - Automatic parameterization, no SQL injection
- **Observable** - 23+ real-time metrics
- **Portable** - Cross-database with database-specific optimizations

---

## 🧠 Philosophy

- **Primary keys ≠ pseudokeys** - Separate logical IDs from physical keys
- **Open late, close early** - Minimize connection lifetime
- **Parameterize everything** - Always use parameters, never string concatenation
- **Audit everything** - Store in UTC with automatic CreatedBy/On, LastUpdatedBy/On
- **Don't assume** - Use provider metadata (`DbProviderFactory`, `GetSchema`)
- **Test in production-like environments** - fakeDb for unit tests, Testcontainers for integration
- **Explicit over implicit** - No magic, no surprises

---

## 🔬 Comprehensive Tool Comparison

| Feature | pengdows.crud | EF Core | Dapper |
|---------|---------------|---------|--------|
| **Multi-Tenancy (Mixed DB Types)** | ✅ **Built-in registry** | ❌ Manual | ❌ None |
| **Read-Only Enforcement** | ✅ **ConnString + Session SQL** | ❌ None | ❌ None |
| **Advanced Types (Spatial, Network, Ranges)** | ✅ **14 converters** | ⚠️ Limited | ❌ None |
| **Streaming Large Results** | ✅ **IAsyncEnumerable** | ⚠️ With overhead | ❌ Loads all |
| **Real-Time Metrics (P95/P99, Connections)** | ✅ **23+ metrics** | ⚠️ Logging only | ❌ None |
| **Connection Strategies (4 modes)** | ✅ **Auto-detected** | ❌ None | ❌ Manual |
| **Portable Isolation Levels** | ✅ **IsolationProfile** | ❌ DB-specific | ❌ Manual |
| **Database Dialects** | ✅ **12 with feature detection** | ⚠️ Via providers | ⚠️ Manual |
| **Set-Valued Parameters (PostgreSQL)** | ✅ **ANY($1::int[])** | ❌ None | ❌ None |
| **LOB Streaming** | ✅ **Stream/TextReader** | ❌ None | ❌ None |
| **Testing Infrastructure** | ✅ **Complete fakeDb provider** | ⚠️ InMemory | ⚠️ Mocks |
| **Stored Procedure Wrapping** | ✅ **6 dialect strategies** | ⚠️ Limited | ✅ Good |
| **Flexible Audit Tracking** | ✅ **Any type (string/int/Guid), custom resolvers** | ⚠️ Manual SaveChanges | ❌ None |
| **Optimistic Locking** | ✅ **Automatic version increment** | ✅ Manual concurrency tokens | ❌ Manual |
| **SQL Control** | ✅ **Full** | ❌ LINQ only | ✅ Full |
| **Safe Parameterization** | ✅ **Automatic** | ✅ Automatic | ⚠️ Manual |
| **Query Performance** | ✅ **Excellent** | ⚠️ Good | ✅ Excellent |
| **Change Tracking** | ❌ Explicit | ✅ Automatic | ❌ None |
| **LINQ** | ❌ By design | ✅ Full | ❌ None |
| **Migrations** | ❌ By design | ✅ Built-in | ❌ None |

---

## 📚 Documentation

Topics include:

- **Multi-tenancy**: `TenantContextRegistry`, mixing database types per tenant
- **Advanced types**: Spatial, network, intervals, ranges, LOBs, JSON
- **Metrics**: Real-time observability and performance tracking
- **Streaming**: Memory-efficient `IAsyncEnumerable<T>` operations
- **Connection strategies**: Standard, KeepAlive, SingleWriter, SingleConnection
- **TableGateway**: CRUD operations with SQL customization (replaces the older TableGateway alias)
- **SqlContainer**: Safe SQL composition and parameterization
- **DbMode**: Connection lifecycle management
- **Audit tracking**: Automatic CreatedBy/On, LastUpdatedBy/On
- **Transaction scopes**: IsolationProfile, transaction management
- **Type coercion**: CLR to database type mapping
- **Primary vs. pseudokeys**: Separating logical IDs from physical keys
- **Dialect system**: Database-specific SQL generation and feature detection
- **Testing**: fakeDb provider and Testcontainers integration

---

## 🛠️ Getting Started

### Installation

```bash
dotnet add package pengdows.crud
```

For testing without a real database:

```bash
dotnet add package pengdows.crud.fakeDb
```

### Basic Usage

```csharp
using System.Data.SqlClient;
using pengdows.crud;
using pengdows.crud.configuration;

var config = new DatabaseContextConfiguration {
    ConnectionString = "your-connection-string",
    ProviderName = "System.Data.SqlClient"
};
var context = new DatabaseContext(config, SqlClientFactory.Instance);

// Execute raw SQL
var sc = context.CreateSqlContainer();
sc.Query.Append("SELECT CURRENT_TIMESTAMP");
var dt = await sc.ExecuteScalarRequiredAsync<DateTime>();
```

### Entity Mapping

```csharp
using System.Data;
using pengdows.crud;
using pengdows.crud.configuration;

[Table("users")]
public class User
{
    [Id]
    [Column("id", DbType.Int64)]
    public long Id { get; set; }

    [PrimaryKey(1)]
    [Column("email", DbType.String)]
    public string Email { get; set; }

    [Column("name", DbType.String)]
    public string Name { get; set; }

    [Column("status", DbType.String)]
    [EnumColumn(typeof(UserStatus))]
    public UserStatus Status { get; set; }

    [CreatedOn]
    public DateTime CreatedAt { get; set; }

    [LastUpdatedOn]
    public DateTime? UpdatedAt { get; set; }
}

public enum UserStatus { Active, Inactive, Suspended }

var config = new DatabaseContextConfiguration {
    ConnectionString = "connection-string",
    ProviderName = "System.Data.SqlClient"
};
var context = new DatabaseContext(config, SqlClientFactory.Instance);
var helper = new TableGateway<User, long>(context);

// Create
var user = new User { Email = "john@example.com", Name = "John", Status = UserStatus.Active };
await helper.CreateAsync(user);  // user.Id populated, CreatedAt set automatically

// Retrieve
var found = await helper.RetrieveOneAsync(user.Id);

// Update
found.Name = "John Doe";
await helper.UpdateAsync(found);  // UpdatedAt set automatically

// Delete
await helper.DeleteAsync(found.Id);

// Stream large result sets
await foreach (var u in helper.RetrieveStreamAsync(userIds))
{
    await ProcessUserAsync(u);
}
```

### Multi-Tenancy Setup

```csharp
// appsettings.json
{
  "MultiTenant": {
    "Tenants": [
      {
        "Name": "enterprise-client",
        "DatabaseContextConfiguration": {
          "ConnectionString": "Server=sql.azure.com;Database=enterprise;",
          "ProviderName": "System.Data.SqlClient",
          "DbMode": "Standard"
        }
      },
      {
        "Name": "startup-client",
        "DatabaseContextConfiguration": {
          "ConnectionString": "Host=localhost;Database=startup;",
          "ProviderName": "Npgsql",
          "DbMode": "Standard"
        }
      }
    ]
  }
}

// Startup.cs
services.AddMultiTenancy(configuration);

// Register database factories
services.AddKeyedSingleton<DbProviderFactory>("System.Data.SqlClient",
    (_, _) => SqlClientFactory.Instance);
services.AddKeyedSingleton<DbProviderFactory>("Npgsql",
    (_, _) => NpgsqlFactory.Instance);

// Usage
var registry = services.GetRequiredService<ITenantContextRegistry>();
var enterpriseCtx = registry.GetContext("enterprise-client");  // SQL Server
var startupCtx = registry.GetContext("startup-client");        // PostgreSQL
```

### Flexible Audit Tracking

**Choose your audit fields and types** - pengdows.crud gives you complete control:

```csharp
// Time-only audit (no resolver needed)
[Table("products")]
public class Product
{
    [Id]
    [Column("id", DbType.Int64)]
    public long Id { get; set; }

    [Column("name", DbType.String)]
    public string Name { get; set; }

    [CreatedOn]  // Auto-populated with DateTime.UtcNow on INSERT
    [Column("created_at", DbType.DateTime)]
    public DateTime CreatedAt { get; set; }

    [LastUpdatedOn]  // Auto-updated on every UPDATE
    [Column("updated_at", DbType.DateTime)]
    public DateTime? UpdatedAt { get; set; }
}

// String-based user IDs
[Table("orders")]
public class Order
{
    [Id]
    [Column("id", DbType.Int32)]
    public int Id { get; set; }

    [CreatedBy]  // Populated from resolver
    [Column("created_by", DbType.String)]
    public string CreatedBy { get; set; }

    [LastUpdatedBy]
    [Column("updated_by", DbType.String)]
    public string LastUpdatedBy { get; set; }
}

// Integer user IDs
[Table("invoices")]
public class Invoice
{
    [Id]
    [Column("id", DbType.Int32)]
    public int Id { get; set; }

    [CreatedBy]
    [Column("created_by_user_id", DbType.Int32)]
    public int CreatedByUserId { get; set; }

    [LastUpdatedBy]
    [Column("updated_by_user_id", DbType.Int32)]
    public int UpdatedByUserId { get; set; }
}

// Guid user IDs
[Table("documents")]
public class Document
{
    [Id]
    [Column("id", DbType.Guid)]
    public Guid Id { get; set; }

    [CreatedBy]
    [Column("created_by", DbType.Guid)]
    public Guid CreatedBy { get; set; }

    [CreatedOn]
    [Column("created_at", DbType.DateTime)]
    public DateTime CreatedAt { get; set; }
}

// Full audit with optimistic locking
[Table("accounts")]
public class Account
{
    [Id]
    [Column("id", DbType.Int64)]
    public long Id { get; set; }

    [Column("balance", DbType.Decimal)]
    public decimal Balance { get; set; }

    [Version]  // Automatic optimistic locking
    [Column("version", DbType.Int32)]
    public int Version { get; set; }

    [CreatedBy]
    [Column("created_by", DbType.String)]
    public string CreatedBy { get; set; }

    [CreatedOn]
    [Column("created_at", DbType.DateTime)]
    public DateTime CreatedAt { get; set; }

    [LastUpdatedBy]
    [Column("updated_by", DbType.String)]
    public string LastUpdatedBy { get; set; }

    [LastUpdatedOn]
    [Column("updated_at", DbType.DateTime)]
    public DateTime UpdatedAt { get; set; }
}
```

**Custom audit resolver** - pull user info from anywhere:

```csharp
// HTTP context resolver
public class HttpAuditResolver : IAuditValueResolver
{
    private readonly IHttpContextAccessor _accessor;

    public HttpAuditResolver(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public IAuditValues Resolve()
    {
        var userId = _accessor.HttpContext?.User
            .FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";

        return new AuditValues { UserId = userId };
    }
}

// Register in DI
builder.Services.AddScoped<IAuditValueResolver, HttpAuditResolver>();

// TableGateway uses it automatically
var helper = new TableGateway<Order, int>(context, auditResolver);
await helper.CreateAsync(order, context);
// CreatedBy and LastUpdatedBy automatically populated from HTTP context!
```

### Automatic Optimistic Locking

**Version fields prevent lost updates** - zero configuration needed:

```csharp
[Table("accounts")]
public class Account
{
    [Id]
    [Column("id", DbType.Int32)]
    public int Id { get; set; }

    [Column("balance", DbType.Decimal)]
    public decimal Balance { get; set; }

    [Version]  // Marks this as version field
    [Column("version", DbType.Int32)]
    public int Version { get; set; }
}

// Concurrency protection
var account = await helper.RetrieveOneAsync(accountId);  // Version = 5

// Another user updates the account
// Version becomes 6 in the database

// Try to update with stale data
account.Balance += 100;
var rowsAffected = await helper.UpdateAsync(account);  // Version still = 5

// Result: 0 rows affected!
// SQL: UPDATE accounts SET balance = @p0, version = version + 1
//      WHERE id = @id AND version = @v5
// Database has version = 6, so WHERE clause doesn't match

// Detect concurrency conflict
if (rowsAffected == 0)
{
    // Reload and retry, or notify user of conflict
    throw new DbUpdateConcurrencyException("Account was modified by another user");
}
```

**Supported version types:**
- **`int`, `long`** - Auto-incremented (SET version = version + 1)
- **`byte[]`** - Database-managed (SQL Server ROWVERSION/timestamp)
- **Nullable types** - `int?` for distinguishing new vs updated records

### Transactions

```csharp
public class OrderService
{
    private readonly DatabaseContext _context;
    private readonly ITableGateway<Order, int> _orderHelper;

    public async Task ProcessOrderAsync(Order order)
    {
        // Create transaction per operation
        using var tx = _context.BeginTransaction();

        await _orderHelper.CreateAsync(order, tx);
        await UpdateInventoryAsync(order, tx);

        await tx.CommitAsync();
    }
}
```

---
## ⚡ Advanced Features

Features ordered by real-world impact. Skip to what you need.

### 1. Prepared Statement Caching (Automatic)

**Zero-configuration performance boost** - happens automatically on every query:

```csharp
// First execution: Prepared and cached by SQL shape
await helper.CreateAsync(user1);
// SQL: INSERT users (name, email) VALUES (@p0, @p1)
// Shape hash: INSERT_users_2params
// Action: Prepare + execute + cache shape

// Second execution: Cache hit, no re-prepare
await helper.CreateAsync(user2);
// Same shape hash → reuse prepared statement
// Action: Execute only (no prepare overhead)

// Different entity, same shape: Still cached
await helper.CreateAsync(user3);
// Shape unchanged → cache hit

// Check what's happening
Console.WriteLine($"Cached: {context.Metrics.StatementsCached}");
Console.WriteLine($"Evicted: {context.Metrics.StatementsEvicted}");
```

**How it works:**
- Per-connection cache of 32 SQL shapes (LRU eviction)
- Shape = SQL structure + parameter count/types
- Transparent - no code changes needed
- If prepare fails once, disabled for that connection
- Persistent across operations on same physical connection

**When it matters:** High-throughput APIs doing repeated CRUD. Can eliminate 30-50% of database round-trip time.

**When to skip:** Low-traffic applications, ad-hoc queries.

---

### 2. Composite Primary Keys

**Real-world problem:** Legacy databases, natural keys, junction tables.

```csharp
// Multi-column primary key (order + line number)
[Table("order_items")]
public class OrderItem
{
    [PrimaryKey(1)]  // Composite key part 1
    [Column("order_id", DbType.Int32)]
    public int OrderId { get; set; }

    [PrimaryKey(2)]  // Composite key part 2
    [Column("line_number", DbType.Int32)]
    public int LineNumber { get; set; }

    [Id]  // Separate surrogate key (optional)
    [Column("id", DbType.Int64)]
    public long Id { get; set; }

    [Column("product_name", DbType.String)]
    public string ProductName { get; set; }
}

// Retrieve by composite key
var item = await helper.RetrieveOneAsync(new OrderItem { OrderId = 100, LineNumber = 1 });

// Build WHERE clause using composite key
var query = helper.BuildBaseRetrieve("oi");
helper.BuildWhereByPrimaryKey(query, item);
// WHERE oi.order_id = @p0 AND oi.line_number = @p1
```

**When it matters:** Working with existing databases you can't change. Junction tables. Natural keys required by business rules.

**When to skip:** Greenfield projects where you control schema design.

---

### 3. Stored Procedure Output Parameters

**Real-world problem:** Calling stored procedures that return data via OUTPUT parameters or RETURN values.

```csharp
var sc = context.CreateSqlContainer("sp_ProcessOrder");

// Input parameter
sc.AddParameterWithValue("orderId", DbType.Int32, 12345, ParameterDirection.Input);

// Output parameters
var statusParam = sc.AddParameterWithValue("status", DbType.String, null, ParameterDirection.Output);
statusParam.Size = 50;

var totalParam = sc.AddParameterWithValue("total", DbType.Decimal, null, ParameterDirection.Output);

// Return value
var returnParam = sc.CreateDbParameter("returnValue", DbType.Int32, null);
returnParam.Direction = ParameterDirection.ReturnValue;
sc.AddParameter(returnParam);

// Execute procedure
sc.WrapForStoredProc(ExecutionType.Write);
await sc.ExecuteNonQueryAsync(CommandType.StoredProcedure);

// Read outputs
string status = (string)statusParam.Value;
decimal total = (decimal)totalParam.Value;
int result = (int)returnParam.Value;
```

**When it matters:** Enterprise codebases with heavy stored procedure use. Required when business logic lives in database.

**When to skip:** SQL-first development where you write queries directly.

---

### 4. Enum Parsing Fallback Modes

**Real-world problem:** Database has enum values ("Cancelled") that code doesn't know about yet. Deploy crashes.

```csharp
public enum OrderStatus { Pending, Processing, Shipped, Delivered }

[Table("orders")]
public class Order
{
    [Id]
    [Column("id", DbType.Int32)]
    public int Id { get; set; }

    [Column("status", DbType.String)]
    [EnumColumn(typeof(OrderStatus))]
    public OrderStatus? Status { get; set; }
}

// Create helper with enum failure mode
var helper = new TableGateway<Order, int>(context)
{
    EnumParseBehavior = EnumParseFailureMode.SetNullAndLog  // Don't throw on unknown values
};

// Database has "Cancelled" but enum doesn't
// OLD: Would throw InvalidOperationException
// NEW: Sets Status = null and logs warning
var orders = await helper.RetrieveAsync(orderIds);
// Orders with unknown status have Status = null

// Three modes:
// - Throw: Strict mode (default) - fail fast
// - SetNullAndLog: Graceful degradation - null + log warning
// - SetDefaultValue: Use default(TEnum) - e.g., OrderStatus.Pending
```

**When it matters:** Continuous deployment where database changes deploy independently from code. Schema evolution without downtime.

**When to skip:** Coordinated deployments where database and code always match.

---

### 5. Percentile Metrics (P95/P99 Latency)

**Real-world problem:** Need to detect performance degradation before users complain. APM tools cost money.

```csharp
var config = new DatabaseContextConfiguration
{
    ConnectionString = connectionString,
    EnableMetrics = true,
    MetricsOptions = new MetricsOptions
    {
        PercentileWindowSize = 1024,        // Sliding window (power of 2)
        LongLivedConnectionThreshold = 30   // Seconds before flagged
    }
};
var context = new DatabaseContext(config, factory);

// Subscribe to real-time metrics updates
context.MetricsUpdated += (sender, m) =>
{
    // SLA violation detection
    if (m.P95CommandMs > 500)
        _logger.LogWarning("P95 latency {P95}ms exceeds 500ms SLA", m.P95CommandMs);

    if (m.P99CommandMs > 1000)
        _logger.LogError("P99 latency {P99}ms exceeds 1s SLA", m.P99CommandMs);

    // Connection leak detection
    if (m.LongLivedConnections > 5)
        _logger.LogWarning("{Count} connections held > {Threshold}s",
            m.LongLivedConnections, m.LongLivedConnectionThreshold);
};

// Poll metrics anytime
var metrics = context.Metrics;
Console.WriteLine($"P95: {metrics.P95CommandMs}ms");
Console.WriteLine($"P99: {metrics.P99CommandMs}ms");
Console.WriteLine($"Avg: {metrics.AvgCommandMs}ms");
Console.WriteLine($"Failed: {metrics.CommandsFailed}");
Console.WriteLine($"Timed out: {metrics.CommandsTimedOut}");
Console.WriteLine($"Rows read: {metrics.RowsReadTotal}");
```

**23+ tracked metrics:** Connections (current, max, reused, long-lived), commands (total, failed, timed-out, cancelled), latency (avg, P95, P99), rows (read, affected), prepared statements (cached, evicted), transactions (active, max, avg duration).

**When it matters:** Production systems with SLAs. Cost-conscious teams avoiding APM subscription fees.

**When to skip:** Dev/test environments. Non-critical background jobs.

---

### 6. Type Coercion Configuration

**Real-world problem:** PostgreSQL has timestamptz, MySQL doesn't. Oracle handles JSON differently. Need consistent behavior.

```csharp
var options = TypeCoercionOptions.Default with
{
    TimeMappingPolicy = TimeMappingPolicy.ForceUtcDateTime, // Strip offsets
    JsonPassThrough = JsonPassThrough.PreferDocument        // Use JsonDocument
};

var normalized = TypeCoercionHelper.Coerce(
    value: dbValue,
    sourceType: dbValue.GetType(),
    targetType: typeof(DateTimeOffset),
    options: options);
```

**When it matters:** Multi-database support. Migrating between providers.

**When to skip:** Single database type in production.

---

### 7. Mapper Options (Schema Evolution)

**Real-world problem:** Added a column to database. Don't want to update every entity immediately.

```csharp
var options = new MapperOptions(
    Strict: false,                                // Ignore extra columns
    ColumnsOnly: true,                            // Only map [Column] properties
    NamePolicy: name => name.ToLowerInvariant(), // Custom mapping
    EnumMode: EnumParseFailureMode.SetNullAndLog);

// Strict=true: Throws if SELECT returns columns not in entity
// Strict=false: Ignores extra columns (gradual migration)
var users = await DataReaderMapper.LoadAsync<User>(reader, options);
```

**When it matters:** Large codebases with gradual schema evolution. Blue-green deployments.

**When to skip:** Small teams with coordinated schema changes.

---

### 8. Isolation Level Portability

**Real-world problem:** Need consistent transaction semantics but each database has different native isolation names. Want portable code.

```csharp
// Portable isolation profiles
using var tx = context.BeginTransaction(IsolationProfile.SafeNonBlockingReads);

// IsolationProfile maps to provider-native levels per dialect

// Check if degraded
var (nativeLevel, wasDegraded) = context.Dialect.IsolationResolver.ResolveWithDetail(
    IsolationProfile.SafeNonBlockingReads);

if (wasDegraded)
    _logger.LogWarning("Isolation level degraded to {Level}", nativeLevel);

// Available:
// - SafeNonBlockingReads
// - StrictConsistency
// - FastWithRisks
```

**When it matters:** Multi-database support. Testing against different providers.

**When to skip:** Single production database type.

---

### 9. Generated Key Strategies (Automatic)

**How pengdows.crud retrieves auto-generated IDs** - automatic, transparent, database-specific:

```csharp
// You write:
await helper.CreateAsync(user);  // user.Id = 0

// pengdows.crud automatically uses best strategy:

// PostgreSQL 15+: RETURNING clause
// INSERT INTO users (name) VALUES (@p0) RETURNING id

// SQL Server: OUTPUT INSERTED
// INSERT INTO users (name) OUTPUT INSERTED.id VALUES (@p0)

// Oracle: RETURNING INTO
// INSERT INTO users (name) VALUES (@p0) RETURNING id INTO :id

// MySQL: LAST_INSERT_ID()
// INSERT INTO users (name) VALUES (@p0); SELECT LAST_INSERT_ID()

// SQLite: last_insert_rowid()
// INSERT INTO users (name) VALUES (@p0); SELECT last_insert_rowid()

// Firebird: RETURNING
// INSERT INTO users (name) VALUES (@p0) RETURNING id

// Fallback: Natural key lookup
// INSERT INTO users (email, name) VALUES (@p0, @p1)
// SELECT id FROM users WHERE email = @p0

// 6 strategies: Returning, OutputInserted, SessionScopedFunction,
//               PrefetchSequence, CorrelationToken, NaturalKeyLookup
```

**When it matters:** Always - this is how auto-generated IDs work. Completely automatic.

**When to skip:** Never - it's transparent.

---

### 10. SQL Container Cloning

**Real-world problem:** Running the same custom query repeatedly. Want to reuse SQL structure and avoid rebuilding.

```csharp
// Build query template once
var template = helper.BuildBaseRetrieve("u");
template.Query.Append(" WHERE u.status = @p0");
template.AddParameterWithValue("status", DbType.String, "Active");

// ✅ SAFE: Clone within same context, change parameter values
var query1 = template.Clone();  // Reuse SQL, change params
query1.Parameters.First().Value = "Active";
var activeUsers = await helper.LoadListAsync(query1);

var query2 = template.Clone();
query2.Parameters.First().Value = "Inactive";
var inactiveUsers = await helper.LoadListAsync(query2);

// ✅ SAFE: Clone to same-provider contexts (all PostgreSQL tenants)
for (int i = 0; i < 100; i++)
{
    var tenantCtx = registry.GetContext($"pg-tenant-{i}");  // All PostgreSQL
    var query = template.Clone(tenantCtx);
    var users = await helper.LoadListAsync(query);
}

// ❌ UNSAFE: Clone to different provider contexts
var pgContext = registry.GetContext("tenant-a");  // PostgreSQL
var mysqlContext = registry.GetContext("tenant-b");  // MySQL

var pgQuery = pgContext.CreateSqlContainer();
pgQuery.Query.Append("SELECT * FROM users WHERE id = $1");  // PostgreSQL syntax
pgQuery.AddParameterWithValue("id", DbType.Int32, 123);

var clonedQuery = pgQuery.Clone(mysqlContext);  // BROKEN!
// SQL still says "$1" but MySQL needs "?"
// await helper.LoadListAsync(clonedQuery);  // WILL FAIL
```

**Rules:**
- `Clone()` copies SQL string **as-is** - does NOT regenerate for new dialect
- Safe when: Same context, OR new context with identical provider/version
- Unsafe when: Different database provider (PostgreSQL → MySQL) or major version changes
- Benefit: Avoids rebuilding SQL strings - saves StringBuilder allocations

**When it matters:**
- Same custom query across many same-provider tenants (100 PostgreSQL tenants)
- Reusing query templates within same context with different parameters
- High-throughput scenarios where SQL string building overhead matters

**When to skip:**
- Multi-tenancy with mixed database types (can't safely clone across providers)
- Simple queries using built-in methods like `RetrieveAsync()` (already optimized)

**Honest assessment:** Useful optimization for same-provider scenarios. Not safe across different database types - cloning doesn't regenerate SQL for new dialects.

---

### 11. Custom Type Converters (Extensibility)

**Real-world problem:** Using a database type pengdows.crud doesn't natively support.

```csharp
// Register custom converter for PostgreSQL point type
pengdows.crud.types.AdvancedTypeRegistry.Shared.RegisterConverter<Point>(
    from: (dbValue) => {
        if (dbValue is string str)
        {
            var parts = str.Trim('(', ')').Split(',');
            return new Point(double.Parse(parts[0]), double.Parse(parts[1]));
        }
        return default;
    },
    to: (point) => $"({point.X},{point.Y})"
);

// Use in entity
[Table("locations")]
public class Location
{
    [Id]
    [Column("id", DbType.Int32)]
    public int Id { get; set; }

    [Column("coordinates", DbType.Object)]
    public Point Coordinates { get; set; }
}

// Automatic bi-directional conversion
var location = new Location { Coordinates = new Point(40.7128, -74.0060) };
await helper.CreateAsync(location);  // Point → "(40.7128,-74.0060)"

var retrieved = await helper.RetrieveOneAsync(location.Id);
// "(40.7128,-74.0060)" → Point instance
```

**When it matters:** Custom database types not in the 14 built-in converters.

**When to skip:** Standard types already supported.

---

### 12. Connection Local State (Debugging)

**What it is:** Per-physical-connection state tracking for prepare behavior.

**Important:** Direct connection access is internal-only. Connection-local state is not part of the public API.

**Use instead:** `DatabaseContext.Metrics` and `MetricsUpdated` for diagnostics, or add instrumentation inside the
connection strategies when debugging the library itself.

---

### 13. Dialect Capability Detection (Adaptive SQL)

**Real-world problem:** Want to use MERGE if available, fall back to INSERT/UPDATE if not.

```csharp
var dialect = context.Dialect;

// Feature detection for conditional logic
if (dialect.SupportsMerge)
{
    // SQL Server, Oracle, Firebird, PostgreSQL 15+
    await helper.UpsertAsync(entity);  // Uses MERGE
}
else if (dialect.SupportsInsertOnConflict)
{
    // PostgreSQL <15, SQLite 3.24+
    await helper.UpsertAsync(entity);  // Uses ON CONFLICT
}

// Check other capabilities
if (dialect.SupportsSetValuedParameters)
{
    // PostgreSQL: ANY($1::int[]) - 1 param instead of 1000
    var orders = await helper.RetrieveAsync(thousandIds);
}

if (dialect.SupportsJsonTypes)
{
    // Use native JSON/JSONB column types and operators
}

// 40+ flags: SupportsMerge, SupportsInsertOnConflict, SupportsSetValuedParameters,
// SupportsJsonTypes, SupportsTemporalData, SupportsCTE, SupportsWindowFunctions,
// SupportsFullOuterJoin, MaxSupportedStandard (SQL:1992/1999/2003/2011/2016)

// Compatibility assessment
if (!dialect.CanUseModernFeatures)
    _logger.LogWarning("Database version lacks modern features");

var warning = dialect.GetCompatibilityWarning();
if (warning != null)
    _logger.LogWarning(warning);
```

**When it matters:** Writing database-agnostic code that adapts to capabilities. Supporting older database versions.

**When to skip:** Single database type and version in production.

---

## 📦 Dependency Injection & Lifetime

### ⚠️ CRITICAL: DatabaseContext is NOT like EF's DbContext

**`DatabaseContext` is a singleton execution coordinator**, NOT a scoped per-request context.

| Aspect | EF Core DbContext | pengdows.crud DatabaseContext |
|--------|-------------------|-------------------------------|
| **Lifetime** | Scoped (per request) | **Singleton (per connection string)** |
| **Thread Safety** | NOT thread-safe | **Thread-safe (concurrent callers supported)** |
| **Change Tracking** | Yes | **No** |
| **State Management** | Tracks entities | **Stateless** |
| **Connections** | One per context | **Strategy-based (Standard/KeepAlive/SingleWriter/SingleConnection)** |

### Correct Registration

```csharp
// ✅ CORRECT: Singleton per connection string
services.AddSingleton<DatabaseContext>(sp =>
    new DatabaseContext(connectionString, NpgsqlFactory.Instance));

// Multi-tenant: one singleton per tenant
services.AddKeyedSingleton<DatabaseContext>("tenant1", sp =>
    new DatabaseContext(tenant1ConnectionString, SqliteFactory.Instance));

// ❌ WRONG: Do NOT use scoped lifetime
services.AddScoped<DatabaseContext>(sp => ...);  // Breaks persistent connection modes!
```

**Why singleton?**

- **Required for persistent modes** (SingleWriter, SingleConnection): These maintain pinned connections. Multiple contexts = multiple connections = errors.
- **Works with Standard mode**: Provider manages pooling. Singleton avoids repeated initialization.
- **Thread-safe**: Concurrent operations serialize at connection lock, not context lock.

### TransactionContext

**NOT registered in DI.** Create via `context.BeginTransaction()`:

```csharp
public class OrderService
{
    private readonly DatabaseContext _context;  // Injected singleton

    public async Task ProcessOrderAsync(Order order)
    {
        // Create transaction per operation
        using var tx = _context.BeginTransaction();

        await _orderHelper.CreateAsync(order, tx);

        tx.Commit();  // Dispose releases connection lock
    }
}
```

**Key points:**
- Created per operation, not per request
- Holds connection lock for its lifetime
- Dispose promptly (use `using` or `await using`)
- Do NOT create long-lived transactions

---

## 🧪 Testing

### Unit Tests (No Database Required)

```csharp
using pengdows.crud.fakeDb;

// fakeDb is a COMPLETE ADO.NET provider implementation
var factory = new FakeDbFactory(SupportedDatabase.PostgreSql);
var context = new DatabaseContext("Data Source=test;EmulatedProduct=PostgreSql", factory);
var helper = new TableGateway<Order, int>(context);

// Test SQL generation
var container = helper.BuildCreate(order);
Assert.Contains("INSERT INTO orders", container.Query.ToString());
Assert.Equal(3, container.Parameters.Count);

// Test error handling
var connection = (FakeDbConnection)factory.CreateConnection();
connection.SetFailOnOpen();
await Assert.ThrowsAsync<InvalidOperationException>(() =>
    helper.CreateAsync(order, context));

// Test result mapping
connection.EnqueueScalarResult(42);
var result = await container.ExecuteScalarRequiredAsync<int>();
Assert.Equal(42, result);
```

Test totals change continuously. Get the current unit-test count dynamically:

```bash
dotnet test pengdows.crud.Tests/pengdows.crud.Tests.csproj -c Release --list-tests | rg -c "^[[:space:]]*pengdows\\.crud\\.Tests\\."
```

### Integration Tests (Real Databases)

```csharp
using Testcontainers.PostgreSql;

// Spin up real PostgreSQL in Docker
var postgres = new PostgreSqlBuilder()
    .WithImage("postgres:15-alpine")
    .Build();
await postgres.StartAsync();

var ctx = new DatabaseContext(postgres.GetConnectionString(),
    NpgsqlDataSource.Create(postgres.GetConnectionString()));

// Test against REAL PostgreSQL
var helper = new TableGateway<Order, int>(ctx);
await helper.CreateAsync(order, ctx);

var retrieved = await helper.RetrieveOneAsync(order.Id, ctx);
Assert.Equal(order.Total, retrieved.Total);
```

---

## 🧪 Running Tests Inside Docker

Execute the full test suite inside a disposable Docker container:

```bash
./tools/run-tests-in-container.sh
```

Override image or pass custom arguments:

```bash
# Use different SDK build
./tools/run-tests-in-container.sh --image mcr.microsoft.com/dotnet/sdk:8.0.201

# Run specific project
./tools/run-tests-in-container.sh -- dotnet test pengdows.crud.Tests/pengdows.crud.Tests.csproj
```

> Docker must be installed and running. Container is ephemeral—every invocation starts fresh.

---

## 🎯 When to Choose pengdows.crud

### ✅ Choose pengdows.crud if you need:

- **Multi-tenant SaaS** where tenants can use different database types
- **Advanced PostgreSQL types** (inet, cidr, intervals, ranges, hstore)
- **Geospatial data** across multiple database platforms
- **Memory-efficient exports** of millions of rows (ETL, reporting)
- **Production observability** with P95/P99 metrics and connection tracking
- **Superior testability** (fast unit tests + real integration tests)
- **SQL control** without manual connection/transaction management
- **Database-specific optimizations** (MERGE, ON CONFLICT, set-valued params)
- **Portable isolation levels** across different databases
- **Audit tracking** (CreatedBy/On, LastUpdatedBy/On) out of the box

### ⚠️ Choose EF Core instead if you:

- Prefer LINQ over SQL
- Need code-first migrations
- Want change tracking and lazy loading
- Have a team unfamiliar with SQL

### ⚠️ Choose Dapper instead if you:

- Need absolute minimum overhead
- Don't need any advanced features (multi-tenancy, metrics, streaming, etc.)
- Willing to manually manage connections, transactions, and error handling
- Have very simple CRUD requirements

---

## 📊 Performance

- **Reader mapping**: 5.7x faster than pure reflection (compiled property setters)
- **Set-valued parameters**: 1 parameter instead of 1000 on PostgreSQL/SQL Server
- **Prepared statement caching**: Eliminates repeated SQL parsing
- **Zero allocation** in hot paths via `StringBuilderLite` and BoundedCache
- **Streaming**: Process millions of rows with constant memory usage

Benchmark suite included: `benchmarks/CrudBenchmarks/`

---

## 🤝 Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

**TDD is mandatory in version 1.1**: Write tests first, then implementation.

**Minimum test coverage**: 83% (CI enforced), target 90%.

---

## 📄 License

MIT License - see [LICENSE](LICENSE) for details.

---

## 🙏 Acknowledgments

Built for developers who:
- Write SQL and understand ACID
- Don't want ORMs rewriting queries
- Need multi-database support with vendor-specific optimizations
- Value testability and observability
- Build multi-tenant SaaS applications

If you want **SQL control** with **safety**, **structure**, and **production observability**, pengdows.crud is for you.
