# Primary Keys vs Surrogate Keys: The Performance Impact

## The Core Problem

Most developers make this expensive mistake:

```sql
CREATE TABLE users (
    id INT IDENTITY(1,1) PRIMARY KEY,  -- Conflates system ID with business identity
    username VARCHAR(50) UNIQUE,        -- The actual business identifier
    email VARCHAR(255) UNIQUE
);
```

**Problem:** You're treating a system convenience (auto-increment ID) as business identity, paying for it in reads, writes, and index performance.

## The Three Concepts You Must Separate

| Concept | Purpose | Examples | pengdows.crud Attribute |
|---------|---------|----------|------------------------|
| **Primary Key (PK)** | Business identity - why the row exists | username, email, (order_id, line_item) | `[PrimaryKey]` |
| **Surrogate Key** | Stable system ID for FKs and references | INT, UUIDv7, NEWSEQUENTIALID() | `[Id]` |
| **Clustered Index** | Physical storage order (engine-dependent) | Time ranges, frequently-queried columns | Engine-specific |

## Candidate Keys

Any combination of columns that uniquely identify a record based on real-world meaning. These are your legitimate business identifiers.

**Examples:**
- `username` (unique login identifier)
- `email` (unique contact method)
- `sku` (unique product identifier)
- `(customer_id, order_date, line_number)` (composite business key)

## Primary Keys: Business Identity

A primary key is the candidate key you choose to represent the record's core identity. This should reflect the "reason for the row's existence."

**Requirements:**
- Must be NOT NULL
- Must be unique
- Should represent a real-world concept
- Drives your main query patterns

In pengdows.crud: Marked with `[PrimaryKey]` attribute. May span multiple columns. Used by `RetrieveOneAsync(entity)`.

```csharp
public class User
{
    [Id] public Guid Id { get; set; }              // Surrogate key for FKs
    [PrimaryKey] public string Username { get; set; } // Business identity
    [Column("email")] public string Email { get; set; }
}
```

## Surrogate Keys (Pseudo Keys): System Identity

A surrogate key is a synthetic identifier that exists purely for system purposes — not business meaning.

**Characteristics:**
- Usually auto-generated (IDENTITY, GUID, UUID)
- Stable over time (safe for foreign key references)
- No business significance
- Single column for simplicity

**In pengdows.crud:**
- Marked with `[Id]` attribute
- Used by `TableGateway<T, TID>` for default CRUD operations
- Only one `[Id]` property allowed per class

## Database Engine Differences

| Database | Clustering Behavior | Key Points |
|----------|---------------------|------------|
| SQL Server | PK is clustered by default | Use `PRIMARY KEY NONCLUSTERED` when business key isn't optimal for clustering |
| MySQL/InnoDB | PK is always clustered | Your PK choice = your clustering choice. Secondary indexes store full PK. |
| PostgreSQL | Heap tables, no maintained clustering | PK is just a unique B-tree. Use BRIN for time ranges. |
| SQLite | INTEGER PRIMARY KEY = rowid | Non-integer PKs create separate unique indexes |

## Performance Anti-Patterns

### Random UUID as Clustered Index

```sql
-- SQL Server: causes page splits, fragmentation, write amplification
CREATE TABLE users (id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID());

-- MySQL/InnoDB: random clustering + large secondary indexes
CREATE TABLE users (id CHAR(36) PRIMARY KEY DEFAULT (UUID()));
```

**Why it's slow:** Random inserts into clustered structure cause page splits, fragmentation, and poor cache locality.

### Better Approach: Separate Concerns

**SQL Server:**

```sql
CREATE TABLE dbo.Users(
    Id UNIQUEIDENTIFIER DEFAULT NEWSEQUENTIALID(),  -- Time-ordered surrogate
    Username NVARCHAR(50) NOT NULL,                  -- Business identity
    CreatedUtc DATETIME2 DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_Users PRIMARY KEY NONCLUSTERED (Username),  -- Business key as PK
    CONSTRAINT UQ_Users_Id UNIQUE (Id)                        -- Surrogate for FKs
);
CREATE CLUSTERED INDEX CX_Users_CreatedUtc ON dbo.Users(CreatedUtc);  -- Cluster on query pattern
```

**MySQL/InnoDB:**

```sql
CREATE TABLE users (
    id BINARY(16) NOT NULL,              -- UUIDv7 in binary
    username VARCHAR(50) NOT NULL,
    created_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (created_utc, id),       -- Time + tiebreaker clustering
    UNIQUE KEY uq_users_id (id),         -- Stable FK target
    UNIQUE KEY uq_users_username (username)  -- Business lookup
);
```

## Best Practices

### The Golden Rules

1. **Separate identity from clustering** - they serve different purposes
2. **Use business keys as Primary Keys** - model reality, not convenience
3. **Use surrogate keys for FKs** - stable references across systems
4. **Choose clustering per engine** - align with your dominant query patterns
5. **Store UUIDs efficiently** - BINARY(16), not CHAR(36)
6. **Avoid random clustering** - use time-ordered IDs (UUIDv7, NEWSEQUENTIALID())

