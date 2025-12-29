using pengdows.crud;
using pengdows.crud.enums;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// Helper class to create test tables across different database providers
/// </summary>
public class TestTableCreator
{
    private readonly IDatabaseContext _context;

    public TestTableCreator(IDatabaseContext context)
    {
        _context = context;
    }

    public async Task CreateTestTableAsync()
    {
        var sql = _context.Product switch
        {
            SupportedDatabase.Sqlite => CreateSqliteTableSql(),
            SupportedDatabase.PostgreSql => CreatePostgreSqlTableSql(),
            SupportedDatabase.SqlServer => CreateSqlServerTableSql(),
            SupportedDatabase.MySql => CreateMySqlTableSql(),
            SupportedDatabase.MariaDb => CreateMariaDbTableSql(),
            SupportedDatabase.Oracle => CreateOracleTableSql(),
            _ => throw new NotSupportedException($"Database {_context.Product} not supported")
        };

        using var container = _context.CreateSqlContainer(sql);
        var result = await container.ExecuteNonQueryAsync();
        Console.WriteLine($"[TestTableCreator] Created {_context.Product} table, result={result}, DbMode={((DatabaseContext)_context).ConnectionMode}");
    }

    public async Task CreateAccountTableAsync()
    {
        var sql = _context.Product switch
        {
            SupportedDatabase.Sqlite => @"
                CREATE TABLE IF NOT EXISTS accounts (
                    id INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    balance DECIMAL(18,2) NOT NULL DEFAULT 0.00
                )",
            SupportedDatabase.PostgreSql => @"
                CREATE TABLE IF NOT EXISTS accounts (
                    id BIGINT PRIMARY KEY,
                    name VARCHAR(255) NOT NULL,
                    balance DECIMAL(18,2) NOT NULL DEFAULT 0.00
                )",
            SupportedDatabase.SqlServer => @"
                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[accounts]') AND type in (N'U'))
                CREATE TABLE [dbo].[accounts] (
                    [id] BIGINT PRIMARY KEY,
                    [name] NVARCHAR(255) NOT NULL,
                    [balance] DECIMAL(18,2) NOT NULL DEFAULT 0.00
                )",
            SupportedDatabase.MySql or SupportedDatabase.MariaDb => @"
                CREATE TABLE IF NOT EXISTS accounts (
                    id BIGINT PRIMARY KEY,
                    name VARCHAR(255) NOT NULL,
                    balance DECIMAL(18,2) NOT NULL DEFAULT 0.00
                )",
            SupportedDatabase.Oracle => @"
                DECLARE
                    table_exists NUMBER;
                BEGIN
                    SELECT COUNT(*) INTO table_exists FROM user_tables WHERE table_name = 'ACCOUNTS';
                    IF table_exists = 0 THEN
                        EXECUTE IMMEDIATE '
                            CREATE TABLE accounts (
                                id NUMBER PRIMARY KEY,
                                name VARCHAR2(255) NOT NULL,
                                balance DECIMAL(18,2) DEFAULT 0.00 NOT NULL
                            )';
                    END IF;
                END;",
            _ => throw new NotSupportedException($"Database {_context.Product} not supported")
        };

        using var container = _context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    private string CreateSqliteTableSql() => @"
        CREATE TABLE IF NOT EXISTS test_table (
            id BIGINT PRIMARY KEY,
            name TEXT NOT NULL,
            value INTEGER NOT NULL,
            description TEXT,
            is_active INTEGER NOT NULL DEFAULT 1,
            created_at TEXT NOT NULL,
            created_by TEXT,
            updated_at TEXT,
            updated_by TEXT
        )";

    private string CreatePostgreSqlTableSql() => @"
        CREATE TABLE IF NOT EXISTS test_table (
            id BIGINT PRIMARY KEY,
            name VARCHAR(255) NOT NULL,
            value INTEGER NOT NULL,
            description TEXT,
            is_active BOOLEAN NOT NULL DEFAULT TRUE,
            created_at TIMESTAMP NOT NULL DEFAULT NOW(),
            created_by VARCHAR(100),
            updated_at TIMESTAMP,
            updated_by VARCHAR(100)
        )";

    private string CreateSqlServerTableSql() => @"
        IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[test_table]') AND type in (N'U'))
        CREATE TABLE [dbo].[test_table] (
            [id] BIGINT PRIMARY KEY,
            [name] NVARCHAR(255) NOT NULL,
            [value] INT NOT NULL,
            [description] NVARCHAR(MAX),
            [is_active] BIT NOT NULL DEFAULT 1,
            [created_at] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
            [created_by] NVARCHAR(100),
            [updated_at] DATETIME2,
            [updated_by] NVARCHAR(100)
        )";

    private string CreateMySqlTableSql() => @"
        CREATE TABLE IF NOT EXISTS test_table (
            id BIGINT PRIMARY KEY,
            name VARCHAR(255) NOT NULL,
            value INT NOT NULL,
            description TEXT,
            is_active BOOLEAN NOT NULL DEFAULT TRUE,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            created_by VARCHAR(100),
            updated_at TIMESTAMP NULL,
            updated_by VARCHAR(100)
        )";

    private string CreateMariaDbTableSql() => CreateMySqlTableSql(); // Same as MySQL

    private string CreateOracleTableSql() => @"
        DECLARE
            table_exists NUMBER;
        BEGIN
            SELECT COUNT(*) INTO table_exists FROM user_tables WHERE table_name = 'TEST_TABLE';
            IF table_exists = 0 THEN
                EXECUTE IMMEDIATE '
                    CREATE TABLE test_table (
                        id NUMBER PRIMARY KEY,
                        name VARCHAR2(255) NOT NULL,
                        value NUMBER NOT NULL,
                        description CLOB,
                        is_active NUMBER(1) DEFAULT 1 NOT NULL,
                        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP NOT NULL,
                        created_by VARCHAR2(100),
                        updated_at TIMESTAMP,
                        updated_by VARCHAR2(100)
                    )';
            END IF;
        END;";
}
