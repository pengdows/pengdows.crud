# Runtime Capability Discovery

`pengdows.crud` normalizes what it knows about the connected database into two objects reachable
from any `IDatabaseContext`, so application code (and higher-level Pengdows libraries) can branch
on **what the database actually supports** instead of hard-coding `if (database == SupportedDatabase.X)`
checks. This doc explains how to read that surface correctly — most importantly, that `ISqlDialect`
mixes two different audiences in one interface, and only one of them is meant for you to call.

## Getting capability information

```csharp
IDatabaseContext context = ...;

ISqlDialect dialect = context.Dialect;                 // the primary capability/behavior surface
IDataSourceInformation info = context.DataSourceInfo;   // a narrower metadata-only subset
```

Both are populated after the context detects the connected product — by the time you have a
constructed `IDatabaseContext`, they're ready to read. `context.Dialect.DatabaseType` gives you the
detected `SupportedDatabase` enum value if you ever need it, but the entire point of this surface is
that you usually shouldn't need to switch on it.

`IDataSourceInformation` (`pengdows.crud.abstractions/IDataSourceInformation.cs`) is a smaller,
metadata-flavored subset: detected product name/version, parsed `Version`, quoting characters,
parameter marker pattern/regex/max length, named/repeated-parameter support, parameter/output
limits, default-prepare recommendation, procedure wrapping style, a handful of DDL-capability
flags (`SupportsDropTableIfExists`, `SupportsTruncateTable`, `SupportsMerge`,
`SupportsInsertOnConflict`, `SupportsOnDuplicateKey`), `StandardCompliance`, fallback-dialect
status, and `GetCompatibilityWarning()`. Everything on it also appears on `ISqlDialect`, which is
the larger and more current surface — reach for `context.Dialect` first; `DataSourceInfo` exists
for callers that only need this narrower metadata view.

## `ISqlDialect` mixes two audiences — know which one you're calling

`ISqlDialect` (`pengdows.crud.abstractions/dialects/ISqlDialect.cs`, ~940 lines) is CLAUDE.md's
documented "official surface area" — but reading through it, its members split into two genuinely
different jobs:

1. **Consumer-facing capability flags and metadata** — booleans, enums, and simple accessors that
   describe what the database *can do*. Safe and intended for application code (and libraries like
   `pengdows.hangfire`) to branch on. This is what the rest of this doc catalogs.
2. **Internal SQL-generation machinery** — methods `TableGateway`/`PrimaryKeyTableGateway` call to
   actually build dialect-correct SQL. These are technically reachable through the public interface
   (`context.Dialect.BuildBatchInsertSql(...)`, `context.Dialect.GetSavepointSql("x")`, etc.), but
   they are implementation plumbing, not a stable application API — their exact SQL shape, parameter
   ordering, and calling contract are free to change alongside `TableGateway`'s own internals, and
   several take positional arguments (wrapped names, value-lookup delegates, pre-computed column
   lists) that only make sense in the context of the exact call sequence the gateway classes use.
   Calling them directly from application code is not a supported pattern. This group includes:
   `BuildBatchInsertSql` (both overloads), `BuildBatchUpdateSql`, `GetSavepointSql`,
   `GetRollbackToSavepointSql`, `WrapObjectName`/`WrapSimpleName`/`ReplaceNeutralTokens`,
   `MakeParameterName` (both overloads), `UpsertIncomingColumn`/`UpsertIncomingAlias`/
   `RenderMergeOnClause`, `CreateDbParameter<T>` (both overloads), `GetVersionQuery`,
   `GetSequenceNextValQuery`, `GetConnectionSessionSettings`, `GenerateParameterName`/
   `GenerateRandomName`, `GetLastInsertedIdQuery`, `RenderInsertReturningClause`/
   `RenderInsertReturningPrefix`, `GetCompoundInsertIdSuffix`, `GetLastInsertedIdFromCommand`,
   `GetCorrelationTokenLookupQuery`/`GetNaturalKeyLookupQuery`, `PrepareParameterValue`, and
   `AppendPaging`.