### Query Performance Impact

**Before (anti-pattern):**

```sql
-- Requires extra JOIN step through surrogate key
SELECT o.* FROM orders o
JOIN users u ON u.id = o.user_id  -- Indirect lookup
WHERE u.username = 'john_doe'
AND o.order_date >= '2024-01-01';
```

**After (optimized):**

```sql
-- Direct business key lookup, aligned clustering
SELECT o.* FROM orders o
JOIN users u ON u.username = o.username  -- Direct business key join
WHERE u.username = 'john_doe'            -- Index-aligned lookup
AND o.order_date >= '2024-01-01';        -- Clustered time scan
```

**Result:** Fewer page reads, better cache locality, lower p95/p99 latency.

## UUID Storage Optimization

**Wrong Way (36 bytes):**

```sql
id CHAR(36)  -- '550e8400-e29b-41d4-a716-446655440000'
```

**Right Way (16 bytes):**

```sql
id BINARY(16)        -- MySQL
id uuid              -- PostgreSQL
id UNIQUEIDENTIFIER  -- SQL Server
```

Use **UUIDv7** for time-locality: Timestamp prefix ensures better clustering than random UUIDv4.

## pengdows.crud's Built-in UUIDv7 Generator

pengdows.crud includes `Uuid7Optimized` - a high-performance, RFC 9562-compliant UUIDv7 generator:

```csharp
using pengdows.crud;

// Generate time-ordered UUIDs for optimal clustering
var id = Uuid7Optimized.NewUuid7();

// High-performance no-wait version (returns false if counter exhausted)
if (Uuid7Optimized.TryNewUuid7(out var uuid))
{
    // Use the UUID
}

// Direct byte generation (avoids Guid allocation)
Span<byte> buffer = stackalloc byte[16];
Uuid7Optimized.NewUuid7Bytes(buffer);
```

**Features:**
- Thread-safe with per-thread counters (no lock contention)
- Monotonic ordering within process scope (up to 4,096 IDs/ms per thread)
- Clock drift handling with bounded backward time tolerance
- High throughput with buffered randomness and optimized allocations
- RFC 9562 compliant with proper version and variant bits

**Framework Compatibility:**
- **.NET 8:** Use `pengdows.crud.Uuid7Optimized` (included)
- **.NET 9+:** Choose between `pengdows.crud.Uuid7Optimized` or native `Guid.CreateVersion7()`
- **Performance:** pengdows.crud's implementation is optimized for high-throughput scenarios

## pengdows.crud Implementation

pengdows.crud enforces this philosophy through its attribute system:

- `[Id]` marks a unique surrogate key column — single column only
- `[PrimaryKey]` may be used on one or more fields for business identity
- Tables must have a unique single-column `[Id]` to use full TableGateway automation
- `RetrieveOneAsync(entity)` targets `[PrimaryKey]` columns, not `[Id]`

### Flexible CRUD Operations

```csharp
// By business primary key - optimal for most queries
var user1 = await gateway.RetrieveOneAsync(new User { Username = "john_doe" });

// By surrogate ID - useful for FK relationships
var user2 = await gateway.RetrieveOneAsync(userId);

// Upsert by business key - handles conflicts naturally
await gateway.UpsertAsync(new User {
    Username = "john_doe",
    Email = "john@example.com"
});
```

### Benefits of This Approach

- **Performance:** Query patterns align with business logic and indexes
- **Flexibility:** Support both business key and surrogate key operations
- **Consistency:** Stable foreign key references with surrogate IDs
- **Maintainability:** Schema reflects real-world concepts, not ORM limitations
- **Deterministic IDs:** Use hash-derived UUIDs for predictable references

## When to Break the Rules

- **Unstable business keys** (social handles that can change) → Use surrogate PK + UNIQUE business constraint
- **Logs, queues, time-series** → Monotonic clustering (IDENTITY, UUIDv7) more important than business identity
- **ORM constraints** → Keep surrogate PK for framework compatibility, enforce business uniqueness separately
- **Distributed systems** → Global uniqueness coordination challenges

## Troubleshooting Common Issues

| Problem | Cause | Solution |
|---------|-------|----------|
| Random Insert Performance Degradation | UUIDv4 or NEWID() in clustered structure | Use UUIDv7, NEWSEQUENTIALID(), or cluster on time column |
| Large Secondary Index Sizes (MySQL) | Composite or large PK stored in every secondary index | Use shorter surrogate key or optimize PK composition |
| Business Key Changes Breaking FKs | Using business key for foreign key references | Use stable surrogate key for FKs, business key for lookups |
| Poor Time-Range Query Performance | Clustering doesn't align with temporal queries | Include time dimension in clustering key |
