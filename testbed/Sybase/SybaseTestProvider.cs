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
        const string objectIdName = "dbo.test_table";
        var tableName = context.WrapObjectName(objectIdName);
        var idColumn = context.WrapObjectName("id");
        var nameColumn = context.WrapObjectName("name");
        var descriptionColumn = context.WrapObjectName("description");
        var createdAtColumn = context.WrapObjectName("created_at");
        var createdByColumn = context.WrapObjectName("created_by");
        var updatedAtColumn = context.WrapObjectName("updated_at");
        var updatedByColumn = context.WrapObjectName("updated_by");

        var drop = context.CreateSqlContainer();
        drop.Query.Append($@"
IF OBJECT_ID('{objectIdName}','U') IS NOT NULL
  DROP TABLE {tableName};
");
        await drop.ExecuteNonQueryAsync();

        var create = context.CreateSqlContainer();
        create.Query.Append($@"
CREATE TABLE {tableName} (
  {idColumn}          BIGINT      NOT NULL UNIQUE,
  {nameColumn}        VARCHAR(100) NOT NULL,
  {descriptionColumn} VARCHAR(1000) NOT NULL,
  {createdAtColumn}   DATETIME    NOT NULL,
  {createdByColumn}   VARCHAR(100) NOT NULL,
  {updatedAtColumn}   DATETIME    NOT NULL,
  {updatedByColumn}   VARCHAR(100) NOT NULL
);
");
        await create.ExecuteNonQueryAsync();
    }
}