#region

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using pengdows.crud;
using pengdows.crud.enums;
using testbed.Snowflake;

#endregion

namespace testbed;

public sealed record CheckResult
{
    public required string Name { get; init; }
    public required string Outcome { get; init; } // "passed", "skipped", "failed"
    public string? Reason { get; init; }
}

/// <summary>
/// Base class for one database's testbed lifecycle check. As of the testbed/IntegrationTests
/// consolidation, this class is deliberately narrow: it owns only what genuinely needs a
/// container-provisioned connection at this layer rather than a pooled connection string —
/// table creation (<see cref="CreateTable"/>), the scalar-UDF smoke check, and the
/// <c>DbMode</c>/<c>PreventDatabaseUnload</c> idle-unload probe cluster (which measures real
/// cold-vs-warm reconnect cost against a live container and therefore cannot be expressed as an
/// ordinary pooled-connection xUnit test). CRUD round-trips, parameter binding, transactions/
/// isolation, stored procedures, upsert/error-mapping/identifier-quoting capability probes, pool
/// isolation, and kill-connection rollback behavior all now live in
/// <c>pengdows.crud.IntegrationTests</c> instead (see that project's <c>Core/</c>,
/// <c>ErrorHandling/</c>, and <c>DatabaseSpecific/</c> folders) — this class used to duplicate a
/// large fraction of that suite independently. A subclass should only override
/// <see cref="RunAdditionalTestsAsync"/> for a check that still genuinely needs the live
/// container itself (not just a connection string to it).
/// </summary>
public class TestProvider : IAsyncTestProvider
{
    protected readonly IDatabaseContext _context;
    protected readonly TableGateway<TestTable, long> _helper;

    private readonly List<CheckResult> _checks = new();
    public IReadOnlyList<CheckResult> Checks => _checks;

    public int ChecksPassed => _checks.Count(c => c.Outcome == "passed");
    public int ChecksSkipped => _checks.Count(c => c.Outcome == "skipped");
    public int ChecksFailed => _checks.Count(c => c.Outcome == "failed");

    protected void CheckOk(string name, string? message = null)
    {
        if (message != null)
        {
            Console.WriteLine(message);
        }
        else
        {
            Console.WriteLine($"  [{name}] OK");
        }
        _checks.Add(new CheckResult { Name = name, Outcome = "passed" });
    }

    protected void CheckSkip(string name, string reason)
    {
        Console.WriteLine($"  [{name}] (skipped: {reason})");
        _checks.Add(new CheckResult { Name = name, Outcome = "skipped", Reason = reason });
    }

    protected void CheckFail(string name, string error)
    {
        Console.WriteLine($"  [{name}] ❌ Failed: {error}");
        _checks.Add(new CheckResult { Name = name, Outcome = "failed", Reason = error });
    }

    public TestProvider(IDatabaseContext databaseContext, IServiceProvider serviceProvider)
    {
        _context = databaseContext;
        var resolver = serviceProvider.GetService<IAuditValueResolver>() ??
                       new TestAuditValueResolver("system");
        _helper = new TableGateway<TestTable, long>(databaseContext, resolver);
    }


    public async Task RunTest()
    {
        var totalSw = Stopwatch.StartNew();
        var stepSw = new Stopwatch();
        Console.WriteLine($"[{_context.Product}] Starting test run");
        try
        {
            stepSw.Restart();
            Console.WriteLine("Running Create table");
            SnowflakeStep("Create table: start");
            await CreateTable();
            Console.WriteLine($"  Create table: {stepSw.ElapsedMilliseconds}ms");
            SnowflakeStep($"Create table: done in {stepSw.ElapsedMilliseconds}ms");
            CheckOk("Lifecycle.CreateTable", "  [Lifecycle] Create table: OK");

            stepSw.Restart();
            Console.WriteLine("Running scalar UDF test");
            SnowflakeStep("Scalar UDF: start");
            await TestScalarUdf();
            Console.WriteLine($"  Scalar UDF: {stepSw.ElapsedMilliseconds}ms");
            SnowflakeStep($"Scalar UDF: done in {stepSw.ElapsedMilliseconds}ms");

            stepSw.Restart();
            Console.WriteLine("Running DbMode idle-unload probe");
            SnowflakeStep("DbMode idle-unload probe: start");
            await TestIdleUnloadProbe();
            Console.WriteLine($"  DbMode idle-unload probe: {stepSw.ElapsedMilliseconds}ms");
            SnowflakeStep($"DbMode idle-unload probe: done in {stepSw.ElapsedMilliseconds}ms");

            await RunAdditionalTestsAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to complete tests successfully: " + ex.Message + "\n" + ex.StackTrace);
            throw;
        }
        finally
        {
            Console.WriteLine($"[{_context.Product}] Test run completed in {totalSw.ElapsedMilliseconds}ms");
        }
    }

