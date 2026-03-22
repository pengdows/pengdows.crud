using pengdows.crud.enums;
using pengdows.crud.infrastructure;

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
            SupportedDatabase.Snowflake => CreateSnowflakeTableSql(),
            SupportedDatabase.Oracle => CreateOracleTableSql(),
            _ => throw new NotSupportedException($"Database {_context.Product} not supported")
        };

        await using var container = _context.CreateSqlContainer(sql);
        var result = await container.ExecuteNonQueryAsync();
        Console.WriteLine(
            $"[TestTableCreator] Created {_context.Product} table, result={result}, DbMode={((DatabaseContext)_context).ConnectionMode}");
    }

    public async Task CreateRoundTripTableAsync()
    {
        var table = IntegrationObjectNameHelper.Table(_context, "round_trip_entity");
        var idCol = _context.WrapObjectName("id");
        var textCol = _context.WrapObjectName("text_value");
        var unicodeCol = _context.WrapObjectName("text_unicode");
        var nullCol = _context.WrapObjectName("text_nullable");
        var intCol = _context.WrapObjectName("int_value");
        var longCol = _context.WrapObjectName("long_value");
        var decimalCol = _context.WrapObjectName("decimal_value");
        var boolCol = _context.WrapObjectName("bool_value");
        var dtoCol = _context.WrapObjectName("datetimeoffset_value");
        var guidCol = _context.WrapObjectName("guid_value");
        var binCol = _context.WrapObjectName("binary_value");

        if (_context.Product == SupportedDatabase.Firebird)
        {
            await using var tx = _context.BeginTransaction();
            try
            {
                var sqlFb = $@"
                    CREATE TABLE {table} (
                        {idCol} BIGINT NOT NULL PRIMARY KEY,
                        {textCol} VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
                        {unicodeCol} VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
                        {nullCol} VARCHAR(255) CHARACTER SET UTF8,
                        {intCol} INTEGER NOT NULL,
                        {longCol} BIGINT NOT NULL,
                        {decimalCol} DECIMAL(18,8) NOT NULL,
                        {boolCol} SMALLINT NOT NULL,
                        {dtoCol} TIMESTAMP NOT NULL,
                        {guidCol} CHAR(16) CHARACTER SET OCTETS NOT NULL,
                        {binCol} BLOB SUB_TYPE 0 NOT NULL
                    )";
                await using var fbContainer = tx.CreateSqlContainer(sqlFb);
                await fbContainer.ExecuteNonQueryAsync();
                tx.Commit();
                return;
            }
            catch (Exception ex) when (ex.Message.Contains("already exists"))
            {
                // ignore
                return;
            }
        }

        var sql = _context.Product switch
        {
            SupportedDatabase.Sqlite => $@"
                CREATE TABLE IF NOT EXISTS {table} (
                    {idCol} BIGINT PRIMARY KEY,
                    {textCol} TEXT NOT NULL,
                    {unicodeCol} TEXT NOT NULL,
                    {nullCol} TEXT,
                    {intCol} INTEGER NOT NULL,
                    {longCol} BIGINT NOT NULL,
                    {decimalCol} REAL NOT NULL,
                    {boolCol} INTEGER NOT NULL,
                    {dtoCol} TEXT NOT NULL,
                    {guidCol} TEXT NOT NULL,
                    {binCol} BLOB NOT NULL
                )",
            SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb or SupportedDatabase.YugabyteDb => $@"
                CREATE TABLE IF NOT EXISTS {table} (
                    {idCol} BIGINT PRIMARY KEY,
                    {textCol} VARCHAR(255) NOT NULL,
                    {unicodeCol} VARCHAR(255) NOT NULL,
                    {nullCol} VARCHAR(255),
                    {intCol} INTEGER NOT NULL,
                    {longCol} BIGINT NOT NULL,
                    {decimalCol} DECIMAL(18,8) NOT NULL,
                    {boolCol} BOOLEAN NOT NULL,
                    {dtoCol} TIMESTAMPTZ NOT NULL,
                    {guidCol} UUID NOT NULL,
                    {binCol} BYTEA NOT NULL
                )",
            SupportedDatabase.Snowflake => $@"
                CREATE TABLE IF NOT EXISTS {table} (
                    {idCol} BIGINT PRIMARY KEY,
                    {textCol} VARCHAR(255) NOT NULL,
                    {unicodeCol} VARCHAR(255) NOT NULL,
                    {nullCol} VARCHAR(255),
                    {intCol} INTEGER NOT NULL,
                    {longCol} BIGINT NOT NULL,
                    {decimalCol} DECIMAL(18,8) NOT NULL,
                    {boolCol} BOOLEAN NOT NULL,
                    {dtoCol} TIMESTAMP_NTZ NOT NULL,
                    {guidCol} VARCHAR(36) NOT NULL,
                    {binCol} VARBINARY(256) NOT NULL
                )",
            SupportedDatabase.SqlServer => $@"
                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[round_trip_entity]') AND type in (N'U'))
                CREATE TABLE [dbo].[round_trip_entity] (
                    [id] BIGINT PRIMARY KEY,
                    [text_value] NVARCHAR(255) NOT NULL,
                    [text_unicode] NVARCHAR(255) NOT NULL,
                    [text_nullable] NVARCHAR(255),
                    [int_value] INT NOT NULL,
                    [long_value] BIGINT NOT NULL,
                    [decimal_value] DECIMAL(18,8) NOT NULL,
                    [bool_value] BIT NOT NULL,
                    [datetimeoffset_value] DATETIMEOFFSET NOT NULL,
                    [guid_value] UNIQUEIDENTIFIER NOT NULL,
                    [binary_value] VARBINARY(256) NOT NULL
                )",
            SupportedDatabase.MySql or SupportedDatabase.MariaDb or SupportedDatabase.TiDb => $@"
                CREATE TABLE IF NOT EXISTS {table} (
                    {idCol} BIGINT PRIMARY KEY,
                    {textCol} VARCHAR(255) NOT NULL,
                    {unicodeCol} VARCHAR(255) CHARACTER SET utf8mb4 NOT NULL,
                    {nullCol} VARCHAR(255),
                    {intCol} INT NOT NULL,
                    {longCol} BIGINT NOT NULL,
                    {decimalCol} DECIMAL(18,8) NOT NULL,
                    {boolCol} BOOLEAN NOT NULL,
                    {dtoCol} DATETIME(6) NOT NULL,
                    {guidCol} CHAR(36) NOT NULL,
                    {binCol} VARBINARY(256) NOT NULL
                )",
            SupportedDatabase.Oracle => $@"
                DECLARE
                    table_exists NUMBER;
                BEGIN
                    SELECT COUNT(*) INTO table_exists FROM user_tables WHERE table_name = 'ROUND_TRIP_ENTITY';
                    IF table_exists = 0 THEN
                        EXECUTE IMMEDIATE '
                            CREATE TABLE {table} (
                                {idCol} NUMBER PRIMARY KEY,
                                {textCol} VARCHAR2(255),
                                {unicodeCol} NVARCHAR2(255),
                                {nullCol} VARCHAR2(255),
                                {intCol} NUMBER(10) NOT NULL,
                                {longCol} NUMBER(19) NOT NULL,
                                {decimalCol} NUMBER(18,8) NOT NULL,
                                {boolCol} NUMBER(1) NOT NULL,
                                {dtoCol} TIMESTAMP WITH TIME ZONE NOT NULL,
                                {guidCol} VARCHAR2(36) NOT NULL,
                                {binCol} RAW(256)
                            )';
                    END IF;
                END;",
            SupportedDatabase.DuckDB => $@"
                CREATE TABLE IF NOT EXISTS {table} (
                    {idCol} BIGINT PRIMARY KEY,
                    {textCol} VARCHAR NOT NULL,
                    {unicodeCol} VARCHAR NOT NULL,
                    {nullCol} VARCHAR,
                    {intCol} INTEGER NOT NULL,
                    {longCol} BIGINT NOT NULL,
                    {decimalCol} DECIMAL(18,8) NOT NULL,
                    {boolCol} BOOLEAN NOT NULL,
                    {dtoCol} TIMESTAMPTZ NOT NULL,
                    {guidCol} UUID NOT NULL,
                    {binCol} BLOB NOT NULL
                )",
            _ => throw new NotSupportedException($"Database {_context.Product} not supported")
        };

        await using var container = _context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    public async Task CreateTortureTableAsync()
    {
        var table = IntegrationObjectNameHelper.Table(_context, "Default Order");
        var idCol = _context.WrapObjectName("Group By");
        var selectCol = _context.WrapObjectName("Select");
        var fromCol = _context.WrapObjectName("From");
        var userCol = _context.WrapObjectName("User Name");

        var sql = _context.Product switch
        {
            SupportedDatabase.Sqlite => $@"
                CREATE TABLE IF NOT EXISTS {table} (
                    {idCol} BIGINT PRIMARY KEY,
                    {selectCol} TEXT,
                    {fromCol} TEXT,
                    {userCol} TEXT
                )",
            SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb or SupportedDatabase.YugabyteDb
                or SupportedDatabase.Snowflake => $@"
                CREATE TABLE IF NOT EXISTS {table} (
                    {idCol} BIGINT PRIMARY KEY,
                    {selectCol} VARCHAR(255),
                    {fromCol} VARCHAR(255),
                    {userCol} VARCHAR(255)
                )",
            SupportedDatabase.SqlServer => $@"
                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'{table}') AND type in (N'U'))
                CREATE TABLE {table} (
                    {idCol} BIGINT PRIMARY KEY,
                    {selectCol} NVARCHAR(255),
                    {fromCol} NVARCHAR(255),
                    {userCol} NVARCHAR(255)
                )",
            SupportedDatabase.MySql or SupportedDatabase.MariaDb or SupportedDatabase.TiDb => $@"
                CREATE TABLE IF NOT EXISTS {table} (
                    {idCol} BIGINT PRIMARY KEY,
                    {selectCol} VARCHAR(255),
                    {fromCol} VARCHAR(255),
                    {userCol} VARCHAR(255)
                )",
            SupportedDatabase.Oracle => $@"
                DECLARE
                    table_exists NUMBER;
                BEGIN
                    SELECT COUNT(*) INTO table_exists FROM user_tables WHERE table_name = 'Default Order';
                    IF table_exists = 0 THEN
                        EXECUTE IMMEDIATE '
                            CREATE TABLE ""Default Order"" (
                                ""Group By"" NUMBER PRIMARY KEY,
                                ""Select"" VARCHAR2(255),
                                ""From"" VARCHAR2(255),
                                ""User Name"" VARCHAR2(255)
                            )';
                    END IF;
                END;",
            SupportedDatabase.Firebird => $@"
                EXECUTE BLOCK AS
                BEGIN
                    IF (NOT EXISTS(SELECT 1 FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = 'Default Order')) THEN
                        EXECUTE STATEMENT 'CREATE TABLE ""Default Order"" (
                            ""Group By"" BIGINT NOT NULL PRIMARY KEY,
                            ""Select"" VARCHAR(255),
                            ""From"" VARCHAR(255),
                            ""User Name"" VARCHAR(255)
                        )';
                END",
            SupportedDatabase.DuckDB => $@"
                CREATE TABLE IF NOT EXISTS {table} (
                    {idCol} BIGINT PRIMARY KEY,
                    {selectCol} VARCHAR,
                    {fromCol} VARCHAR,
                    {userCol} VARCHAR
                )",
            _ => throw new NotSupportedException($"Database {_context.Product} not supported")
        };

        // For Firebird DDL visibility
        if (_context.Product == SupportedDatabase.Firebird)
        {
            await using var tx = _context.BeginTransaction();
            try
            {
                await using var fbContainer = tx.CreateSqlContainer(sql);
                await fbContainer.ExecuteNonQueryAsync();
                tx.Commit();
            }
            catch (Exception ex) when (ex.Message.Contains("already exists"))
            {
                /* ignore */
            }

            return;
        }

        await using var container = _context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
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
        var table = IntegrationObjectNameHelper.Table(_context, "accounts");
        var sql = _context.Product switch
        {
            SupportedDatabase.Sqlite => string.Format(@"
                CREATE TABLE IF NOT EXISTS {2} (
                    {0}id{1} INTEGER PRIMARY KEY,
                    {0}name{1} TEXT NOT NULL,
                    {0}balance{1} DECIMAL(18,2) NOT NULL DEFAULT 0.00
                )", qp, qs, table),
            SupportedDatabase.PostgreSql => string.Format(@"
                CREATE TABLE IF NOT EXISTS {2} (
                    {0}id{1} BIGINT PRIMARY KEY,
                    {0}name{1} VARCHAR(255) NOT NULL,
                    {0}balance{1} DECIMAL(18,2) NOT NULL DEFAULT 0.00
                )", qp, qs, table),
            SupportedDatabase.Snowflake => string.Format(@"
                CREATE TABLE IF NOT EXISTS {2} (
                    {0}id{1} BIGINT PRIMARY KEY,
                    {0}name{1} VARCHAR(255) NOT NULL,
                    {0}balance{1} DECIMAL(18,2) NOT NULL DEFAULT 0.00
                )", qp, qs, table),
            SupportedDatabase.SqlServer => string.Format(@"
                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'{2}') AND type in (N'U'))
                CREATE TABLE {2} (
                    {0}id{1} BIGINT PRIMARY KEY,
                    {0}name{1} NVARCHAR(255) NOT NULL,
                    {0}balance{1} DECIMAL(18,2) NOT NULL DEFAULT 0.00
                )", qp, qs, table),
            SupportedDatabase.MySql or SupportedDatabase.MariaDb => string.Format(@"
                CREATE TABLE IF NOT EXISTS {2} (
                    {0}id{1} BIGINT PRIMARY KEY,
                    {0}name{1} VARCHAR(255) NOT NULL,
                    {0}balance{1} DECIMAL(18,2) NOT NULL DEFAULT 0.00
                )", qp, qs, table),
            SupportedDatabase.Oracle => string.Format(@"
                DECLARE
                    table_exists NUMBER;
                BEGIN
                    SELECT COUNT(*) INTO table_exists FROM user_tables WHERE table_name = 'ACCOUNTS';
                    IF table_exists = 0 THEN
                        EXECUTE IMMEDIATE '
                            CREATE TABLE {2} (
                                {0}id{1} NUMBER PRIMARY KEY,
                                {0}name{1} VARCHAR2(255) NOT NULL,
                                {0}balance{1} DECIMAL(18,2) DEFAULT 0.00 NOT NULL
                            )';
                    END IF;
                END;", qp, qs, table),
            _ => throw new NotSupportedException($"Database {_context.Product} not supported")
        };

        await using var container = _context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    private string CreateSqliteTableSql()
    {
        var table = IntegrationObjectNameHelper.Table(_context, "test_table");
        return $@"
        CREATE TABLE IF NOT EXISTS {table} (
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
        var table = IntegrationObjectNameHelper.Table(_context, "test_table");
        return $@"
        CREATE TABLE IF NOT EXISTS {table} (
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
        var table = IntegrationObjectNameHelper.Table(_context, "test_table");
        return $@"
        IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'{table}') AND type in (N'U'))
        CREATE TABLE {table} (
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
        var table = IntegrationObjectNameHelper.Table(_context, "test_table");
        return $@"
        CREATE TABLE IF NOT EXISTS {table} (
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

    private string CreateSnowflakeTableSql()
    {
        var table = IntegrationObjectNameHelper.Table(_context, "test_table");
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
        CREATE TABLE IF NOT EXISTS {table} (
            {idColumn} BIGINT PRIMARY KEY,
            {nameColumn} VARCHAR(255) NOT NULL,
            {valueColumn} INTEGER NOT NULL,
            {descriptionColumn} VARCHAR(1024),
            {isActiveColumn} BOOLEAN NOT NULL DEFAULT TRUE,
            {createdAtColumn} TIMESTAMP_NTZ NOT NULL DEFAULT CURRENT_TIMESTAMP(),
            {createdByColumn} VARCHAR(100),
            {updatedAtColumn} TIMESTAMP_NTZ,
            {updatedByColumn} VARCHAR(100)
        )";
    }

    private string CreateFirebirdTableSql()
    {
        var table = IntegrationObjectNameHelper.Table(_context, "test_table");
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
        var table = IntegrationObjectNameHelper.Table(_context, "test_table");
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
        var table = IntegrationObjectNameHelper.Table(_context, "test_table");
        var qp = _context.QuotePrefix;
        var qs = _context.QuoteSuffix;
        return string.Format(@"
        CREATE TABLE IF NOT EXISTS {2} (
            {0}id{1} BIGINT PRIMARY KEY,
            {0}name{1} VARCHAR(255) NOT NULL,
            {0}value{1} INTEGER NOT NULL,
            {0}description{1} VARCHAR(1024),
            {0}is_active{1} BOOLEAN NOT NULL DEFAULT TRUE,
            {0}created_at{1} TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            {0}created_by{1} VARCHAR(100),
            {0}updated_at{1} TIMESTAMP,
            {0}updated_by{1} VARCHAR(100)
        )", qp, qs, table);
    }

    private string CreateOracleTableSql()
    {
        var table = IntegrationObjectNameHelper.Table(_context, "test_table");
        var qp = _context.QuotePrefix;
        var qs = _context.QuoteSuffix;
        return string.Format(@"
        DECLARE
            table_exists NUMBER;
            sequence_exists NUMBER;
        BEGIN
            SELECT COUNT(*) INTO table_exists FROM user_tables WHERE table_name = 'TEST_TABLE';
            IF table_exists = 0 THEN
                EXECUTE IMMEDIATE '
                    CREATE TABLE {2} (
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

            SELECT COUNT(*) INTO sequence_exists FROM user_sequences WHERE LOWER(sequence_name) = 'test_table_seq';
            IF sequence_exists = 0 THEN
                EXECUTE IMMEDIATE '
                    CREATE SEQUENCE {0}test_table_seq{1}
                    START WITH 1
                    INCREMENT BY 1
                    NOCACHE
                    NOCYCLE';
            END IF;
        END;", qp, qs, table);
    }
}
