# RepoDb Comparison Coverage Ledger

This ledger records the test coverage status for the actionable provider findings in the RepoDb comparison. It distinguishes existing evidence from focused regression coverage still required, so feature work does not lose the original provider context.

## Scope

This is a coverage ledger, not a claim that every listed issue is a current defect. A focused reproduction is required before changing production behavior. Items that have no corresponding generated or execution path are deferred until that feature exists.

## Existing evidence

| Provider | Area | Current evidence | Gap remaining |
|---|---|---|---|
| Db2 | MERGE/upsert and type binding | Unit SQL-shape coverage; live generic upsert, `FINAL TABLE` identity retrieval, unique-violation, savepoint, session cleanup, GUID binding, and UTC `DateTimeOffset`-to-`TIMESTAMP` round trips | Reproduce `SQL0418N` with the exact untyped MERGE parameter shapes. |
| PostgreSQL | Identity insert | Generated-identity insert/returning coverage | Explicit-ID upsert against `GENERATED ALWAYS AS IDENTITY`. |
| SQL Server | Identity return | `OUTPUT INSERTED` identity coverage | Enabled-trigger execution path. |
| SQL Server | Paging | OFFSET/FETCH SQL generation and live paging | Missing-`ORDER BY` fail-fast behavior. |
| Oracle | MERGE/upsert | Live generic upsert passes in the provider testbed | Focused terminator assertion/reproduction. Existing live coverage means a semicolon issue must be reproduced before it is treated as a defect. |
| MariaDB | Upsert/paging | Generic upsert and paging coverage | `uint`/`ulong` identity-return conversion. |
| SQLite | Mapping and paging | Decimal, GUID, DateTime, paging, and value-dependent decimal reader-type coverage | Provider disposal timing. |

## Required regression coverage

1. **Db2 — untyped MERGE parameters:** unit SQL rendering plus live prepare/execute coverage for values that historically cause `SQL0418N`.
2. **PostgreSQL — explicit identity upsert:** live test for a caller-supplied identity value on a `GENERATED ALWAYS AS IDENTITY` table.
3. **SQL Server — triggers and OUTPUT:** live insert-with-returning test on a table with an enabled insert trigger.
4. **SQL Server — paging order:** unit and integration coverage for paging without `ORDER BY`, with the intended fail-fast contract defined by the test.
5. **Oracle — MERGE terminator:** unit assertion of generated MERGE text and live execution against ODP.NET.
6. **MariaDB — unsigned identities:** live `uint` and `ulong` identity round trips.
7. **SQLite — disposal timing:** provider-specific command/reader lifetime test, requiring a System.Data.SQLite harness in addition to Microsoft.Data.Sqlite.

## Deferred until a corresponding feature exists

| Area | Reason to defer | Required test when implemented |
|---|---|---|
| Db2 `TRUNCATE TABLE ... IMMEDIATE` | pengdows.crud does not generate a first-class truncate operation | Db2 integration test that verifies generated SQL includes `IMMEDIATE`. |
| Db2 MERGE generated-key return and autocommit | Db2 MERGE does not currently return a generated key | Integration test proving identity retrieval remains correct after statement autocommit. |
| SQLite truncate emulation / VACUUM in transactions | pengdows.crud does not generate SQLite truncate emulation | Transactional and non-transactional integration tests for the chosen API contract. |

## Testbed capability skips

The Db2 testbed currently reports two capability skips. These are not xUnit skipped tests:

- Stored procedures are not modeled as supported for Db2.
- No Db2-specific invalid-isolation profile is currently asserted.

The invalid-isolation case is a test coverage gap. The stored-procedure entry is a declared capability boundary and should only become a passing check when support is intentionally added.
