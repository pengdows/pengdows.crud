# Unit Test Coverage Report

This document captures the most recent coverage data generated for the `pengdows.crud` solution. The measurements come from `coverage.json`, which was produced by DotCover `2025.2.0.1`.

## How to run the unit tests with coverage

From the repository root, execute the following command:

```bash
dotnet test pengdows.crud.Tests/pengdows.crud.Tests.csproj \
  -c Release \
  --settings coverage.runsettings \
  --results-directory TestResults
```

The runsettings file excludes the test projects and helpers from the coverage calculation so the reported metrics focus on the production assemblies.

## Coverage summary

Overall coverage: **86%** (2467 / 2858 statements).

| Namespace | Covered Statements | Total Statements | Coverage |
| --- | --- | --- | --- |
| attributes | 30 | 35 | 86% |
| configuration | 88 | 95 | 93% |
| connection | 59 | 65 | 91% |
| dialects | 557 | 718 | 78% |
| exceptions | 15 | 15 | 100% |
| infrastructure | 27 | 27 | 100% |
| isolation | 36 | 37 | 97% |
| tenant | 53 | 56 | 95% |
| threading | 33 | 33 | 100% |
| wrappers | 128 | 142 | 90% |

The root `pengdows.crud` namespace also exposes several direct types. Their individual coverage is outlined below:

| Type | Covered Statements | Total Statements | Coverage |
| --- | --- | --- | --- |
| AuditValueResolver | 0 | 0 | 0% |
| AuditValues | 6 | 6 | 100% |
| ColumnInfo | 45 | 46 | 98% |
| DataReaderMapper | 32 | 32 | 100% |
| DataSourceInformation | 59 | 59 | 100% |
| DatabaseContext | 140 | 153 | 92% |
| DecimalHelpers | 9 | 9 | 100% |
| EntityHelper&lt;TEntity, TRowID&gt; | 491 | 583 | 84% |
| EphemeralSecureString | 57 | 58 | 98% |
| ReflectionSerializer | 67 | 74 | 91% |
| SqlContainer | 116 | 148 | 78% |
| SqlContainerExtensions | 2 | 2 | 100% |
| StubAuditValueResolver | 4 | 4 | 100% |
| TableInfo | 20 | 20 | 100% |
| TransactionContext | 122 | 136 | 90% |
| TypeCoercionHelper | 35 | 39 | 90% |
| TypeMapRegistry | 104 | 104 | 100% |
| Utils | 24 | 26 | 92% |
| Uuid7Optimized | 108 | 136 | 79% |

## Environment note

Running the coverage command above requires the .NET SDK to be available on the execution environment. If the SDK is missing you can install it by following the guidance at <https://learn.microsoft.com/dotnet/core/install/>.
