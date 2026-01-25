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
        if (_context.Product == SupportedDatabase.Firebird)
        {
            await CreateFirebirdTestTableAsync();
            return;
        }

        var sql = _context.Product switch
        {
            SupportedDatabase.Sqlite => CreateSqliteTableSql(),
            SupportedDatabase.PostgreSql => CreatePostgreSqlTableSql(),
            SupportedDatabase.SqlServer => CreateSqlServerTableSql(),
            SupportedDatabase.MySql => CreateMySqlTableSql(),
            SupportedDatabase.MariaDb => CreateMariaDbTableSql(),
            SupportedDatabase.DuckDB => CreateDuckDbTableSql(),
            SupportedDatabase.CockroachDb => CreatePostgreSqlTableSql(),
            SupportedDatabase.Oracle => CreateOracleTableSql(),
            _ => throw new NotSupportedException($"Database {_context.Product} not supported")
        };

        await using var container = _context.CreateSqlContainer(sql);
        var result = await container.ExecuteNonQueryAsync();
        Console.WriteLine(
            $"[TestTableCreator] Created {_context.Product} table, result={result}, DbMode={((DatabaseContext)_context).ConnectionMode}");
    }

    private async Task CreateFirebirdTestTableAsync()
    {
        await using var create = _context.CreateSqlContainer(CreateFirebirdTableSql());
        try
        {
            var result = await create.ExecuteNonQueryAsync();
            Console.WriteLine(
                $"[TestTableCreator] Created {_context.Product} table, result={result}, DbMode={((DatabaseContext)_context).ConnectionMode}");
        }
        catch (Exception ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            // Table already present; swallow
        }
    }

    public async Task CreateAccountTableAsync()
    {
        var qp = _context.QuotePrefix;
        var qs = _context.QuoteSuffix;
        var sql = _context.Product switch
        {
            SupportedDatabase.Sqlite => string.Format(@"
                CREATE TABLE IF NOT EXISTS {0}accounts{1} (
                    {0}id{1} INTEGER PRIMARY KEY,
                    {0}name{1} TEXT NOT NULL,
                    {0}balance{1} DECIMAL(18,2) NOT NULL DEFAULT 0.00
                )", qp, qs),
            SupportedDatabase.PostgreSql => string.Format(@"
                CREATE TABLE IF NOT EXISTS {0}accounts{1} (
                    {0}id{1} BIGINT PRIMARY KEY,
                    {0}name{1} VARCHAR(255) NOT NULL,
                    {0}balance{1} DECIMAL(18,2) NOT NULL DEFAULT 0.00
                )", qp, qs),
            SupportedDatabase.SqlServer => string.Format(@"
                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'{0}dbo{1}.{0}accounts{1}') AND type in (N'U'))
                CREATE TABLE {0}dbo{1}.{0}accounts{1} (
                    {0}id{1} BIGINT PRIMARY KEY,
                    {0}name{1} NVARCHAR(255) NOT NULL,
                    {0}balance{1} DECIMAL(18,2) NOT NULL DEFAULT 0.00
                )", qp, qs),
            SupportedDatabase.MySql or SupportedDatabase.MariaDb => string.Format(@"
                CREATE TABLE IF NOT EXISTS {0}accounts{1} (
                    {0}id{1} BIGINT PRIMARY KEY,
                    {0}name{1} VARCHAR(255) NOT NULL,
                    {0}balance{1} DECIMAL(18,2) NOT NULL DEFAULT 0.00
                )", qp, qs),
            SupportedDatabase.Oracle => string.Format(@"
                DECLARE
                    table_exists NUMBER;
                BEGIN
                    SELECT COUNT(*) INTO table_exists FROM user_tables WHERE table_name = 'ACCOUNTS';
                    IF table_exists = 0 THEN
                        EXECUTE IMMEDIATE '
                            CREATE TABLE {0}accounts{1} (
                                {0}id{1} NUMBER PRIMARY KEY,
                                {0}name{1} VARCHAR2(255) NOT NULL,
                                {0}balance{1} DECIMAL(18,2) DEFAULT 0.00 NOT NULL
                            )';
                    END IF;
                END;", qp, qs),
            _ => throw new NotSupportedException($"Database {_context.Product} not supported")
        };

        await using var container = _context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    private string CreateSqliteTableSql()
    {
        return @"
        CREATE TABLE IF NOT EXISTS test_table (
            id BIGINT PRIMARY KEY,
            name TEXT NOT NULL,
            value INTEGER NOT NULL,
            description TEXT,
            is_active INTEGER NOT NULL DEFAULT 1,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            created_by TEXT,
            updated_at DATETIME,
            updated_by TEXT
        )";
    }

    private string CreatePostgreSqlTableSql()
    {
        return @"
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
    }

    private string CreateSqlServerTableSql()
    {
        return @"
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
    }

    private string CreateMySqlTableSql()
    {
        return @"
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
    }

    private string CreateMariaDbTableSql()
    {
        return CreateMySqlTableSql();
        // Same as MySQL
    }

    private string CreateFirebirdTableSql()
    {
        var table = _context.WrapObjectName("test_table");
        var idColumn = _context.WrapObjectName("id");
        var nameColumn = _context.WrapObjectName("name");
        var valueColumn = _context.WrapObjectName("value");
        var descriptionColumn = _context.WrapObjectName("description");
        var isActiveColumn = _context.WrapObjectName("is_active");
        var createdAtColumn = _context.WrapObjectName("created_at");
        var createdByColumn = _context.WrapObjectName("created_by");
        var updatedAtColumn = _context.WrapObjectName("updated_at");
        var updatedByColumn = _context.WrapObjectName("updated_by");

        return $@"
        CREATE TABLE {table} (
            {idColumn} BIGINT NOT NULL PRIMARY KEY,
            {nameColumn} VARCHAR(255) NOT NULL,
            {valueColumn} INTEGER NOT NULL,
            {descriptionColumn} VARCHAR(1024),
            {isActiveColumn} SMALLINT NOT NULL,
            {createdAtColumn} TIMESTAMP NOT NULL,
            {createdByColumn} VARCHAR(100),
            {updatedAtColumn} TIMESTAMP,
            {updatedByColumn} VARCHAR(100)
        );";
    }

    private string CreateFirebirdDropBlockSql()
    {
        var table = _context.WrapObjectName("test_table");
        var target = "test_table".ToUpperInvariant();
        return $@"
        EXECUTE BLOCK AS
        BEGIN
            IF (EXISTS(
                SELECT 1
                FROM RDB$RELATIONS
                WHERE TRIM(UPPER(RDB$RELATION_NAME)) = '{target}'
            )) THEN
            BEGIN
                EXECUTE STATEMENT '
                    DROP TABLE {table}
                ';
            END
        END;";
    }

    private string CreateDuckDbTableSql()
    {
        var qp = _context.QuotePrefix;
        var qs = _context.QuoteSuffix;
        return string.Format(@"
        CREATE TABLE IF NOT EXISTS {0}test_table{1} (
            {0}id{1} BIGINT PRIMARY KEY,
            {0}name{1} VARCHAR(255) NOT NULL,
            {0}value{1} INTEGER NOT NULL,
            {0}description{1} VARCHAR(1024),
            {0}is_active{1} BOOLEAN NOT NULL DEFAULT TRUE,
            {0}created_at{1} TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            {0}created_by{1} VARCHAR(100),
            {0}updated_at{1} TIMESTAMP,
            {0}updated_by{1} VARCHAR(100)
        )", qp, qs);
    }

    private string CreateOracleTableSql()
    {
        var qp = _context.QuotePrefix;
        var qs = _context.QuoteSuffix;
        return string.Format(@"
        DECLARE
            table_exists NUMBER;
        BEGIN
            SELECT COUNT(*) INTO table_exists FROM user_tables WHERE table_name = 'TEST_TABLE';
            IF table_exists = 0 THEN
                EXECUTE IMMEDIATE '
                    CREATE TABLE {0}test_table{1} (
                        {0}id{1} NUMBER PRIMARY KEY,
                        {0}name{1} VARCHAR2(255) NOT NULL,
                        {0}value{1} NUMBER NOT NULL,
                        {0}description{1} CLOB,
                        {0}is_active{1} NUMBER(1) DEFAULT 1 NOT NULL,
                        {0}created_at{1} TIMESTAMP DEFAULT CURRENT_TIMESTAMP NOT NULL,
                        {0}created_by{1} VARCHAR2(100),
                        {0}updated_at{1} TIMESTAMP,
                        {0}updated_by{1} VARCHAR2(100)
                    )';
            END IF;
        END;", qp, qs);
    }
}