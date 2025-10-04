#region

using pengdows.crud;

#endregion

namespace testbed.Sybase;

public class SybaseTestProvider : TestProvider
{
    private readonly IDatabaseContext context;

    public SybaseTestProvider(IDatabaseContext ctx, IServiceProvider svcs)
        : base(ctx, svcs)
    {
        context = ctx;
    }

    public override async Task CreateTable()
    {
        var qp = context.QuotePrefix;
        var qs = context.QuoteSuffix;
        var tbl = $"{qp}test_table{qs}";

        var drop = context.CreateSqlContainer();
        drop.Query.Append(@"
 IF OBJECT_ID('dbo.test_table','U') IS NOT NULL
   DROP TABLE dbo.test_table;
 ");
        await drop.ExecuteNonQueryAsync();

        var create = context.CreateSqlContainer();
        create.Query.Append(@"
 CREATE TABLE dbo.test_table (
   id          BIGINT      NOT NULL UNIQUE,
   name        VARCHAR(100) NOT NULL,
   description VARCHAR(1000) NOT NULL,
   created_at  DATETIME    NOT NULL,
   created_by  VARCHAR(100) NOT NULL,
   updated_at  DATETIME    NOT NULL,
   updated_by  VARCHAR(100) NOT NULL
 );
 ");
        await create.ExecuteNonQueryAsync();
    }
}
