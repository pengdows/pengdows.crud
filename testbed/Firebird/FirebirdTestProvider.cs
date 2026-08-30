#region

using FirebirdSql.Data.FirebirdClient;
using pengdows.crud;

#endregion

namespace testbed.Firebird;

public class FirebirdTestProvider : TestProvider
{
    private readonly IDatabaseContext context;

    public FirebirdTestProvider(IDatabaseContext context, IServiceProvider serviceProvider)
        : base(context, serviceProvider)
    {
        this.context = context;
    }

    /// <summary>
    /// Firebird's default container database uses the NONE character set which only supports
    /// ASCII. Override the description to ASCII-only; other round-trip assertions still run.
    /// </summary>
    protected override string RoundTripDescription => "Hello World ASCII round-trip test string";

    protected override string RoundTripFidelityUnicodeText => "Hello World ASCII fidelity test string";

    public override async Task CreateTable()
    {
        var sqlContainer = context.CreateSqlContainer();
        var tableName = context.WrapObjectName("test_table");
        var idColumn = context.WrapObjectName("id");
        var nameColumn = context.WrapObjectName("name");
        var descriptionColumn = context.WrapObjectName("description");
        var valueColumn = context.WrapObjectName("value");
        var isActiveColumn = context.WrapObjectName("is_active");
        var createdAtColumn = context.WrapObjectName("created_at");
        var createdByColumn = context.WrapObjectName("created_by");
        var updatedAtColumn = context.WrapObjectName("updated_at");
        var updatedByColumn = context.WrapObjectName("updated_by");

        // Drop table if exists (Firebird 4.0+)
        sqlContainer.Query.AppendFormat(
            "DROP TABLE IF EXISTS {0}", tableName);

        try
        {
            await sqlContainer.ExecuteNonQueryAsync();
        }
        catch (Exception)
        {
            // Ignore drop errors (table may not exist, or IF EXISTS not supported)
        }

        sqlContainer.Clear();
        sqlContainer.Query.AppendFormat(@"
CREATE TABLE {0} (
    {1} BIGINT NOT NULL PRIMARY KEY,
    {2} VARCHAR(100) NOT NULL,
    {3} VARCHAR(1000) NOT NULL,
    {4} INTEGER NOT NULL,
    {5} BOOLEAN NOT NULL,
    {6} TIMESTAMP NOT NULL,
    {7} VARCHAR(100) NOT NULL,
    {8} TIMESTAMP NOT NULL,
    {9} VARCHAR(100) NOT NULL
	)", tableName, idColumn, nameColumn, descriptionColumn, valueColumn, isActiveColumn, createdAtColumn,
            createdByColumn, updatedAtColumn, updatedByColumn);

        await sqlContainer.ExecuteNonQueryAsync();
        Console.WriteLine("Table created successfully");
    }

    /// <summary>
    /// Firebird SuperServer exposes a native, per-database, instantly-effective idle-unload knob:
    /// RDB$LINGER (default 0/NULL — closes and discards the database's cache immediately after
    /// the last attachment closes). Set to a short, deterministic value so the probe doesn't need
    /// to guess or wait out an unknown default.
    /// </summary>
    protected override async Task<bool> TryEnableFastIdleUnloadAsync()
    {
        await using var sc = context.CreateSqlContainer("ALTER DATABASE SET LINGER TO 1");
        await sc.ExecuteNonQueryAsync();
        return true;
    }

    /// <summary>
    /// Forces the actual physical connections out of the ADO.NET pool so the probe's post-drain
    /// query is a genuine cold reconnect, not a warm one served from a still-open pooled
    /// connection that never actually left the process.
    /// </summary>
    protected override void ClearProviderPoolForIdleUnloadProbe()
    {
        FbConnection.ClearAllPools();
    }
}
