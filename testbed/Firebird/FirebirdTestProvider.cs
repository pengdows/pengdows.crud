#region

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
    /// DateTimeOffset against both Firebird temporal column types, through a TableGateway. Firebird 4+
    /// sends the value as an FbZonedDateTime(UTC instant): a TIMESTAMP WITH TIME ZONE column must
    /// store the instant, and a plain TIMESTAMP column must still store the UTC wall time (what the
    /// UTC-DateTime coercion always stored), so existing TIMESTAMP columns are unaffected.
    /// </summary>
    protected override async Task RunAdditionalTestsAsync()
    {
        var table = context.WrapObjectName("fb_tz_columns");
        await using var sc = context.CreateSqlContainer();
        sc.Query.Append($"RECREATE TABLE {table} ({context.WrapObjectName("id")} BIGINT NOT NULL PRIMARY KEY, " +
                        $"{context.WrapObjectName("plain_ts")} TIMESTAMP, " +
                        $"{context.WrapObjectName("zoned_ts")} TIMESTAMP WITH TIME ZONE)");
        await sc.ExecuteNonQueryAsync();

        try
        {
            var value = new DateTimeOffset(2026, 2, 21, 12, 34, 56, TimeSpan.FromHours(-5)); // 17:34:56Z
            var gateway = new TableGateway<FirebirdTimeZoneRow, long>(context);
            await gateway.CreateAsync(new FirebirdTimeZoneRow { Id = 1, PlainTs = value, ZonedTs = value }, context);

            var loaded = await gateway.RetrieveOneAsync(1L, context)
                         ?? throw new Exception("[Firebird TZ] row not found");
            if (loaded.PlainTs != value || loaded.ZonedTs != value)
            {
                throw new Exception(
                    $"[Firebird TZ] round trip: plain={loaded.PlainTs:O} zoned={loaded.ZonedTs:O}, expected instant {value:O}");
            }

            sc.Clear();
            sc.Query.Append($"SELECT CAST({context.WrapObjectName("plain_ts")} AS VARCHAR(40)) FROM {table}");
            var plainStored = await sc.ExecuteScalarOrNullAsync<string>();
            if (plainStored == null || !plainStored.StartsWith("2026-02-21 17:34:56", StringComparison.Ordinal))
            {
                throw new Exception($"[Firebird TZ] plain TIMESTAMP stored '{plainStored}', expected the UTC wall time 2026-02-21 17:34:56");
            }

            CheckOk("  [Firebird] DateTimeOffset into TIMESTAMP and TIMESTAMP WITH TIME ZONE: OK");
        }
        finally
        {
            sc.Clear();
            sc.Query.Append($"DROP TABLE {table}");
            await sc.ExecuteNonQueryAsync();
        }
    }

    [pengdows.crud.attributes.Table("fb_tz_columns")]
    private class FirebirdTimeZoneRow
    {
        [pengdows.crud.attributes.Id(true)]
        [pengdows.crud.attributes.Column("id", System.Data.DbType.Int64)]
        public long Id { get; set; }

        [pengdows.crud.attributes.Column("plain_ts", System.Data.DbType.DateTimeOffset)]
        public DateTimeOffset PlainTs { get; set; }

        [pengdows.crud.attributes.Column("zoned_ts", System.Data.DbType.DateTimeOffset)]
        public DateTimeOffset ZonedTs { get; set; }
    }
}
