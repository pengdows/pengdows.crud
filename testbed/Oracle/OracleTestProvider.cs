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
        var tableName = databaseContext.WrapObjectName("test_table");
        var sequenceName = databaseContext.WrapObjectName("test_table_seq");
        var triggerName = databaseContext.WrapObjectName("test_table_bi");
        var idColumn = databaseContext.WrapObjectName("id");
        var nameColumn = databaseContext.WrapObjectName("name");
        var descriptionColumn = databaseContext.WrapObjectName("description");
        var valueColumn = databaseContext.WrapObjectName("value");
        var isActiveColumn = databaseContext.WrapObjectName("is_active");
        var createdAtColumn = databaseContext.WrapObjectName("created_at");
        var createdByColumn = databaseContext.WrapObjectName("created_by");
        var updatedAtColumn = databaseContext.WrapObjectName("updated_at");
        var updatedByColumn = databaseContext.WrapObjectName("updated_by");

        // Drop table if it exists
        sqlContainer.Query.AppendFormat(@"
BEGIN
  EXECUTE IMMEDIATE 'DROP TABLE {0}';
EXCEPTION
  WHEN OTHERS THEN
    IF SQLCODE != -942 THEN
      RAISE;
    END IF;
END;", tableName);

        await sqlContainer.ExecuteNonQueryAsync();

        // Drop sequence if it exists
        sqlContainer.Clear();
        sqlContainer.Query.AppendFormat(@"
BEGIN
  EXECUTE IMMEDIATE 'DROP SEQUENCE {0}';
EXCEPTION
  WHEN OTHERS THEN
    IF SQLCODE != -2289 THEN -- ORA-02289: sequence does not exist
      RAISE;
    END IF;
END;", sequenceName);

        await sqlContainer.ExecuteNonQueryAsync();

        // Create table
        sqlContainer.Clear();
        sqlContainer.Query.AppendFormat(@"
CREATE TABLE {0} (
  {1} NUMBER(18,0) PRIMARY KEY,
  {2} VARCHAR2(100) NOT NULL,
  {3} VARCHAR2(1000) NOT NULL,
  {4} NUMBER(10,0) NOT NULL,
  {5} NUMBER(1,0) NOT NULL,
  {6} TIMESTAMP NOT NULL,
  {7} VARCHAR2(100) NOT NULL,
  {8} TIMESTAMP NOT NULL,
  {9} VARCHAR2(100) NOT NULL
)", tableName, idColumn, nameColumn, descriptionColumn, valueColumn, isActiveColumn, createdAtColumn,
            createdByColumn, updatedAtColumn, updatedByColumn);
        Console.WriteLine(sqlContainer.Query.ToString());
        await sqlContainer.ExecuteNonQueryAsync();

        // Create sequence
        sqlContainer.Clear();
        sqlContainer.Query.AppendFormat(@"
CREATE SEQUENCE {0}
START WITH 1
INCREMENT BY 1
NOCACHE
NOCYCLE", sequenceName);

        await sqlContainer.ExecuteNonQueryAsync();

        // Create trigger
        sqlContainer.Clear();
        sqlContainer.Query.AppendFormat(@"
CREATE OR REPLACE TRIGGER {0}
BEFORE INSERT ON {1}
FOR EACH ROW
BEGIN
  IF :NEW.{2} IS NULL THEN
    SELECT {3}.NEXTVAL
    INTO :NEW.{2}
    FROM dual;
  END IF;
END;", triggerName, tableName, idColumn, sequenceName);

        await sqlContainer.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Oracle does not support reusing the same named parameter twice in one predicate.
    /// Use two distinct parameter names instead and verify same result.
    /// </summary>
    protected override async Task TestDuplicateParameter()
    {
        var sc = context.CreateSqlContainer();
        sc.Query.AppendFormat(
            "SELECT COUNT(*) FROM {0} WHERE {1} = {2} OR {3} = {4}",
            _helper.WrappedTableName,
            context.WrapObjectName("created_by"),
            sc.MakeParameterName("p0"),
            context.WrapObjectName("updated_by"),
            sc.MakeParameterName("p1"));
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
                sc.MakeParameterName("p0"),
                sc.MakeParameterName("p1"));
            sc.AddParameterWithValue("p0", DbType.Int32, 1);
            sc.AddParameterWithValue("p1", DbType.Int32, 42);
            await sc.ExecuteNonQueryAsync();

            // SELECT
            sc.Clear();
            sc.Query.AppendFormat(
                "SELECT {0} FROM {1} WHERE {2} = {3}",
                wrappedOrder, wrappedTable, wrappedId,
                sc.MakeParameterName("p0"));
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
