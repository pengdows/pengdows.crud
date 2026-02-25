using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// Creates the <c>type_hydration</c> table used by hydration-verification tests.
/// The table carries exactly one column per distinct DbType supported by pengdows.crud,
/// plus nullable variants and both integer/string enum storage modes.
///
/// Per-provider type decisions:
/// <list type="bullet">
///   <item>SQLite: REAL for float, double, and decimal; INTEGER for short/bool;
///     TEXT for datetime/datetimeoffset/guid/enum-string; BLOB for binary.</item>
///   <item>PostgreSQL / CockroachDB: REAL for float; DOUBLE PRECISION for double;
///     TIMESTAMP for DateTime; TIMESTAMPTZ for DateTimeOffset; UUID for guid; BYTEA for binary.</item>
///   <item>SQL Server: REAL for float; FLOAT for double; DATETIME2 for DateTime;
///     DATETIMEOFFSET for DateTimeOffset; UNIQUEIDENTIFIER for guid; VARBINARY(256) for binary.</item>
///   <item>MySQL / MariaDB: FLOAT for float; DOUBLE for double; DATETIME(6) for both date types
///     (no tz-aware column); CHAR(36) for guid; VARBINARY(256) for binary.</item>
///   <item>Oracle: BINARY_FLOAT (32-bit IEEE 754) for float; BINARY_DOUBLE (64-bit) for double;
///     NUMBER(5/10/19) for short/int/long; TIMESTAMP for DateTime;
///     TIMESTAMP WITH TIME ZONE for DateTimeOffset; VARCHAR2(36) for guid; RAW(256) for binary.</item>
///   <item>DuckDB: REAL for float; DOUBLE for double; TIMESTAMP for DateTime;
///     TIMESTAMPTZ for DateTimeOffset; UUID for guid; BLOB for binary.</item>
///   <item>Firebird: FLOAT (32-bit) for float; DOUBLE PRECISION for double;
///     SMALLINT for bool; TIMESTAMP for both date types (no tz-aware in Firebird 3.x);
///     CHAR(36) for guid; VARBINARY(256) for binary.</item>
///   <item>Snowflake: FLOAT4 declared (stored as 64-bit internally); DOUBLE for double;
///     TIMESTAMP_NTZ for both date types; VARCHAR(36) for guid; VARBINARY for binary.</item>
/// </list>
/// </summary>
public class TypeHydrationTableCreator
{
    private readonly IDatabaseContext _context;

    public TypeHydrationTableCreator(IDatabaseContext context)
    {
        _context = context;
    }

    public async Task CreateTableAsync()
    {
        if (_context.Product == SupportedDatabase.Firebird)
        {
            await CreateFirebirdTableAsync();
            return;
        }

        if (_context.Product == SupportedDatabase.Oracle)
        {
            await using var container = _context.CreateSqlContainer(CreateOracleSql());
            await container.ExecuteNonQueryAsync();
            return;
        }

        var sql = _context.Product switch
        {
            SupportedDatabase.Sqlite                                            => CreateSqliteSql(),
            SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb      => CreatePostgreSqlSql(),
            SupportedDatabase.SqlServer                                         => CreateSqlServerSql(),
            SupportedDatabase.MySql or SupportedDatabase.MariaDb               => CreateMySqlSql(),
            SupportedDatabase.DuckDB                                            => CreateDuckDbSql(),
            SupportedDatabase.Snowflake                                         => CreateSnowflakeSql(),
            _ => throw new NotSupportedException(
                $"Database {_context.Product} is not supported by TypeHydrationTableCreator")
        };

        await using var sc = _context.CreateSqlContainer(sql);
        await sc.ExecuteNonQueryAsync();
    }

    // -------------------------------------------------------------------------
    // Per-provider DDL
    // -------------------------------------------------------------------------

