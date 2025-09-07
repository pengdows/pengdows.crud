#region

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud.fakeDb;

public class fakeDbCommand : DbCommand
{
    private bool _shouldFailOnExecute;
    private Exception? _customExecuteException;

    public fakeDbCommand(DbConnection connection)
    {
        Connection = connection;
        DbConnection = connection;
    }

    public fakeDbCommand()
    {
    }

    [AllowNull]
    public override string CommandText { get; set; }
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }

    protected override DbConnection? DbConnection { get; set; }
    [AllowNull]
    public new DbTransaction Transaction { get; set; }

    protected override DbParameterCollection DbParameterCollection
        => new FakeParameterCollection();

    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel()
    {
    }

    public override void Prepare()
    {
    }

    private fakeDbConnection? FakeConnection => Connection as fakeDbConnection;

    /// <summary>
    /// Sets the command to fail on execute operations
    /// </summary>
    public void SetFailOnExecute(bool shouldFail = true, Exception? customException = null)
    {
        _shouldFailOnExecute = shouldFail;
        _customExecuteException = customException;
    }

    private void ThrowIfShouldFail(string operation)
    {
        if (_shouldFailOnExecute)
        {
            throw _customExecuteException ?? new InvalidOperationException($"Simulated {operation} failure");
        }
    }

    public override int ExecuteNonQuery()
    {
        ThrowIfShouldFail(nameof(ExecuteNonQuery));

        var conn = FakeConnection;
        // Treat session-setting commands as no-op and do not consume queued NonQueryResults,
        // so tests that seed rows-affected for subsequent DML remain stable.
        if (!string.IsNullOrWhiteSpace(CommandText))
        {
            var trimmed = CommandText.TrimStart();
            var upper = trimmed.ToUpperInvariant();
            if (upper.StartsWith("SET ") || upper.StartsWith("PRAGMA ") || upper.Contains("ALTER SESSION SET"))
            {
                if (conn != null && !string.IsNullOrWhiteSpace(CommandText))
                {
                    conn.ExecutedNonQueryTexts.Add(CommandText);
                }
                return 0;
            }
        }
        if (conn != null && conn.NonQueryExecuteException != null)
        {
            var ex = conn.NonQueryExecuteException;
            conn.SetNonQueryExecuteException(null);
            throw ex;
        }
        if (conn != null && !string.IsNullOrEmpty(CommandText) && conn.CommandFailuresByText.TryGetValue(CommandText, out var exNonQuery))
        {
            throw exNonQuery;
        }
        if (conn != null && !string.IsNullOrWhiteSpace(CommandText))
        {
            conn.ExecutedNonQueryTexts.Add(CommandText);
        }
        if (conn != null && conn.NonQueryResults.Count > 0)
        {
            return conn.NonQueryResults.Dequeue();
        }

        return 1;
    }

    public override object? ExecuteScalar()
    {
        ThrowIfShouldFail(nameof(ExecuteScalar));

        var conn = FakeConnection;
        if (conn != null)
        {
            if (conn.ScalarExecuteException != null)
            {
                var ex = conn.ScalarExecuteException;
                conn.SetScalarExecuteException(null);
                throw ex;
            }
            if (!string.IsNullOrEmpty(CommandText) && conn.CommandFailuresByText.TryGetValue(CommandText, out var exScalar))
            {
                throw exScalar;
            }
            // Command-text-based result
            if (!string.IsNullOrEmpty(CommandText) && conn.ScalarResultsByCommand.TryGetValue(CommandText, out var commandResult))
            {
                return commandResult;
            }
            // If this is any kind of version query and the test queued a scalar (even null/empty), prefer the queued value
            if (!string.IsNullOrEmpty(CommandText))
            {
                var upper = CommandText.TrimStart().ToUpperInvariant();
                var isGenericVersion = upper == "SELECT VERSION()" || upper == "PRAGMA VERSION" || upper == "SELECT CURRENT_VERSION";
                var isFirebirdEngine = upper.Contains("RDB$GET_CONTEXT('SYSTEM', 'ENGINE_VERSION')");
                var isFirebirdMonitor = upper.Contains("MON$SERVER_VERSION");
                if ((isGenericVersion || isFirebirdEngine || isFirebirdMonitor) && conn.ScalarResults.Count > 0)
                {
                    return conn.ScalarResults.Dequeue();
                }
            }
            // Apply default scalar only to identity-returning paths and explicit version overrides
            if (conn.DefaultScalarResultOnce != null && !string.IsNullOrWhiteSpace(CommandText))
            {
                var trimmed = CommandText.TrimStart();
                var upper = trimmed.ToUpperInvariant();
                // Version override (tests may supply an explicit version string)
                if (upper == "SELECT VERSION()" || upper == "PRAGMA VERSION" || upper == "SELECT CURRENT_VERSION"
                    || upper.Contains("RDB$GET_CONTEXT('SYSTEM', 'ENGINE_VERSION')")
                    || upper.Contains("MON$SERVER_VERSION"))
                {
                    return conn.ConsumeDefaultScalarOnce();
                }
                // INSERT ... RETURNING should return generated IDs
                if (upper.StartsWith("INSERT") && (upper.Contains("RETURNING") || upper.Contains("OUTPUT INSERTED")))
                {
                    return conn.ConsumeDefaultScalarOnce();
                }
                // Identity retrieval SELECTs
                if (upper.Contains("SCOPE_IDENTITY") || upper.Contains("LASTVAL") || upper.Contains("LAST_INSERT_ROWID") || upper.Contains("LAST_INSERT_ID"))
                {
                    return conn.ConsumeDefaultScalarOnce();
                }
            }
            // Handle version queries automatically based on emulated product (do not consume the generic queue)
            if (!string.IsNullOrEmpty(CommandText))
            {
                var versionResult = GetVersionQueryResult(CommandText, conn.EmulatedProduct);
                if (versionResult != null)
                {
                    return versionResult;
                }
            }
            // Prefer queued results when present (test control) for non-version commands
            if (conn.ScalarResults.Count > 0)
            {
                return conn.ScalarResults.Dequeue();
            }
        }

        return 42;
    }

    private string? GetVersionQueryResult(string commandText, SupportedDatabase emulatedProduct)
    {
        var normalizedCommand = commandText.Trim().ToUpperInvariant();

        // Normalize to treat SELECT CURRENT_VERSION similar to SELECT VERSION()
        var isVersionQuery = normalizedCommand == "SELECT VERSION()" || normalizedCommand == "SELECT CURRENT_VERSION";

        // Handle recognized product/version queries with canned responses to stabilize tests
        return emulatedProduct switch
        {
            SupportedDatabase.SqlServer when normalizedCommand == "SELECT @@VERSION"
                => "Microsoft SQL Server 2019 (RTM) - 15.0.2000.5",

            SupportedDatabase.PostgreSql when isVersionQuery
                => "PostgreSQL 15.0 on x86_64-pc-linux-gnu",

            SupportedDatabase.MySql when isVersionQuery
                => "8.0.33",

            SupportedDatabase.MariaDb when isVersionQuery
                => "10.11.0-MariaDB",

            SupportedDatabase.Sqlite when normalizedCommand == "SELECT SQLITE_VERSION()"
                => "3.42.0",

            SupportedDatabase.Oracle when normalizedCommand.Contains("SELECT * FROM V$VERSION")
                => "Oracle Database 19c Enterprise Edition Release 19.0.0.0.0",

            SupportedDatabase.Firebird when normalizedCommand.Contains("RDB$GET_CONTEXT('SYSTEM', 'ENGINE_VERSION')")
                => "4.0.0",
            // Firebird monitor table fallback used by dialect tests
            SupportedDatabase.Firebird when normalizedCommand.Contains("MON$SERVER_VERSION")
                => "Firebird 4.0.2",

            SupportedDatabase.CockroachDb when isVersionQuery
                => "CockroachDB CCL v23.1.0",

            SupportedDatabase.DuckDB when isVersionQuery
                => "DuckDB 0.9.2",
            // DuckDB pragma fallback used by dialect tests
            SupportedDatabase.DuckDB when normalizedCommand == "PRAGMA VERSION"
                => "v0.9.2",

            _ => null
        };
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior _)
    {
        ThrowIfShouldFail(nameof(ExecuteDbDataReader));
        var conn = FakeConnection;
        if (conn != null && !string.IsNullOrEmpty(CommandText) && conn.CommandFailuresByText.TryGetValue(CommandText, out var exReader))
        {
            throw exReader;
        }
        if (conn != null && conn.ReaderResults.Count > 0)
        {
            return new fakeDbDataReader(conn.ReaderResults.Dequeue());
        }

        return new fakeDbDataReader();
    }

    // **Async overrides**
    public override Task<int> ExecuteNonQueryAsync(CancellationToken ct)
    {
        return Task.FromResult(ExecuteNonQuery());
    }

    public override Task<object?> ExecuteScalarAsync(CancellationToken ct)
    {
        return Task.FromResult(ExecuteScalar());
    }

    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(
        CommandBehavior behavior, CancellationToken ct)
    {
        return Task.FromResult<DbDataReader>(ExecuteDbDataReader(behavior));
    }

    public override Task PrepareAsync(CancellationToken ct)
    {
        Prepare();
        return Task.CompletedTask;
    }

    protected override DbParameter CreateDbParameter()
    {
        return new fakeDbParameter();
    }
}
