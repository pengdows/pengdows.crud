#region

using pengdows.crud;

#endregion

namespace testbed;

public class FirebirdTestProvider : TestProvider
{
    private readonly IDatabaseContext context;

    public FirebirdTestProvider(IDatabaseContext context, IServiceProvider serviceProvider)
        : base(context, serviceProvider)
    {
        this.context = context;
    }

    public override async Task CreateTable()
    {
        var databaseContext = context;
        var sqlContainer = databaseContext.CreateSqlContainer();
        var qp = databaseContext.QuotePrefix;
        var qs = databaseContext.QuoteSuffix;

        // Drop table if it exists (Firebird-specific method)
        sqlContainer.Query.Append(@"
EXECUTE BLOCK AS
BEGIN
  IF (EXISTS(SELECT 1 FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = 'TEST_TABLE')) THEN
    EXECUTE STATEMENT 'DROP TABLE test_table';
END
");

        try
        {
            await sqlContainer.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Drop failed: " + ex.Message);
        }

        sqlContainer.Query.Clear();
        sqlContainer.Query.AppendFormat(@"
CREATE TABLE {0}test_table{1} (
    {0}id{1} BIGINT NOT NULL PRIMARY KEY,
    {0}name{1} VARCHAR(100) NOT NULL,
    {0}description{1} VARCHAR(1000) NOT NULL,
    {0}value{1} INTEGER NOT NULL,
    {0}is_active{1} SMALLINT NOT NULL,
    {0}created_at{1} TIMESTAMP NOT NULL,
    {0}created_by{1} VARCHAR(100) NOT NULL,
    {0}updated_at{1} TIMESTAMP NOT NULL,
    {0}updated_by{1} VARCHAR(100) NOT NULL
);", qp, qs);

        try
        {
            await sqlContainer.ExecuteNonQueryAsync();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message + "\n --- Continuing anyways");
        }
    }
}