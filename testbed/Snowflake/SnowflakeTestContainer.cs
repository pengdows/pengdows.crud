using pengdows.crud;
using Snowflake.Data.Client;

namespace testbed.Snowflake;

/// <summary>
/// Snowflake test container that assumes Snowflake is already running externally.
/// Credentials are supplied via environment variables; no Docker image is available.
/// Required env vars: SNOWFLAKE_ACCOUNT, SNOWFLAKE_USER, SNOWFLAKE_PASSWORD, SNOWFLAKE_WAREHOUSE.
/// Required when SNOWFLAKE_CREATE_DATABASE is false: SNOWFLAKE_DATABASE.
/// Optional env vars:
/// - SNOWFLAKE_CREATE_DATABASE (true/false, default: false)
/// - SNOWFLAKE_ADMIN_DATABASE (admin connection database; defaults to SNOWFLAKE_DATABASE when present)
/// - SNOWFLAKE_SCHEMA (schema for database mode; default: PUBLIC)
/// - SNOWFLAKE_TEST_PREFIX (prefix for generated schema/database names; default: PENGDOWS_TEST)
/// </summary>
public class SnowflakeTestContainer : TestContainer
{
    private readonly SnowflakeTestConfiguration _config;
    private Guid _cleanupId = Guid.Empty;

    public SnowflakeTestContainer()
    {
        _config = SnowflakeTestConfiguration.FromEnvironment();
    }

