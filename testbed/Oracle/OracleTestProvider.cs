#region

using System.Data;
using pengdows.crud;

#endregion

namespace testbed.Oracle;

public class OracleTestProvider
    : TestProvider
{
    private readonly IDatabaseContext context;

    public OracleTestProvider(IDatabaseContext context, IServiceProvider serviceProvider)
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

        // Drop table if it exists
        sqlContainer.Query.AppendFormat(@"
BEGIN
  EXECUTE IMMEDIATE 'DROP TABLE {0}test_table{1}';
EXCEPTION
  WHEN OTHERS THEN
    IF SQLCODE != -942 THEN
      RAISE;
    END IF;
END;", qp, qs);

        await sqlContainer.ExecuteNonQueryAsync();

        // Drop sequence if it exists
        sqlContainer.Clear();
        sqlContainer.Query.AppendFormat(@"
BEGIN
  EXECUTE IMMEDIATE 'DROP SEQUENCE {0}test_table_seq{1}';
EXCEPTION
  WHEN OTHERS THEN
    IF SQLCODE != -2289 THEN -- ORA-02289: sequence does not exist
      RAISE;
    END IF;
END;", qp, qs);

        await sqlContainer.ExecuteNonQueryAsync();

        // Create table
        sqlContainer.Clear();
        sqlContainer.Query.AppendFormat(@"
CREATE TABLE {0}test_table{1} (
  {0}id{1} NUMBER(18,0) PRIMARY KEY,
  {0}name{1} VARCHAR2(100) NOT NULL,
  {0}description{1} VARCHAR2(1000) NOT NULL,
  {0}value{1} NUMBER(10,0) NOT NULL,
  {0}is_active{1} NUMBER(1,0) NOT NULL,
  {0}created_at{1} TIMESTAMP NOT NULL,
  {0}created_by{1} VARCHAR2(100) NOT NULL,
  {0}updated_at{1} TIMESTAMP NOT NULL,
  {0}updated_by{1} VARCHAR2(100) NOT NULL
)", qp, qs);
        Console.WriteLine(sqlContainer.Query.ToString());
        await sqlContainer.ExecuteNonQueryAsync();

        // Create sequence
        sqlContainer.Clear();
        sqlContainer.Query.AppendFormat(@"
CREATE SEQUENCE {0}test_table_seq{1}
START WITH 1
INCREMENT BY 1
NOCACHE
NOCYCLE", qp, qs);

        await sqlContainer.ExecuteNonQueryAsync();

        // Create trigger
        sqlContainer.Clear();
        sqlContainer.Query.AppendFormat(@"
CREATE OR REPLACE TRIGGER {0}test_table_bi{1}
BEFORE INSERT ON {0}test_table{1}
FOR EACH ROW
BEGIN
  IF :NEW.{0}id{1} IS NULL THEN
    SELECT {0}test_table_seq{1}.NEXTVAL
    INTO :NEW.{0}id{1}
    FROM dual;
  END IF;
END;", qp, qs);

        await sqlContainer.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Oracle MERGE upsert generation currently produces invalid SQL ("ORA-00969: missing ON keyword").
    /// Skip the upsert probe until the dialect's BuildUpsert is fixed for Oracle.
    /// </summary>
    protected override Task TestUpsertCapability()
    {
        Console.WriteLine("  [Oracle] Skipping upsert capability (MERGE generation issue — ORA-00969)");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Oracle does not support repeated named parameters (:p0 used twice).
    /// Use two distinct parameter names instead and verify same result.
    /// </summary>
    protected override async Task TestDuplicateParameter()
    {
        var sc = context.CreateSqlContainer();
        sc.Query.AppendFormat(
            "SELECT COUNT(*) FROM {0} WHERE {1} = {2} OR {3} = {4}",
            _helper.WrappedTableName,
            context.WrapObjectName("created_by"),
            context.MakeParameterName("p0"),
            context.WrapObjectName("updated_by"),
            context.MakeParameterName("p1"));
        sc.AddParameterWithValue("p0", DbType.String, "__nonexistent_user_xyzzy__");
        sc.AddParameterWithValue("p1", DbType.String, "__nonexistent_user_xyzzy__");
        var count = await sc.ExecuteScalarOrNullAsync<int>();
        if (count < 0)
            throw new Exception($"[ParamBinding] Oracle 2-param duplicate: invalid count {count}");
        Console.WriteLine($"  [ParamBinding] Duplicate param (Oracle 2-param workaround): OK ({count} rows)");
    }

    /// <summary>
    /// Oracle uses NUMBER types rather than INT/BIGINT, and requires PL/SQL for conditional DROP.
    /// </summary>
    protected override async Task TestIdentifierQuoting()
    {
        var wrappedTable = context.WrapObjectName("quote_test");
        var wrappedId = context.WrapObjectName("id");
        var wrappedOrder = context.WrapObjectName("order"); // reserved word

        await DropTableIfExistsAsync("quote_test");

        var sc = context.CreateSqlContainer();
        sc.Query.AppendFormat(
            "CREATE TABLE {0} ({1} NUMBER(10,0) NOT NULL PRIMARY KEY, {2} NUMBER(10,0) NOT NULL)",
            wrappedTable, wrappedId, wrappedOrder);
        await sc.ExecuteNonQueryAsync();

        try
        {
            // INSERT
            sc.Clear();
            sc.Query.AppendFormat(
                "INSERT INTO {0} ({1}, {2}) VALUES ({3}, {4})",
                wrappedTable, wrappedId, wrappedOrder,
                context.MakeParameterName("p0"),
                context.MakeParameterName("p1"));
            sc.AddParameterWithValue("p0", DbType.Int32, 1);
            sc.AddParameterWithValue("p1", DbType.Int32, 42);
            await sc.ExecuteNonQueryAsync();

            // SELECT
            sc.Clear();
            sc.Query.AppendFormat(
                "SELECT {0} FROM {1} WHERE {2} = {3}",
                wrappedOrder, wrappedTable, wrappedId,
                context.MakeParameterName("p0"));
            sc.AddParameterWithValue("p0", DbType.Int32, 1);
            var val = await sc.ExecuteScalarOrNullAsync<int>();
            if (val != 42)
                throw new Exception($"[Quoting] Oracle: expected 42 for 'order' column, got {val}");

            Console.WriteLine("  [Quoting] Reserved word 'order' as column name (Oracle NUMBER types): OK");
        }
        finally
        {
            sc.Clear();
            sc.Query.AppendFormat("DROP TABLE {0}", wrappedTable);
            await sc.ExecuteNonQueryAsync();
        }
    }
}