    private string CreateSqliteSql() => @"
CREATE TABLE IF NOT EXISTS type_hydration (
    id                 INTEGER  NOT NULL PRIMARY KEY,
    col_string         TEXT     NOT NULL,
    col_string_null    TEXT,
    col_short          INTEGER  NOT NULL,
    col_int            INTEGER  NOT NULL,
    col_int_null       INTEGER,
    col_long           INTEGER  NOT NULL,
    col_float          REAL     NOT NULL,
    col_double         REAL     NOT NULL,
    col_decimal        REAL     NOT NULL,
    col_bool           INTEGER  NOT NULL,
    col_bool_null      INTEGER,
    col_datetime       TEXT     NOT NULL,
    col_datetimeoffset TEXT     NOT NULL,
    col_guid           TEXT     NOT NULL,
    col_binary         BLOB,
    col_enum_int       INTEGER  NOT NULL,
    col_enum_str       TEXT     NOT NULL
)";
    // NOTE: SQLite REAL is IEEE 754 64-bit double — decimal round-trips with ~1e-7 relative error.

    private string CreatePostgreSqlSql() => @"
CREATE TABLE IF NOT EXISTS type_hydration (
    id                 BIGINT           NOT NULL PRIMARY KEY,
    col_string         VARCHAR(500)     NOT NULL,
    col_string_null    VARCHAR(500),
    col_short          SMALLINT         NOT NULL,
    col_int            INTEGER          NOT NULL,
    col_int_null       INTEGER,
    col_long           BIGINT           NOT NULL,
    col_float          REAL             NOT NULL,
    col_double         DOUBLE PRECISION NOT NULL,
    col_decimal        DECIMAL(18,8)    NOT NULL,
    col_bool           BOOLEAN          NOT NULL,
    col_bool_null      BOOLEAN,
    col_datetime       TIMESTAMP        NOT NULL,
    col_datetimeoffset TIMESTAMPTZ      NOT NULL,
    col_guid           UUID             NOT NULL,
    col_binary         BYTEA,
    col_enum_int       INTEGER          NOT NULL,
    col_enum_str       VARCHAR(50)      NOT NULL
)";

    private string CreateSqlServerSql() => @"
IF NOT EXISTS (
    SELECT * FROM sys.objects
    WHERE object_id = OBJECT_ID(N'[dbo].[type_hydration]') AND type = N'U')
CREATE TABLE [dbo].[type_hydration] (
    [id]                 BIGINT           NOT NULL PRIMARY KEY,
    [col_string]         NVARCHAR(500)    NOT NULL,
    [col_string_null]    NVARCHAR(500),
    [col_short]          SMALLINT         NOT NULL,
    [col_int]            INT              NOT NULL,
    [col_int_null]       INT,
    [col_long]           BIGINT           NOT NULL,
    [col_float]          REAL             NOT NULL,
    [col_double]         FLOAT            NOT NULL,
    [col_decimal]        DECIMAL(18,8)    NOT NULL,
    [col_bool]           BIT              NOT NULL,
    [col_bool_null]      BIT,
    [col_datetime]       DATETIME2        NOT NULL,
    [col_datetimeoffset] DATETIMEOFFSET   NOT NULL,
    [col_guid]           UNIQUEIDENTIFIER NOT NULL,
    [col_binary]         VARBINARY(256),
    [col_enum_int]       INT              NOT NULL,
    [col_enum_str]       NVARCHAR(50)     NOT NULL
)";