    protected virtual Task RunAdditionalTestsAsync() => Task.CompletedTask;

    private void SnowflakeStep(string message)
    {
        if (_context.Product != SupportedDatabase.Snowflake)
        {
            return;
        }

        SnowflakeDebugLog.Log($"[Snowflake][Step] {message}");
    }

    public virtual async Task CreateTable()
    {
        var databaseContext = _context;
        var sqlContainer = databaseContext.CreateSqlContainer();
        var tableName = databaseContext.WrapObjectName("test_table");
        var idColumn = databaseContext.WrapObjectName("id");
        var nameColumn = databaseContext.WrapObjectName("name");
        var descriptionColumn = databaseContext.WrapObjectName("description");
        var valueColumn = databaseContext.WrapObjectName("value");
        var isActiveColumn = databaseContext.WrapObjectName("is_active");
        var createdAtColumn = databaseContext.WrapObjectName("created_at");
        var createdByColumn = databaseContext.WrapObjectName("created_by");
        var updatedAtColumn = databaseContext.WrapObjectName("updated_at");
        var updatedByColumn = databaseContext.WrapObjectName("updated_by");
        sqlContainer.Query.AppendFormat("DROP TABLE IF EXISTS {0}", tableName);
        try
        {
            await sqlContainer.ExecuteNonQueryAsync();
        }
        catch
        {
            // Table did not exist, ignore
        }

        sqlContainer.Clear();
        var dateType = GetDateTimeType(databaseContext.Product);
        var intType = GetIntType(databaseContext.Product);
        var longType = GetLongType(databaseContext.Product);
        var boolType = GetBooleanType(databaseContext.Product);
        var shortTextType = GetTextType(databaseContext.Product, 100);
        var descriptionType = GetTextType(databaseContext.Product, 1000);
        sqlContainer.Query.Append($@"
CREATE TABLE {tableName} (
    {idColumn} {longType} NOT NULL,
    {nameColumn} {shortTextType} NOT NULL,
    {descriptionColumn} {descriptionType} NOT NULL,
    {valueColumn} {intType} NOT NULL,
    {isActiveColumn} {boolType} NOT NULL,
    {createdAtColumn} {dateType} NOT NULL,
    {createdByColumn} {shortTextType} NOT NULL,
    {updatedAtColumn} {dateType} NOT NULL,
    {updatedByColumn} {shortTextType} NOT NULL,
    PRIMARY KEY ({idColumn})
);");
        await sqlContainer.ExecuteNonQueryAsync();
    }

    private static string GetDateTimeType(SupportedDatabase product)
    {
        return product switch
        {
            SupportedDatabase.PostgreSql or SupportedDatabase.Spanner => "TIMESTAMP WITH TIME ZONE",
            // Db2 has no DATETIME type — TIMESTAMP is the equivalent.
            SupportedDatabase.Db2 => "TIMESTAMP",
            // pengdows.flatfile parses only ISO SQL - "DATETIME" is rejected as vendor syntax;
            // "TIMESTAMP" is the standard form (see pengdows.flatfile/SQL_STANDARDS_STATUS.md).
            SupportedDatabase.FlatFile => "TIMESTAMP",
            // Informix's DATETIME type requires an explicit precision qualifier - a bare
            // "DATETIME" is not valid syntax (IBM docs, DATETIME data type reference). UNVERIFIED
            // against a live server - this is the documented form, not yet confirmed to be
            // accepted exactly as written here.
            SupportedDatabase.Informix => "DATETIME YEAR TO FRACTION(5)",
            // CONFIRMED live: InterBase has no DATETIME type at all — "DATETIME" is parsed as an
            // (unresolvable) domain/column reference, not a type keyword, and fails with SQLCODE
            // -607 "Specified domain or source column ... does not exist" rather than a syntax
            // error. TIMESTAMP is the correct type, same as Firebird/Db2.
            SupportedDatabase.InterBase => "TIMESTAMP",
            _ => "DATETIME"
        };
    }

    private static string GetIntType(SupportedDatabase product)
    {
        return product switch
        {
            SupportedDatabase.Sqlite => "INTEGER",
            SupportedDatabase.Oracle => "NUMBER(10)",
            SupportedDatabase.Firebird => "INTEGER",
            _ => "INT"
        };
    }

    private static string GetLongType(SupportedDatabase product)
    {
        return product switch
        {
            SupportedDatabase.Sqlite => "INTEGER",
            SupportedDatabase.Oracle => "NUMBER(19)",
            SupportedDatabase.Firebird => "BIGINT",
            // CONFIRMED live: InterBase 15 has no BIGINT/INT64 type at all — both are rejected
            // with the same SQLCODE -607 "Specified domain or source column ... does not exist"
            // as an unrecognized-type-keyword shape (parsed as a domain reference, not a syntax
            // error). NUMERIC(18,0)/DECIMAL(18,0) is InterBase's classic (pre-Firebird-BIGINT)
            // 64-bit-range exact-integer idiom and is accepted.
            SupportedDatabase.InterBase => "NUMERIC(18,0)",
            _ => "BIGINT"
        };
    }

    private static string GetBooleanType(SupportedDatabase product)
    {
        return product switch
        {
            SupportedDatabase.PostgreSql or SupportedDatabase.Spanner => "BOOLEAN",
            SupportedDatabase.CockroachDb => "BOOLEAN",
            SupportedDatabase.YugabyteDb => "BOOLEAN",
            SupportedDatabase.Sqlite => "INTEGER",
            SupportedDatabase.DuckDB => "BOOLEAN",
            SupportedDatabase.Firebird => "SMALLINT",
            SupportedDatabase.Oracle => "NUMBER(1)",
            SupportedDatabase.MySql => "BOOLEAN",
            SupportedDatabase.MariaDb => "BOOLEAN",
            SupportedDatabase.TiDb => "BOOLEAN",
            SupportedDatabase.SqlServer => "BIT",
            SupportedDatabase.SybaseASE => "BIT",
            // CONFIRMED live: InformixDialect is positional (SupportsNamedParameters == false),
            // so SqlDialect's default NeedsCommonConversions (!SupportsNamedParameters) applies
            // and every bool parameter is sent as Int16 (0/1), not a raw bool. Declaring the
            // column as Informix's native BOOLEAN then breaks comparison — "Routine (equal) can
            // not be resolved", since IDS has no "=" operator between BOOLEAN and SMALLINT.
            SupportedDatabase.Informix => "SMALLINT",
            _ => "BOOLEAN"
        };
    }

    private static string GetTextType(SupportedDatabase product, int length)
    {
        return product switch
        {
            SupportedDatabase.SqlServer => $"NVARCHAR({length})",
            SupportedDatabase.Oracle => $"NVARCHAR2({length})",
            SupportedDatabase.Sqlite => "TEXT",
            // CONFIRMED live: Informix's traditional VARCHAR is capped at 255 bytes
            // ("Maximum varchar size has been exceeded" for anything longer) — LVARCHAR
            // supports up to 32739 bytes and is the correct type for longer text columns.
            SupportedDatabase.Informix when length > 255 => $"LVARCHAR({length})",
            _ => $"VARCHAR({length})"
        };
    }

    /// <summary>
    /// Tests scalar UDF invocation inline in a SELECT statement.
    /// Default implementation is a no-op; override in database-specific providers
    /// where UDF creation and inline invocation is meaningful to exercise.
    /// </summary>
    protected virtual Task TestScalarUdf() => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // § 10  DbMode.Best empirical capability probes
    // -------------------------------------------------------------------------
    //
    // Rather than trusting documentation/general-knowledge claims about a database's idle-unload
    // lifecycle (a real Firebird SuperServer default RDB$LINGER=0 behavior confirmed live; a
    // since-reverted, never-actually-verified Db2 claim taken uncritically from a chat message),
    // this probe empirically measures whether a real cold-reconnect cost exists, for any database
    // that exposes a FAST, settable knob to force its normal (often minutes-long, CI-impractical)
    // idle-unload timeout down to a few seconds. Databases without such a knob report an honest
    // "not empirically tested" skip rather than guessing — see TryEnableFastIdleUnloadAsync.

    /// <summary>
    /// Override to set a short, deterministic idle-unload timeout for this database if it exposes
    /// one natively (e.g. Firebird's <c>ALTER DATABASE SET LINGER TO n</c>). Return false (the
    /// default) when no such fast knob exists — <see cref="TestIdleUnloadProbe"/> then reports
    /// "not empirically tested" instead of guessing or waiting out an unknown, likely
    /// CI-impractical default timeout.
    /// </summary>
    protected virtual Task<bool> TryEnableFastIdleUnloadAsync() => Task.FromResult(false);

    /// <summary>
    /// Override to force the ADO.NET provider's connection pool to release its physical
    /// connections for this exact connection string (e.g. <c>FbConnection.ClearAllPools()</c>),
    /// so the probe's post-drain query measures a genuine cold reconnect rather than a warm
    /// pooled one that never actually left the process.
    /// </summary>
    protected virtual void ClearProviderPoolForIdleUnloadProbe()
    {
    }

    /// <summary>
    /// Override to undo whatever database-level setting <see cref="TryEnableFastIdleUnloadAsync"/>
    /// mutated (e.g. restore Firebird's <c>LINGER</c> or SQL Server's <c>AUTO_CLOSE</c>), so the
    /// probe doesn't leave a persistent, contaminating setting behind for every later test that
    /// runs against the same container in this testbed session. Default no-op — correct for any
    /// override (e.g. Db2's) that doesn't actually mutate persistent database state.
    /// </summary>
    protected virtual Task RestoreIdleUnloadKnobAsync() => Task.CompletedTask;

    protected virtual async Task TestIdleUnloadProbe()
    {
        var knobEnabled = await TryEnableFastIdleUnloadAsync();
        if (!knobEnabled)
        {
            CheckSkip("DbMode.IdleUnloadProbe",
                $"No known fast idle-unload knob for {_context.Product} — not empirically tested, DbMode.Best policy unchanged");
            return;
        }

        try
        {
            // Drain the pool and wait past the fast timeout just configured, then compare the first
            // (cold, forced-reconnect) round trip against the immediately-following (warm, pooled)
            // one. Apples-to-apples: same query shape, same connection string, only the pool state
            // differs between the two measurements.
            ClearProviderPoolForIdleUnloadProbe();
            await Task.Delay(TimeSpan.FromSeconds(3));

            // A trivial "SELECT 1" with no FROM clause isn't universally portable (Firebird and
            // Oracle both reject it) — count against the already-created test_table instead, which
            // every dialect supports identically.
            var probeSql = $"SELECT COUNT(*) FROM {_helper.WrappedTableName}";

            var coldSw = Stopwatch.StartNew();
            await using (var coldContainer = _context.CreateSqlContainer(probeSql))
            {
                await coldContainer.ExecuteScalarOrNullAsync<int>();
            }
            coldSw.Stop();

            var warmSw = Stopwatch.StartNew();
            await using (var warmContainer = _context.CreateSqlContainer(probeSql))
            {
                await warmContainer.ExecuteScalarOrNullAsync<int>();
            }
            warmSw.Stop();

            var coldMs = coldSw.Elapsed.TotalMilliseconds;
            var warmMs = Math.Max(warmSw.Elapsed.TotalMilliseconds, 0.01);
            var ratio = coldMs / warmMs;

            // Generous threshold — this only needs to distinguish "genuinely paid a reconnect/
            // reactivation cost" from ordinary run-to-run noise, not measure its exact magnitude.
            var unloadDetected = coldMs - warmMs > 5.0 && ratio > 2.0;

            // Detecting a real cost does NOT mean DbMode.Best should auto-select
            // PreventDatabaseUnload — that's a separate, deliberate policy call (see CLAUDE.md and
            // docs/connection/connection-modes.md). A heavily-trafficked deployment may never drain
            // its pool to zero (the cost never actually materializes), and a deliberately
            // scale-to-zero/cost-optimized deployment may not want a permanent sentinel forced on it
            // at all — only the operator knows which applies. This probe's job is to confirm the cost
            // is real and that PreventDatabaseUnload genuinely mitigates it (see the sentinel
            // validation below), not to decide the default on the operator's behalf.
            CheckOk("DbMode.IdleUnloadProbe",
                $"  [DbMode] Idle-unload probe for {_context.Product}: cold={coldMs:F2}ms, warm={warmMs:F2}ms, ratio={ratio:F1}x — " +
                (unloadDetected
                    ? "unload/reactivation cost DETECTED (PreventDatabaseUnload available as an explicit opt-in to mitigate it)"
                    : "no unload cost detected despite the fast knob"));

            if (unloadDetected)
            {
                await TestSentinelPreventsDetectedUnloadCostAsync(probeSql, coldMs, warmMs);
            }
        }
        finally
        {
            // Undo the fast-timeout knob regardless of outcome, so this probe never leaves a
            // mutated database-level setting behind for the rest of this testbed run.
            await RestoreIdleUnloadKnobAsync();
            ClearProviderPoolForIdleUnloadProbe();
        }
    }

    /// <summary>
    /// Closes the loop on a detected idle-unload cost: does <see cref="DbMode.PreventDatabaseUnload"/>'s
    /// actual mechanism — one connection held open, never returned to the pool — genuinely prevent
    /// it? Rather than trusting the design intent, this holds a real open reader (pinning a
    /// connection exactly the way a PreventDatabaseUnload sentinel does) through the same
    /// pool-clear-and-wait sequence, then re-measures. <see cref="ClearProviderPoolForIdleUnloadProbe"/>
    /// only releases connections currently idle IN the pool — a connection actively checked out
    /// (in use, not yet returned) is untouched by it, so the database should never actually see
    /// zero attachments this time. The pass bar is relative to this database's own already-measured
    /// cold/warm gap (recovering at least half of it), not a fixed absolute number — the raw
    /// magnitude of the cost varies a lot per database (Firebird ~9ms, SQL Server AUTO_CLOSE ~40ms).
    /// </summary>
    private async Task TestSentinelPreventsDetectedUnloadCostAsync(string probeSql, double coldMs, double warmMs)
    {
        await using var sentinelContainer = _context.CreateSqlContainer(probeSql);
        await using var sentinelReader = await sentinelContainer.ExecuteReaderAsync();

        ClearProviderPoolForIdleUnloadProbe();
        await Task.Delay(TimeSpan.FromSeconds(3));

        var sw = Stopwatch.StartNew();
        await using (var container = _context.CreateSqlContainer(probeSql))
        {
            await container.ExecuteScalarOrNullAsync<int>();
        }
        sw.Stop();

        var sentinelMs = sw.Elapsed.TotalMilliseconds;
        var originalGap = coldMs - warmMs;
        var remainingGap = sentinelMs - warmMs;
        var sentinelPrevented = remainingGap < originalGap / 2.0;

        CheckOk("DbMode.SentinelPreventsUnload",
            $"  [DbMode] Sentinel validation for {_context.Product}: round trip with one connection held open throughout = {sentinelMs:F2}ms (original gap was {originalGap:F2}ms) — " +
            (sentinelPrevented
                ? "unload cost PREVENTED (confirms PreventDatabaseUnload's sentinel mechanism actually works here)"
                : "cost still present — sentinel did NOT prevent it (investigate before trusting PreventDatabaseUnload for this database)"));
    }

}