    public override async Task StartAsync()
    {
        var label = _config.UseDatabaseIsolation
            ? $"database {_config.TestDatabase}"
            : $"schema {_config.TestSchema}";

        var dropSqls = _config.GetDropCommands().ToList();
        var totalSw = System.Diagnostics.Stopwatch.StartNew();

        SnowflakeDebugLog.Log($"[Snowflake] StartAsync: using {label}");
        SnowflakeDebugLog.Log($"[Snowflake] Warehouse={_config.Warehouse}");

        try
        {
            await using var conn = SnowflakeDbFactory.Instance.CreateConnection();
            conn.ConnectionString = _config.AdminConnectionString;
            SnowflakeDebugLog.Log("[Snowflake] Opening admin connection...");
            var openSw = System.Diagnostics.Stopwatch.StartNew();
            await conn.OpenAsync();
            SnowflakeDebugLog.Log($"[Snowflake] Admin connection opened in {openSw.ElapsedMilliseconds}ms");

            await using var cmd = conn.CreateCommand();
            var createCommands = _config.GetCreateCommands().ToList();

            for (var i = 0; i < createCommands.Count; i++)
            {
                cmd.CommandText = createCommands[i];
                SnowflakeDebugLog.Log(
                    $"[Snowflake] Create ({i + 1}/{createCommands.Count}) start: {DescribeCommand(cmd.CommandText)}");
                var cmdSw = System.Diagnostics.Stopwatch.StartNew();
                await cmd.ExecuteNonQueryAsync();
                SnowflakeDebugLog.Log(
                    $"[Snowflake] Create ({i + 1}/{createCommands.Count}) " +
                    $"completed in {cmdSw.ElapsedMilliseconds}ms");

                // Register for emergency cleanup as soon as the first resource exists.
                // This guarantees the process-exit handler can drop it even if the
                // process is killed before DisposeAsyncCore runs.
                if (i == 0)
                {
                    _cleanupId = SnowflakeDatabaseCleanupRegistry.Register(
                        _config.AdminConnectionString, dropSqls, label);
                }
            }

            await conn.CloseAsync();
            Console.WriteLine($"[Snowflake] Created test {label} in {totalSw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            // If the first resource was created but a subsequent step failed (e.g. CREATE
            // DATABASE succeeded but CREATE SCHEMA failed), attempt an immediate cleanup
            // rather than leaving the orphaned resource until process exit.
            if (_cleanupId != Guid.Empty)
            {
                await TryDropAsync(dropSqls, label);
                SnowflakeDatabaseCleanupRegistry.Deregister(_cleanupId);
                _cleanupId = Guid.Empty;
            }

            SnowflakeDebugLog.Log($"[Snowflake] Admin connection or DDL failed: {ex}");

            throw new InvalidOperationException(
                $"[Snowflake] Cannot connect to Snowflake or create test {label}. " +
                $"Error: {ex.Message}", ex);
        }
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        var context = new DatabaseContext(_config.TestConnectionString, SnowflakeDbFactory.Instance);
        return Task.FromResult<IDatabaseContext>(context);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        var label = _config.UseDatabaseIsolation
            ? $"database {_config.TestDatabase}"
            : $"schema {_config.TestSchema}";

        try
        {
            await using var conn = SnowflakeDbFactory.Instance.CreateConnection();
            conn.ConnectionString = _config.AdminConnectionString;
            var openSw = System.Diagnostics.Stopwatch.StartNew();
            await conn.OpenAsync();
            SnowflakeDebugLog.Log($"[Snowflake] Admin connection opened in {openSw.ElapsedMilliseconds}ms");

            await using var cmd = conn.CreateCommand();
            var dropCommands = _config.GetDropCommands().ToList();
            for (var i = 0; i < dropCommands.Count; i++)
            {
                cmd.CommandText = dropCommands[i];
                SnowflakeDebugLog.Log(
                    $"[Snowflake] Drop ({i + 1}/{dropCommands.Count}) start: {DescribeCommand(cmd.CommandText)}");
                var cmdSw = System.Diagnostics.Stopwatch.StartNew();
                await cmd.ExecuteNonQueryAsync();
                SnowflakeDebugLog.Log(
                    $"[Snowflake] Drop ({i + 1}/{dropCommands.Count}) completed in {cmdSw.ElapsedMilliseconds}ms");
            }

            await conn.CloseAsync();
            Console.WriteLine($"[Snowflake] Dropped test {label}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Snowflake] Warning: Failed to drop test {label}: {ex.Message}");
        }
        finally
        {
            // Deregister regardless of success or failure so the process-exit handler
            // does not attempt a second drop after DisposeAsyncCore already ran.
            if (_cleanupId != Guid.Empty)
            {
                SnowflakeDatabaseCleanupRegistry.Deregister(_cleanupId);
                _cleanupId = Guid.Empty;
            }
        }
    }

    private async Task TryDropAsync(IReadOnlyList<string> dropSqls, string label)
    {
        try
        {
            await using var conn = SnowflakeDbFactory.Instance.CreateConnection();
            conn.ConnectionString = _config.AdminConnectionString;
            var openSw = System.Diagnostics.Stopwatch.StartNew();
            await conn.OpenAsync();
            SnowflakeDebugLog.Log($"[Snowflake] Admin connection opened in {openSw.ElapsedMilliseconds}ms");

            await using var cmd = conn.CreateCommand();
            for (var i = 0; i < dropSqls.Count; i++)
            {
                cmd.CommandText = dropSqls[i];
                SnowflakeDebugLog.Log(
                    $"[Snowflake] Cleanup ({i + 1}/{dropSqls.Count}) start: {DescribeCommand(cmd.CommandText)}");
                var cmdSw = System.Diagnostics.Stopwatch.StartNew();
                await cmd.ExecuteNonQueryAsync();
                SnowflakeDebugLog.Log(
                    $"[Snowflake] Cleanup ({i + 1}/{dropSqls.Count}) completed in {cmdSw.ElapsedMilliseconds}ms");
            }

            await conn.CloseAsync();
            Console.WriteLine($"[Snowflake] Cleaned up partial test {label} after startup failure");
        }
        catch (Exception dropEx)
        {
            Console.WriteLine(
                $"[Snowflake] Warning: Failed to clean up partial test {label}: {dropEx.Message}");
        }
    }

    private static string DescribeCommand(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return "<empty>";
        }

        var trimmed = sql.Trim().Replace(Environment.NewLine, " ");
        return trimmed.Length <= 160 ? trimmed : trimmed[..160] + "...";
    }
}
