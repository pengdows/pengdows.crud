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

    /// <summary>
    /// This image's ASE instance defaults to the iso_1 (Latin-1) character set rather than a
    /// Unicode one, so non-ASCII characters in the base class's round-trip strings fail with
    /// "Error converting characters into server's character set." Override to ASCII-only,
    /// matching Firebird's equivalent override for the same reason.
    /// </summary>
    protected override string RoundTripDescription => "Hello World ASCII round-trip test string";

    protected override string RoundTripFidelityUnicodeText => "Hello World ASCII fidelity test string";

    public override async Task CreateTable()
    {
        const string objectIdName = "dbo.test_table";
        var tableName = context.WrapObjectName(objectIdName);
        var idColumn = context.WrapObjectName("id");
        var nameColumn = context.WrapObjectName("name");
        var descriptionColumn = context.WrapObjectName("description");
        var valueColumn = context.WrapObjectName("value");
        var isActiveColumn = context.WrapObjectName("is_active");
        var createdAtColumn = context.WrapObjectName("created_at");
        var createdByColumn = context.WrapObjectName("created_by");
        var updatedAtColumn = context.WrapObjectName("updated_at");
        var updatedByColumn = context.WrapObjectName("updated_by");

        var drop = context.CreateSqlContainer();
        // ASE's OBJECT_ID() in this build only accepts the single-argument form — the
        // 2-argument (name, type) overload fails with "wrong number or type of argument(s)"
        // (verified live against the real container).
        drop.Query.Append($@"
IF OBJECT_ID('{objectIdName}') IS NOT NULL
  DROP TABLE {tableName}
");
        await drop.ExecuteNonQueryAsync();

        var create = context.CreateSqlContainer();
        // No trailing ';' — verified live that ASE rejects it ("Incorrect syntax near ';'.")
        // after a CREATE TABLE statement, same as after MERGE. Columns must match the shared
        // TestTable entity (testbed/TestTable.cs) in full, including value/is_active — the
        // original DDL here predated those columns and never actually ran against a live server.
        create.Query.Append($@"
CREATE TABLE {tableName} (
  {idColumn}          BIGINT      NOT NULL UNIQUE,
  {nameColumn}        VARCHAR(100) NOT NULL,
  {descriptionColumn} VARCHAR(1000) NOT NULL,
  {valueColumn}       INT         NOT NULL,
  {isActiveColumn}    BIT         NOT NULL,
  {createdAtColumn}   DATETIME    NOT NULL,
  {createdByColumn}   VARCHAR(100) NOT NULL,
  {updatedAtColumn}   DATETIME    NOT NULL,
  {updatedByColumn}   VARCHAR(100) NOT NULL
)
");
        await create.ExecuteNonQueryAsync();
    }
}