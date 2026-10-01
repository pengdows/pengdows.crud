#region

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

}
