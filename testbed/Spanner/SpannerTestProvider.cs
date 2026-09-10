using pengdows.crud;
using testbed.PostgreSQL;

namespace testbed.Spanner;

/// <summary>
/// Spanner rides <see cref="PostgreSQLTestProvider"/>'s PostgreSQL-wire-compatible test suite, but
/// its PostgreSQL interface (via PGAdapter) has a materially narrower type system than real
/// PostgreSQL — see CLAUDE.md's "Adding a New Database" checklist items 17/27 for the full list of
/// verified-live gaps. <see cref="PostgreSQLTestProvider.CreateTable"/>'s own <c>test_table</c> DDL
/// hardcodes plain <c>TIMESTAMP</c> (no time zone) for <c>created_at</c>/<c>updated_at</c>, which
/// Spanner's PostgreSQL interface rejects outright (<c>P0001: Type &lt;timestamp&gt; is not
/// supported.</c>) — and because that DDL failure was caught, logged, and silently ignored
/// ("Continuing anyways"), <c>test_table</c> never actually got created, and every later operation
/// against it failed with <c>relation "test_table" does not exist</c> instead of surfacing the real
/// root cause. This override fixes the column type only — everything else PostgreSQLTestProvider
/// does is otherwise reused as-is.
/// </summary>
public class SpannerTestProvider : PostgreSQLTestProvider
{
    public SpannerTestProvider(IDatabaseContext context, IServiceProvider serviceProvider)
        : base(context, serviceProvider)
    {
    }

    public override async Task CreateTable()
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
        sqlContainer.Query.AppendFormat(@"
-- Create table
CREATE TABLE {0} (
    {1} BIGINT PRIMARY KEY,
    {2} VARCHAR(100) NOT NULL,
    {3} VARCHAR(1000) NOT NULL,
    {4} INT NOT NULL,
    {5} BOOLEAN NOT NULL,
    {6} TIMESTAMP WITH TIME ZONE NOT NULL,
    {7} VARCHAR(100) NOT NULL,
    {8} TIMESTAMP WITH TIME ZONE NOT NULL,
    {9} VARCHAR(100) NOT NULL
);
", tableName, idColumn, nameColumn, descriptionColumn, valueColumn, isActiveColumn, createdAtColumn,
            createdByColumn, updatedAtColumn, updatedByColumn);
        // Unlike PostgreSQLTestProvider.CreateTable, do NOT swallow a failure here: a silently
        // ignored CREATE TABLE failure is exactly what turned this DDL bug into a much harder to
        // diagnose "relation test_table does not exist" crash three tests later.
        await sqlContainer.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Verified live (CLAUDE.md item 27): Spanner rejects an identifier containing a space even
    /// when correctly double-quoted ("Column name not valid: quote_test.display name") — a genuine
    /// platform limitation, not a workaround-able SQL shape difference. The xUnit
    /// pengdows.crud.IntegrationTests project's QuotingTortureTests already skips Spanner for the
    /// same reason; this override applies the same exclusion to the testbed harness's own,
    /// independent identifier-quoting check.
    /// </summary>
    protected override Task TestIdentifierQuoting()
    {
        CheckSkip("Quoting.SpaceInIdentifier",
            "Spanner rejects an identifier containing a space even when correctly double-quoted " +
            "(verified live) — a genuine platform limitation, not a testbed/SQL-generation gap.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Verified live: PostgreSQLTestProvider's raw-ADO.NET kill-probe (<c>pg_backend_pid()</c> /
    /// <c>pg_terminate_backend()</c>) relies on real PostgreSQL server administrative functions
    /// that Spanner's PostgreSQL interface (PGAdapter) does not implement
    /// ("P0001: Postgres function pg_backend_pid() is not supported"). A genuine platform
    /// limitation, not something pengdows.crud or this harness can work around.
    /// </summary>
    protected override Task TestTransactionRollbackOnKilledConnection()
    {
        CheckSkip("Spanner.TransactionRollbackOnKilledConnection",
            "Spanner's PostgreSQL interface (PGAdapter) does not implement pg_backend_pid()/" +
            "pg_terminate_backend() (verified live) — a genuine platform limitation.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Verified live: PostgreSQLTestProvider's explicit-identity-upsert test hardcodes
    /// <c>GENERATED ALWAYS AS IDENTITY</c>, which Spanner rejects ("P0001: Only
    /// &lt;GENERATED BY DEFAULT&gt; is supported for identity column."). Swapping in
    /// <c>GENERATED BY DEFAULT AS IDENTITY</c> would change what the test actually verifies — BY
    /// DEFAULT already accepts a client-supplied value without requiring
    /// <c>OVERRIDING SYSTEM VALUE</c>, so it wouldn't exercise the same override-a-generated-value
    /// scenario. Skipped rather than silently testing something weaker.
    /// </summary>
    protected override Task RunAdditionalTestsAsync()
    {
        CheckSkip("Spanner.GeneratedAlwaysIdentity",
            "Spanner only supports GENERATED BY DEFAULT AS IDENTITY, not GENERATED ALWAYS AS " +
            "IDENTITY (verified live) — the explicit-identity-upsert test's premise (overriding a " +
            "GENERATED ALWAYS value) doesn't apply the same way under BY DEFAULT semantics.");
        return Task.CompletedTask;
    }
}
