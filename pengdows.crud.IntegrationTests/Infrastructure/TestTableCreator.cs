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
        await container.ExecuteNonQueryAsync();
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
        CREATE TABLE IF NOT EXISTS TestTable (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            Value INTEGER NOT NULL,
            Description TEXT,
            IsActive INTEGER NOT NULL DEFAULT 1,
            CreatedOn TEXT NOT NULL,
            CreatedBy TEXT,
            LastUpdatedOn TEXT,
            LastUpdatedBy TEXT,
            Version INTEGER NOT NULL DEFAULT 1
        )";

    private string CreatePostgreSqlTableSql() => @"
        CREATE TABLE IF NOT EXISTS TestTable (
            Id BIGSERIAL PRIMARY KEY,
            Name VARCHAR(255) NOT NULL,
            Value INTEGER NOT NULL,
            Description TEXT,
            IsActive BOOLEAN NOT NULL DEFAULT TRUE,
            CreatedOn TIMESTAMP NOT NULL DEFAULT NOW(),
            CreatedBy VARCHAR(100),
            LastUpdatedOn TIMESTAMP,
            LastUpdatedBy VARCHAR(100),
            Version INTEGER NOT NULL DEFAULT 1
        )";

    private string CreateSqlServerTableSql() => @"
        IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[TestTable]') AND type in (N'U'))
        CREATE TABLE [dbo].[TestTable] (
            [Id] BIGINT IDENTITY(1,1) PRIMARY KEY,
            [Name] NVARCHAR(255) NOT NULL,
            [Value] INT NOT NULL,
            [Description] NVARCHAR(MAX),
            [IsActive] BIT NOT NULL DEFAULT 1,
            [CreatedOn] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
            [CreatedBy] NVARCHAR(100),
            [LastUpdatedOn] DATETIME2,
            [LastUpdatedBy] NVARCHAR(100),
            [Version] INT NOT NULL DEFAULT 1
        )";

    private string CreateMySqlTableSql() => @"
        CREATE TABLE IF NOT EXISTS TestTable (
            Id BIGINT AUTO_INCREMENT PRIMARY KEY,
            Name VARCHAR(255) NOT NULL,
            Value INT NOT NULL,
            Description TEXT,
            IsActive BOOLEAN NOT NULL DEFAULT TRUE,
            CreatedOn TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            CreatedBy VARCHAR(100),
            LastUpdatedOn TIMESTAMP NULL,
            LastUpdatedBy VARCHAR(100),
            Version INT NOT NULL DEFAULT 1
        )";

    private string CreateMariaDbTableSql() => CreateMySqlTableSql(); // Same as MySQL

    private string CreateOracleTableSql() => @"
        DECLARE
            table_exists NUMBER;
        BEGIN
            SELECT COUNT(*) INTO table_exists FROM user_tables WHERE table_name = 'TESTTABLE';
            IF table_exists = 0 THEN
                EXECUTE IMMEDIATE '
                    CREATE TABLE TestTable (
                        Id NUMBER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        Name VARCHAR2(255) NOT NULL,
                        Value NUMBER NOT NULL,
                        Description CLOB,
                        IsActive NUMBER(1) DEFAULT 1 NOT NULL,
                        CreatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP NOT NULL,
                        CreatedBy VARCHAR2(100),
                        LastUpdatedOn TIMESTAMP,
                        LastUpdatedBy VARCHAR2(100),
                        Version NUMBER DEFAULT 1 NOT NULL
                    )';
            END IF;
        END;";
}