    private string CreateMySqlSql()
    {
        var qp = _context.QuotePrefix;
        var qs = _context.QuoteSuffix;
        return string.Format(@"
CREATE TABLE IF NOT EXISTS {0}type_hydration{1} (
    {0}id{1}                 BIGINT        NOT NULL PRIMARY KEY,
    {0}col_string{1}         VARCHAR(500)  NOT NULL,
    {0}col_string_null{1}    VARCHAR(500),
    {0}col_short{1}          SMALLINT      NOT NULL,
    {0}col_int{1}            INT           NOT NULL,
    {0}col_int_null{1}       INT,
    {0}col_long{1}           BIGINT        NOT NULL,
    {0}col_float{1}          FLOAT         NOT NULL,
    {0}col_double{1}         DOUBLE        NOT NULL,
    {0}col_decimal{1}        DECIMAL(18,8) NOT NULL,
    {0}col_bool{1}           BOOLEAN       NOT NULL,
    {0}col_bool_null{1}      BOOLEAN,
    {0}col_datetime{1}       DATETIME(6)   NOT NULL,
    {0}col_datetimeoffset{1} DATETIME(6)   NOT NULL,
    {0}col_guid{1}           CHAR(36)      NOT NULL,
    {0}col_binary{1}         VARBINARY(256),
    {0}col_enum_int{1}       INT           NOT NULL,
    {0}col_enum_str{1}       VARCHAR(50)   NOT NULL
)", qp, qs);
    }

    private string CreateDuckDbSql()
    {
        var qp = _context.QuotePrefix;
        var qs = _context.QuoteSuffix;
        return string.Format(@"
CREATE TABLE IF NOT EXISTS {0}type_hydration{1} (
    {0}id{1}                 BIGINT           NOT NULL PRIMARY KEY,
    {0}col_string{1}         VARCHAR(500)     NOT NULL,
    {0}col_string_null{1}    VARCHAR(500),
    {0}col_short{1}          SMALLINT         NOT NULL,
    {0}col_int{1}            INTEGER          NOT NULL,
    {0}col_int_null{1}       INTEGER,
    {0}col_long{1}           BIGINT           NOT NULL,
    {0}col_float{1}          REAL             NOT NULL,
    {0}col_double{1}         DOUBLE           NOT NULL,
    {0}col_decimal{1}        DECIMAL(18,8)    NOT NULL,
    {0}col_bool{1}           BOOLEAN          NOT NULL,
    {0}col_bool_null{1}      BOOLEAN,
    {0}col_datetime{1}       TIMESTAMP        NOT NULL,
    {0}col_datetimeoffset{1} TIMESTAMPTZ      NOT NULL,
    {0}col_guid{1}           UUID             NOT NULL,
    {0}col_binary{1}         BLOB,
    {0}col_enum_int{1}       INTEGER          NOT NULL,
    {0}col_enum_str{1}       VARCHAR(50)      NOT NULL
)", qp, qs);
    }

    private string CreateSnowflakeSql()
    {
        // Snowflake: FLOAT4 is declared but stored as 64-bit internally.
        // TIMESTAMP_NTZ for both DateTime and DateTimeOffset (offset discarded/UTC only).
        // Guid: VARCHAR(36). Binary: VARBINARY.
        var w = (string name) => _context.WrapObjectName(name);
        return $@"
CREATE TABLE IF NOT EXISTS {w("type_hydration")} (
    {w("id")}                 BIGINT        NOT NULL PRIMARY KEY,
    {w("col_string")}         VARCHAR(500)  NOT NULL,
    {w("col_string_null")}    VARCHAR(500),
    {w("col_short")}          SMALLINT      NOT NULL,
    {w("col_int")}            INTEGER       NOT NULL,
    {w("col_int_null")}       INTEGER,
    {w("col_long")}           BIGINT        NOT NULL,
    {w("col_float")}          FLOAT4        NOT NULL,
    {w("col_double")}         DOUBLE        NOT NULL,
    {w("col_decimal")}        NUMBER(18,8)  NOT NULL,
    {w("col_bool")}           BOOLEAN       NOT NULL,
    {w("col_bool_null")}      BOOLEAN,
    {w("col_datetime")}       TIMESTAMP_NTZ NOT NULL,
    {w("col_datetimeoffset")} TIMESTAMP_NTZ NOT NULL,
    {w("col_guid")}           VARCHAR(36)   NOT NULL,
    {w("col_binary")}         VARBINARY,
    {w("col_enum_int")}       INTEGER       NOT NULL,
    {w("col_enum_str")}       VARCHAR(50)   NOT NULL
)";
    }

    private string CreateOracleSql()
    {
        // Oracle:
        //   - BINARY_FLOAT  = 32-bit IEEE 754 single-precision (matches C# float)
        //   - BINARY_DOUBLE = 64-bit IEEE 754 double-precision (matches C# double)
        //   - NUMBER(5/10/19) for short/int/long; returned as decimal, coerced by Convert.ChangeType
        //   - Empty string coerced to NULL on VARCHAR2/NVARCHAR2 columns
        //   - No IF NOT EXISTS → PL/SQL conditional block
        var qp = _context.QuotePrefix;
        var qs = _context.QuoteSuffix;
        return string.Format(@"DECLARE
    table_exists NUMBER;
BEGIN
    SELECT COUNT(*) INTO table_exists FROM user_tables WHERE table_name = 'TYPE_HYDRATION';
    IF table_exists = 0 THEN
        EXECUTE IMMEDIATE '
            CREATE TABLE {0}type_hydration{1} (
                {0}id{1}                 NUMBER(19)               NOT NULL,
                {0}col_string{1}         NVARCHAR2(500),
                {0}col_string_null{1}    NVARCHAR2(500),
                {0}col_short{1}          NUMBER(5)                NOT NULL,
                {0}col_int{1}            NUMBER(10)               NOT NULL,
                {0}col_int_null{1}       NUMBER(10),
                {0}col_long{1}           NUMBER(19)               NOT NULL,
                {0}col_float{1}          BINARY_FLOAT             NOT NULL,
                {0}col_double{1}         BINARY_DOUBLE            NOT NULL,
                {0}col_decimal{1}        NUMBER(18,8)             NOT NULL,
                {0}col_bool{1}           NUMBER(1)                NOT NULL,
                {0}col_bool_null{1}      NUMBER(1),
                {0}col_datetime{1}       TIMESTAMP                NOT NULL,
                {0}col_datetimeoffset{1} TIMESTAMP WITH TIME ZONE NOT NULL,
                {0}col_guid{1}           VARCHAR2(36)             NOT NULL,
                {0}col_binary{1}         RAW(256),
                {0}col_enum_int{1}       NUMBER(10)               NOT NULL,
                {0}col_enum_str{1}       VARCHAR2(50)             NOT NULL,
                CONSTRAINT pk_type_hydration PRIMARY KEY ({0}id{1})
            )';
    END IF;
END;", qp, qs);
    }

    private string CreateFirebirdSql()
    {
        // Firebird 3.x:
        //   - FLOAT = 32-bit IEEE 754 single-precision
        //   - DOUBLE PRECISION = 64-bit IEEE 754
        //   - SMALLINT for bool (0/1) and for short
        //   - No TIMESTAMP WITH TIME ZONE → plain TIMESTAMP (UTC stored)
        //   - CHAR(36) for guid
        //   - No IF NOT EXISTS → caller catches "already exists"
        var w = (string name) => _context.WrapObjectName(name);
        return $@"CREATE TABLE {w("type_hydration")} (
    {w("id")}                 BIGINT          NOT NULL PRIMARY KEY,
    {w("col_string")}         VARCHAR(500)    NOT NULL,
    {w("col_string_null")}    VARCHAR(500),
    {w("col_short")}          SMALLINT        NOT NULL,
    {w("col_int")}            INTEGER         NOT NULL,
    {w("col_int_null")}       INTEGER,
    {w("col_long")}           BIGINT          NOT NULL,
    {w("col_float")}          FLOAT           NOT NULL,
    {w("col_double")}         DOUBLE PRECISION NOT NULL,
    {w("col_decimal")}        DECIMAL(18,8)   NOT NULL,
    {w("col_bool")}           SMALLINT        NOT NULL,
    {w("col_bool_null")}      SMALLINT,
    {w("col_datetime")}       TIMESTAMP       NOT NULL,
    {w("col_datetimeoffset")} TIMESTAMP       NOT NULL,
    {w("col_guid")}           CHAR(36)        NOT NULL,
    {w("col_binary")}         BLOB SUB_TYPE 0,
    {w("col_enum_int")}       INTEGER         NOT NULL,
    {w("col_enum_str")}       VARCHAR(50)     NOT NULL
)";
    }

    private async Task CreateFirebirdTableAsync()
    {
        await using var container = _context.CreateSqlContainer(CreateFirebirdSql());
        try
        {
            await container.ExecuteNonQueryAsync();
        }
        catch (Exception ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            // Table already present from a prior test run; swallow
        }
    }
}
