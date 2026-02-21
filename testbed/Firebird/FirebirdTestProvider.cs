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
        var qp = context.QuotePrefix;
        var qs = context.QuoteSuffix;

        // Drop table if exists (Firebird 4.0+)
        sqlContainer.Query.AppendFormat(
            "DROP TABLE IF EXISTS {0}test_table{1}", qp, qs);

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
CREATE TABLE {0}test_table{1} (
    {0}id{1} BIGINT NOT NULL PRIMARY KEY,
    {0}name{1} VARCHAR(100) NOT NULL,
    {0}description{1} VARCHAR(1000) NOT NULL,
    {0}value{1} INTEGER NOT NULL,
    {0}is_active{1} BOOLEAN NOT NULL,
    {0}created_at{1} TIMESTAMP NOT NULL,
    {0}created_by{1} VARCHAR(100) NOT NULL,
    {0}updated_at{1} TIMESTAMP NOT NULL,
    {0}updated_by{1} VARCHAR(100) NOT NULL
)", qp, qs);

        await sqlContainer.ExecuteNonQueryAsync();
        Console.WriteLine("Table created successfully");
    }
}