This split is a real architectural tension in the current interface (confirmed by direct
inspection, tracked in `docs/planning/future-work.md`'s DOC-032 entry) — not a documentation
oversight you're missing. Until/unless it's split into two interfaces, the practical rule is: if
a member is a plain `bool`/`enum`/`int`/`string` describing a capability or limit, it's yours to
read; if it's a method that *builds or renders SQL/parameters*, treat it as gateway-internal and
don't call it directly.

## Consumer-facing capability flags, by category

This groups the actual capability surface. For per-database values, see
`docs/supported-databases.md`'s matrix; for *why* ~20 of these derive from one detected value
instead of being independently implemented per dialect, see `docs/architecture.md`'s "Capability
Flags Derive From One Enum" section — get `MaxSupportedStandard`/`ProductInfo.StandardCompliance`
right and most `Supports*` flags below follow automatically, with explicit per-dialect overrides
only where a real engine's behavior diverges from its claimed standard-year compliance.

**Identity & versioning:** `DatabaseType`, `ProductInfo` (`IDatabaseProductInfo`), `IsInitialized`,
`MaxSupportedStandard` / `StandardCompliance`, `IsFallbackDialect`, `GetCompatibilityWarning()`,
`CanUseModernFeatures`, `HasBasicCompatibility`.

**Parameters & identifiers:** `ParameterMarker`, `SupportsNamedParameters`,
`SupportsRepeatedNamedParameters`, `SupportsSetValuedParameters`, `MaxParameterLimit`,
`ParameterNameMaxLength`, `QuotePrefix`/`QuoteSuffix`/`CompositeIdentifierSeparator`,
`PrepareStatements` / `IsPrepareExhausted`.

**SQL language features:** `SupportsJoins`, `SupportsOuterJoins`, `SupportsSubqueries`,
`SupportsUnion`, `SupportsWindowFunctions`, `SupportsEnhancedWindowFunctions`,
`SupportsCommonTableExpressions`, `SupportsInsteadOfTriggers`, `SupportsNamespaces`,
`SupportsRowPatternMatching`, `SupportsPropertyGraphQueries`.

**Types:** `SupportsUserDefinedTypes`, `SupportsArrayTypes`, `SupportsMultidimensionalArrays`,
`SupportsXmlTypes`, `SupportsJsonTypes`, `SupportsSqlJsonConstructors`, `SupportsJsonTable`,
`SupportsTemporalData`, `SupportsRegularExpressions`, `BooleanDbType` (the `DbType` to bind a bool
as — most databases `DbType.Boolean`, SQLite `DbType.Int32`).

**Constraints:** `EnforcesConstraints`, `EnforcesForeignKeyConstraints`,
`SupportsUniqueConstraints`, `SupportsCheckConstraints`, `SupportsDropTableIfExists`,
`SupportsTruncateTable`.

**Upsert/merge — read the caveat below before branching on these:** `SupportsMerge`,
`EmitsAnsiMergeSyntax`, `RequiresMergeStatementTerminator`, `MergeUpdateRequiresTargetAlias`,
`SupportsInsertOnConflict`, `SupportsOnConflictWhere`, `SupportsOnDuplicateKey`,
`SupportsOverridingSystemValue`, `SupportsPureKeyUpsert`.

**Generated keys:** `SupportsInsertReturning`, `InsertReturningClauseBeforeValues`,
`RequiresOutputParameterForReturning`, `WrapsInsertStatementForReturning`,
`GetGeneratedKeyPlan()` (returns a `GeneratedKeyPlan` enum value — see `docs/generated-keys.md`),
`HasSessionScopedLastIdFunction()`.

**Transactions & isolation:** `SupportsSavepoints`, `SupportsReadOnlyTransactions`,
`ReadOnlyConnectionsCanBlockConcurrentWriters`, `RequiresSerializedConnectionOpen`,
`RejectsExplicitIsolationLevelOnBeginTransaction`, `ReadCommittedCompatibleIsolationLevel`.

**Stored procedures:** `ProcWrappingStyle` (see `docs/planning/future-work.md`'s DOC-026 for the
consumer workflow this drives), `RequiresStoredProcParameterNameMatch`, `MaxOutputParameters`.

**Batching & pagination:** `SupportsBatchInsert`, `MaxRowsPerBatch`, `SupportsBatchUpdate`,
`SupportsOffsetFetch`, `SupportsLimitOffset`.

**Pooling:** `SupportsExternalPooling`, `PoolingSettingName`, `MinPoolSizeSettingName`,
`MaxPoolSizeSettingName` — see `docs/connection/connection-pooling.md` for how these feed pool
configuration.

**Exception analysis:** `ClassifyException(Exception)` and `AnalyzeException(Exception)` are
consumer-facing control-flow APIs in their own right (provider-neutral error category, constraint
kind, transient/retryable signals) — see `docs/planning/future-work.md`'s DOC-023 for the dedicated
write-up; `IsUniqueViolation`/`IsForeignKeyViolation`/`IsNotNullViolation`/`IsCheckConstraintViolation`
are the lower-level building blocks `AnalyzeException` composes from and are also safe to call
directly if you only need one specific check.

## Branch on capability, not database name — a real example

Checking a single `Supports*` flag and assuming it's mutually exclusive with every other upsert
strategy is a real bug this codebase has already hit. PostgreSQL 15+ has **both**
`SupportsMerge = true` (its single-row upsert path can use real `MERGE`) **and**
`SupportsInsertOnConflict = true` (batch upsert always uses `ON CONFLICT`, since there is no
batch-`MERGE` implementation for any dialect) **at the same time**. Code that checked only
`SupportsMerge` to decide which upsert-fragment shape to build leaked a `MERGE`-only SQL fragment
(referencing `s.column`, `MERGE`'s own `USING (...) AS s` alias) into batch `ON CONFLICT` SQL,
which has no such alias — a real, shipped bug, fixed by building both fragments independently
rather than treating the flags as an either/or choice (see `TableGateway.Sql.cs`'s
`upsertUpdateFragment`/`upsertUpdateFragmentOnConflict` pre-build and its inline comment for the
full explanation). The lesson generalizes: check the specific flags relevant to the *specific
operation* you're generating, not a single flag as a stand-in for "which dialect family is this."

```csharp
// Wrong: assumes SupportsMerge and SupportsInsertOnConflict are mutually exclusive.
var sql = dialect.SupportsMerge ? BuildMergeUpsert() : BuildOnConflictUpsert();

// Right: each operation checks the flag it actually depends on.
var singleRowSql = dialect.SupportsMerge ? BuildMergeUpsert() : BuildOnConflictOrOnDuplicateUpsert();
var batchSql = dialect.SupportsInsertOnConflict || dialect.SupportsOnDuplicateKey
    ? BuildBatchOnConflictUpsert()
    : BuildPerEntityUpsertLoop(); // e.g. SQL Server/Oracle/Firebird — see docs/batch-operations.md
```

## Stability contract

`ISqlDialect` and `IDataSourceInformation` are part of `pengdows.crud.abstractions`, the package
whose public surface `tools/interface-api-check` baselines and CI enforces — see CLAUDE.md's API
Visibility Principles, which names `ISqlDialect` directly as "official surface area." There is no
separate, more granular stability promise published beyond that: no documented distinction between
"these flags are guaranteed forever" and "these might be renamed." In practice:

- **Additive changes are low-risk.** Several members are C# default-interface-method properties
  (`IsPrepareExhausted`, `EmitsAnsiMergeSyntax`, `RequiresMergeStatementTerminator`,
  `ReadOnlyConnectionsCanBlockConcurrentWriters`, `RequiresSerializedConnectionOpen`,
  `RejectsExplicitIsolationLevelOnBeginTransaction`, `SupportsOnConflictWhere`,
  `SupportsPureKeyUpsert`, `RequiresOutputParameterForReturning`,
  `WrapsInsertStatementForReturning`, `RenderInsertReturningPrefix`,
  `GetLastInsertedIdFromCommand`, `ClassifyException`, `AnalyzeException`, `WrapSimpleName`,
  `ReplaceNeutralTokens`, `RenderMergeOnClause`) specifically so a new capability flag can be added
  to the interface without breaking existing dialect implementations that don't override it.
- **Renaming or removing an existing flag, or narrowing its semantics, is a breaking change** —
  `interface-api-check`'s baseline will catch a removed/renamed member; a semantic narrowing
  (e.g. a flag that used to mean X now means a stricter subset of X) would not be caught
  mechanically and would need to be called out in release notes.
- Treat these flags the way you'd treat any other public interface member from this project: safe
  to build long-lived branching logic on, subject to the same deprecation care as any other
  `pengdows.crud.abstractions` member — not a special, more-volatile category.
