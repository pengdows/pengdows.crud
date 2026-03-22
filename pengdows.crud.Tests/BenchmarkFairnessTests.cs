using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace pengdows.crud.Tests;

public class BenchmarkFairnessTests
{
    [Fact]
    public void Benchmarks_DoNotApplySessionSettingsFixups()
    {
        var tokens = new[]
        {
            "BenchmarkSessionSettings.ApplyAsync",
            "SqlServerSessionSettings.ApplyAsync",
            "SessionSettingsConnectionInterceptor"
        };

        var offenders = FindOffenders(tokens, new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "BenchmarkSessionSettings.cs",
            "SqlServerSessionSettings.cs"
        });

        Assert.True(offenders.Count == 0,
            $"Session settings fix-ups found in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Benchmarks_DoNotContainSessionSettingsVariants()
    {
        var tokens = new[] { "WithSessionSettings", "WithSessionMgmt" };
        var offenders = FindOffenders(tokens, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.True(offenders.Count == 0,
            $"Session settings benchmark variants found in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Benchmarks_DoNotContainIndexedViewHints()
    {
        var tokens = new[] { "NOEXPAND" };
        var offenders = FindOffenders(tokens, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.True(offenders.Count == 0,
            $"Indexed view hints found in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void ApplesToApplesBenchmarks_UsePrebuiltContainersAndFactoryConnections()
    {
        const string fileName = "ApplesToApplesDapperBenchmarks.cs";
        var text = LoadBenchmarkText(fileName);

        // pengdows.crud side uses BuildRetrieve (container built once) + SetParameterValue (reuse per loop).
        // Dapper side uses pre-built SQL string + factory connection per op.
        AssertAllPresent(fileName, text, new[]
        {
            "BuildRetrieve",
            "SetParameterValue",
            "_readSingleSc",
            "_dapperSql",
            "CreateConnection",
            "ConnectionString",
            "OpenAsync",
            "QuerySingleOrDefaultAsync"
        });

        // Must NOT use the old per-iteration container-creation pattern
        AssertAllAbsent(fileName, text, new[]
        {
            "BuildSingleReadSql",
            "_pengdowsSql",
            "CreateSqlContainer(_pengdowsSql)"
        });
    }

    [Fact]
    public void ApplesToApplesBenchmarks_UseSqliteProvider()
    {
        const string fileName = "ApplesToApplesDapperBenchmarks.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "SqliteConnection",
            "SqliteFactory",
            "Microsoft.Data.Sqlite",
            "Mode=Memory",
            "Cache=Shared"
        });

        AssertAllAbsent(fileName, text, new[]
        {
            "DuckDBClientFactory",
            "DuckDBConnection",
            ".duckdb"
        });
    }

    [Fact]
    public void ApplesToApplesBenchmarks_ExposeFieldTypeDiagnostics()
    {
        const string fileName = "ApplesToApplesDapperBenchmarks.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "DumpFieldTypes",
            "GetFieldType",
            "Console.WriteLine"
        });
    }

    [Fact]
    public void EqualFootingCrudBenchmarks_PrewarmAllFrameworks()
    {
        const string fileName = "EqualFootingCrudBenchmarks.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "PreWarmFrameworkCachesAsync",
            "QueryFirstOrDefaultAsync<DapperBenchEntity>",
            "QueryAsync<DapperBenchEntity>",
            "ExecuteAsync(createSql",
            "ExecuteAsync(updateSql",
            "ExecuteAsync(deleteSql",
            "ExecuteScalarAsync<double>",
            "FromSqlRaw(readSingleSql",
            "ExecuteSqlRawAsync(deleteSql",
            "Database.ExecuteSqlRawAsync(updateSql"
        });
    }

    [Fact]
    public void PostgreSqlEqualFootingBenchmarks_PrewarmAllFrameworks()
    {
        const string fileName = "PostgreSqlEqualFootingBenchmarks.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "PreWarmFrameworkCachesAsync",
            "QueryFirstOrDefaultAsync<DapperBenchEntity>",
            "QueryAsync<DapperBenchEntity>",
            "ExecuteAsync(insertSql",
            "ExecuteAsync(updateSql",
            "ExecuteAsync(deleteSql",
            "ExecuteScalarAsync<double>",
            "FromSqlRaw(readSingleSql",
            "ExecuteSqlRawAsync(deleteSql",
            "Database.ExecuteSqlRawAsync(updateSql"
        });
    }

    [Fact]
    public void PostgreSqlEqualFootingBenchmarks_AliasesEfScalarAggregateAsValue()
    {
        const string fileName = "PostgreSqlEqualFootingBenchmarks.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "const string aggregateSql = \"SELECT AVG(salary) AS \\\"Value\\\" FROM benchmark WHERE is_active = TRUE\"",
            "SqlQueryRaw<double>(aggregateSql)"
        });
    }

    [Fact]
    public void EqualFootingCrudBenchmarks_SplitDeleteOnlyFromDeleteInsertCycle()
    {
        const string fileName = "EqualFootingCrudBenchmarks.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "DeleteOnly_Pengdows",
            "DeleteOnly_Dapper",
            "DeleteOnly_EntityFramework",
            "DeleteInsertCycle_Pengdows",
            "DeleteInsertCycle_Dapper",
            "DeleteInsertCycle_EntityFramework",
            "BeginTransactionAsync",
            "Rollback()",
            "Clone(tx)"
        });

        AssertAllAbsent(fileName, text, new[]
        {
            "public async Task<int> Delete_Pengdows()",
            "public async Task<int> Delete_Dapper()",
            "public async Task<int> Delete_EntityFramework()"
        });
    }

    [Fact]
    public void PostgreSqlEqualFootingBenchmarks_SplitDeleteOnlyFromDeleteInsertCycle()
    {
        const string fileName = "PostgreSqlEqualFootingBenchmarks.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "DeleteOnly_Pengdows",
            "DeleteOnly_Dapper",
            "DeleteOnly_EntityFramework",
            "DeleteInsertCycle_Pengdows",
            "DeleteInsertCycle_Dapper",
            "DeleteInsertCycle_EntityFramework",
            "BeginTransactionAsync",
            "Rollback()",
            "Clone(tx)"
        });

        AssertAllAbsent(fileName, text, new[]
        {
            "public async Task<int> Delete_Pengdows()",
            "public async Task<int> Delete_Dapper()",
            "public async Task<int> Delete_EntityFramework()"
        });
    }

    [Fact]
    public void SqliteDateHandlingBenchmarks_ExposeFieldTypeDiagnostics()
    {
        var fileName = Path.Combine("Internal", "SqliteDateHandlingBenchmarks.cs");
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "SqliteDateHandlingBenchmarks",
            "DumpFieldTypes",
            "GetFieldType",
            "SqliteConnection"
        });
    }

    [Fact]
    public void SqliteDateHandlingBenchmarks_IsNotSealed()
    {
        var fileName = Path.Combine("Internal", "SqliteDateHandlingBenchmarks.cs");
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "public class SqliteDateHandlingBenchmarks"
        });

        AssertAllAbsent(fileName, text, new[]
        {
            "sealed class SqliteDateHandlingBenchmarks"
        });
    }

    [Fact]
    public void ReaderMappingBenchmark_UsesDuckDbParameterMarkersForSeedInsert()
    {
        var fileName = Path.Combine("Internal", "ReaderMappingBenchmark.cs");
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "INSERT INTO test_entities (id, name, email, age, salary, is_active, created_at, score) ",
            "VALUES ($id, $name, $email, $age, $salary, $is_active, $created_at, $score)"
        });

        AssertAllAbsent(fileName, text, new[]
        {
            "VALUES (@id, @name, @email, @age, @salary, @is_active, @created_at, @score)"
        });
    }

    [Fact]
    public void TypeHandlingBenchmarks_ExposeDateTimeStringCoercion()
    {
        var fileName = Path.Combine("Internal", "TypeHandlingBenchmarks.cs");
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "Coercion_DateTime_Read_String",
            "ResolveCoercer",
            "DateTime"
        });
    }

    [Fact]
    public void BenchmarkProgram_UsesExplicitBenchmarkDiscovery()
    {
        const string fileName = "Program.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "BenchmarkAttribute",
            "FromTypes",
            "GetTypes"
        });
    }

    [Fact]
    public void BenchmarkProgram_SupportsOptInBenchmarks()
    {
        const string fileName = "Program.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "OptInBenchmarkAttribute",
            "IsOptInBenchmarkEnabled",
            "--include-opt-in",
            "CRUD_BENCH_INCLUDE_OPT_IN"
        });
    }

    [Fact]
    public void BenchmarkProgram_WritesCrossFrameworkRatioSidecar()
    {
        const string fileName = "Program.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "CrossFrameworkRatioWriter",
            "CrossFrameworkRatioWriter.Write"
        });
    }

    [Fact]
    public void SqliteConcurrencyBenchmark_UsesIsolatedDatabaseFilesAndDefensiveSchemaCreation()
    {
        const string fileName = "SqliteConcurrencyBenchmark.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "Path.GetTempPath()",
            "Guid.NewGuid()",
            "CREATE TABLE IF NOT EXISTS TestEntities",
            "DeleteDatabaseFileIfPresent",
            "DisposeExistingContext"
        });
    }

    [Fact]
    public void SqliteConcurrencyBenchmark_UsesDatabaseGeneratedIds()
    {
        const string fileName = "SqliteConcurrencyBenchmark.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "[Id(writable: false)]",
            "AUTOINCREMENT"
        });

        AssertAllAbsent(fileName, text, new[]
        {
            "[Id(writable: true)]"
        });
    }

    [Fact]
    public void CrossFrameworkRatioWriter_EmitsExplicitCrossFrameworkRatios()
    {
        const string fileName = "CrossFrameworkRatioWriter.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "P÷D",
            "EF÷P",
            "_Pengdows",
            "_Dapper",
            "_EntityFramework",
            "BenchmarkDotNet's built-in Ratio column"
        });
    }

    [Fact]
    public void CrossFrameworkRatioWriter_UsesCorrectnessArtifactsToFilterInvalidRows()
    {
        const string fileName = "CrossFrameworkRatioWriter.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "BenchmarkCorrectnessArtifacts.LoadForSummary",
            "if (!means.IsValid(Framework.Pengdows) || !means.IsValid(Framework.Dapper))",
            "Rows requiring invalid framework results are excluded."
        });
    }

    [Fact]
    public void ConnectionPoolProtectionBenchmarks_RecordAndWriteCorrectnessFailures()
    {
        const string fileName = "ConnectionPoolProtectionBenchmarks.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "ConcurrentDictionary<CorrectnessIssueKey, int>",
            "MarkInvalid(",
            "if (entity == null)",
            "if (affected != 1)",
            "BenchmarkCorrectnessArtifacts.Write(",
            "nameof(ConnectionPoolProtectionBenchmarks)"
        });
    }

    [Fact]
    public void ConnectionPoolProtectionBenchmarks_DoesNotRethrowWorkloadFailures()
    {
        const string fileName = "ConnectionPoolProtectionBenchmarks.cs";
        var text = LoadBenchmarkText(fileName);

        Assert.Contains("catch (Exception ex)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("throw;", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionPoolProtectionBenchmarks_DoNotContainMixedOpsScenario()
    {
        const string fileName = "ConnectionPoolProtectionBenchmarks.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllAbsent(fileName, text, new[]
        {
            "ScenarioMixedOps",
            "MixedOps_Pengdows",
            "MixedOps_Dapper",
            "MixedOps_EntityFramework"
        });
    }

    [Fact]
    public void OptInBenchmarkAttribute_IsDefined()
    {
        const string fileName = "OptInBenchmarkAttribute.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "AttributeUsage(AttributeTargets.Class",
            "public sealed class OptInBenchmarkAttribute : Attribute"
        });
    }

    [Fact]
    public void MySqlDefaultConcurrencyBenchmarks_DefinesThreeScenarios()
    {
        const string fileName = "MySqlDefaultConcurrencyBenchmarks.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "[OptInBenchmark]",
            "public class MySqlDefaultConcurrencyBenchmarks",
            "public async Task ReadOnly_Pengdows_MySql()",
            "public async Task WriteOnly_Pengdows_MySql()",
            "public async Task RandomMix_Pengdows_MySql()"
        });
    }

    [Fact]
    public void MySqlDefaultConcurrencyBenchmarks_UsesDefaultMySqlContainerAndProvider()
    {
        const string fileName = "MySqlDefaultConcurrencyBenchmarks.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "mysql:8.0",
            "MySqlClientFactory.Instance",
            "[Params(32, 64, 128, 256)]",
            "RunConcurrentWithErrors"
        });
    }

    [Fact]
    public void MySqlDefaultConcurrencyBenchmarks_SupportsBothMySqlProviders()
    {
        const string fileName = "MySqlDefaultConcurrencyBenchmarks.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "enum MySqlBenchmarkProvider",
            "[Params(MySqlBenchmarkProvider.MySqlData, MySqlBenchmarkProvider.MySqlConnector)]",
            "MySqlConnectorFactory.Instance",
            "MySql.Data.MySqlClient.MySqlClientFactory.Instance",
            "Provider={Provider}"
        });
    }

    private static List<string> FindOffenders(IEnumerable<string> tokens, HashSet<string> excludedFiles)
    {
        var benchmarksDir = GetBenchmarksDirectory();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(benchmarksDir, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(file);
            if (excludedFiles.Contains(fileName))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            if (tokens.Any(token => text.Contains(token, StringComparison.Ordinal)))
            {
                offenders.Add(fileName);
            }
        }

        return offenders;
    }

    private static string GetBenchmarksDirectory()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return Path.Combine(root, "benchmarks", "CrudBenchmarks");
    }

    private static string LoadBenchmarkText(string fileName)
    {
        var path = Path.Combine(GetBenchmarksDirectory(), fileName);
        return File.ReadAllText(path);
    }

    private static void AssertAllPresent(string fileName, string text, IEnumerable<string> tokens)
    {
        var missing = tokens.Where(token => !text.Contains(token, StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0,
            $"{fileName} missing: {string.Join(", ", missing)}");
    }

    private static void AssertAllAbsent(string fileName, string text, IEnumerable<string> tokens)
    {
        var present = tokens.Where(token => text.Contains(token, StringComparison.Ordinal)).ToList();
        Assert.True(present.Count == 0,
            $"{fileName} should not contain: {string.Join(", ", present)}");
    }
}
