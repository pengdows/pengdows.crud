using pengdows.crud.enums;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// Creates the <c>round_trip_entity</c> table used by full row round-trip
/// fidelity tests. Each provider gets column types suited to its dialect.
///
/// Per-provider type decisions:
/// <list type="bullet">
///   <item>SQLite: decimal → REAL (IEEE 754); datetimeoffset → TEXT (ISO-8601);
///     guid → TEXT; binary → BLOB.</item>
///   <item>PostgreSQL / CockroachDB / DuckDB: datetimeoffset → TIMESTAMPTZ;
///     guid → UUID; binary → BYTEA / BYTES / BLOB.</item>
///   <item>SQL Server: datetimeoffset → DATETIMEOFFSET; guid → UNIQUEIDENTIFIER;
///     binary → VARBINARY(256).</item>
///   <item>MySQL / MariaDB: datetimeoffset → DATETIME(6) (UTC normalised, offset
///     dropped); guid → CHAR(36); text_unicode → utf8mb4 charset;
///     binary → VARBINARY(256).</item>
///   <item>Oracle: datetimeoffset → TIMESTAMP WITH TIME ZONE;
///     guid → VARCHAR2(36); binary → RAW(256).</item>
///   <item>Firebird: datetimeoffset → TIMESTAMP (UTC normalised, offset dropped);
///     guid → CHAR(36); binary → VARBINARY(256).</item>
/// </list>
/// </summary>
public class RoundTripTableCreator
{
    private readonly IDatabaseContext _context;

    public RoundTripTableCreator(IDatabaseContext context)
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

        var sql = _context.Product switch
        {
            SupportedDatabase.Sqlite => CreateSqliteSql(),
            SupportedDatabase.PostgreSql => CreatePostgreSqlSql(),
            SupportedDatabase.CockroachDb => CreateCockroachDbSql(),
            SupportedDatabase.SqlServer => CreateSqlServerSql(),
            SupportedDatabase.MySql => CreateMySqlSql(),
            SupportedDatabase.MariaDb => CreateMySqlSql(),
            SupportedDatabase.DuckDB => CreateDuckDbSql(),
            SupportedDatabase.Oracle => CreateOracleSql(),
            _ => throw new NotSupportedException(
                $"Database {_context.Product} is not supported by RoundTripTableCreator")
        };

        await using var container = _context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
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
            // Table already present; swallow
        }
    }

    // -------------------------------------------------------------------------
    // Per-provider DDL
    // -------------------------------------------------------------------------

    private string CreateSqliteSql() => @"
CREATE TABLE IF NOT EXISTS round_trip_entity (
    id                   INTEGER      NOT NULL PRIMARY KEY,
    text_value           TEXT         NOT NULL,
    text_unicode         TEXT         NOT NULL,
    text_nullable        TEXT,
    int_value            INTEGER      NOT NULL,
    long_value           INTEGER      NOT NULL,
    decimal_value        REAL         NOT NULL,
    bool_value           INTEGER      NOT NULL,
    datetimeoffset_value TEXT         NOT NULL,
    guid_value           TEXT         NOT NULL,
    binary_value         BLOB         NOT NULL
)";

    // NOTE: decimal_value uses REAL in SQLite because SQLite has no fixed-
    // precision decimal storage. Tests must assert within ~1e-7 tolerance.

    private string CreatePostgreSqlSql() => @"
CREATE TABLE IF NOT EXISTS round_trip_entity (
    id                   BIGINT        NOT NULL PRIMARY KEY,
    text_value           VARCHAR(500)  NOT NULL,
    text_unicode         VARCHAR(500)  NOT NULL,
    text_nullable        VARCHAR(500),
    int_value            INTEGER       NOT NULL,
    long_value           BIGINT        NOT NULL,
    decimal_value        DECIMAL(18,8) NOT NULL,
    bool_value           BOOLEAN       NOT NULL,
    datetimeoffset_value TIMESTAMPTZ   NOT NULL,
    guid_value           UUID          NOT NULL,
    binary_value         BYTEA         NOT NULL
)";

    private string CreateCockroachDbSql() => @"
CREATE TABLE IF NOT EXISTS round_trip_entity (
    id                   BIGINT        NOT NULL PRIMARY KEY,
    text_value           VARCHAR(500)  NOT NULL,
    text_unicode         VARCHAR(500)  NOT NULL,
    text_nullable        VARCHAR(500),
    int_value            INTEGER       NOT NULL,
    long_value           BIGINT        NOT NULL,
    decimal_value        DECIMAL(18,8) NOT NULL,
    bool_value           BOOL          NOT NULL,
    datetimeoffset_value TIMESTAMPTZ   NOT NULL,
    guid_value           UUID          NOT NULL,
    binary_value         BYTES         NOT NULL
)";

    private string CreateSqlServerSql() => @"
IF NOT EXISTS (
    SELECT * FROM sys.objects
    WHERE object_id = OBJECT_ID(N'[dbo].[round_trip_entity]') AND type = N'U')
CREATE TABLE [dbo].[round_trip_entity] (
    [id]                   BIGINT           NOT NULL PRIMARY KEY,
    [text_value]           NVARCHAR(500)    NOT NULL,
    [text_unicode]         NVARCHAR(500)    NOT NULL,
    [text_nullable]        NVARCHAR(500)    NULL,
    [int_value]            INT              NOT NULL,
    [long_value]           BIGINT           NOT NULL,
    [decimal_value]        DECIMAL(18,8)    NOT NULL,
    [bool_value]           BIT              NOT NULL,
    [datetimeoffset_value] DATETIMEOFFSET   NOT NULL,
    [guid_value]           UNIQUEIDENTIFIER NOT NULL,
    [binary_value]         VARBINARY(256)   NOT NULL
)";

    private string CreateMySqlSql()
    {
        // text_unicode uses utf8mb4 to support 4-byte codepoints (emoji, etc.)
        // datetimeoffset_value: no tz-aware type; UTC DATETIME(6) is used.
        //   The library normalises DateTimeOffset to UTC before storage.
        // guid_value: stored as 36-char string.
        var qp = _context.QuotePrefix;
        var qs = _context.QuoteSuffix;
        return string.Format(@"
CREATE TABLE IF NOT EXISTS {0}round_trip_entity{1} (
    {0}id{1}                   BIGINT                   NOT NULL PRIMARY KEY,
    {0}text_value{1}           VARCHAR(500)             NOT NULL,
    {0}text_unicode{1}         VARCHAR(500) CHARACTER SET utf8mb4 NOT NULL,
    {0}text_nullable{1}        VARCHAR(500),
    {0}int_value{1}            INT                      NOT NULL,
    {0}long_value{1}           BIGINT                   NOT NULL,
    {0}decimal_value{1}        DECIMAL(18,8)            NOT NULL,
    {0}bool_value{1}           BOOLEAN                  NOT NULL,
    {0}datetimeoffset_value{1} DATETIME(6)              NOT NULL,
    {0}guid_value{1}           CHAR(36)                 NOT NULL,
    {0}binary_value{1}         VARBINARY(256)           NOT NULL
)", qp, qs);
    }

    private string CreateDuckDbSql()
    {
        var qp = _context.QuotePrefix;
        var qs = _context.QuoteSuffix;
        return string.Format(@"
CREATE TABLE IF NOT EXISTS {0}round_trip_entity{1} (
    {0}id{1}                   BIGINT        NOT NULL PRIMARY KEY,
    {0}text_value{1}           VARCHAR(500)  NOT NULL,
    {0}text_unicode{1}         VARCHAR(500)  NOT NULL,
    {0}text_nullable{1}        VARCHAR(500),
    {0}int_value{1}            INTEGER       NOT NULL,
    {0}long_value{1}           BIGINT        NOT NULL,
    {0}decimal_value{1}        DECIMAL(18,8) NOT NULL,
    {0}bool_value{1}           BOOLEAN       NOT NULL,
    {0}datetimeoffset_value{1} TIMESTAMPTZ   NOT NULL,
    {0}guid_value{1}           UUID          NOT NULL,
    {0}binary_value{1}         BLOB          NOT NULL
)", qp, qs);
    }

    private string CreateOracleSql()
    {
        // guid_value: VARCHAR2(36) — Oracle RAW(16) requires binary coercion;
        //   VARCHAR2(36) is safer across driver versions.
        // binary_value: RAW(256) — sufficient for small test payloads.
        // datetimeoffset_value: TIMESTAMP WITH TIME ZONE — Oracle 9i+.
        // Oracle coerces '' to NULL for VARCHAR2 — tests must account for this.
        var qp = _context.QuotePrefix;
        var qs = _context.QuoteSuffix;
        return string.Format(@"
DECLARE
    table_exists NUMBER;
BEGIN
    SELECT COUNT(*) INTO table_exists FROM user_tables WHERE table_name = 'ROUND_TRIP_ENTITY';
    IF table_exists = 0 THEN
        EXECUTE IMMEDIATE '
            CREATE TABLE {0}round_trip_entity{1} (
                {0}id{1}                   NUMBER(19)               NOT NULL,
                {0}text_value{1}           NVARCHAR2(500),
                {0}text_unicode{1}         NVARCHAR2(500),
                {0}text_nullable{1}        NVARCHAR2(500),
                {0}int_value{1}            NUMBER(10)               NOT NULL,
                {0}long_value{1}           NUMBER(19)               NOT NULL,
                {0}decimal_value{1}        NUMBER(18,8)             NOT NULL,
                {0}bool_value{1}           NUMBER(1)                NOT NULL,
                {0}datetimeoffset_value{1} TIMESTAMP WITH TIME ZONE NOT NULL,
                {0}guid_value{1}           VARCHAR2(36)             NOT NULL,
                {0}binary_value{1}         RAW(256),
                CONSTRAINT pk_round_trip_entity PRIMARY KEY ({0}id{1})
            )';
    END IF;
END;", qp, qs);
    }

    private string CreateFirebirdSql()
    {
        // Firebird 3.x has no TIMESTAMP WITH TIME ZONE — plain TIMESTAMP (UTC stored).
        // guid_value: CHAR(36) — string representation.
        // Firebird does not support IF NOT EXISTS; the caller swallows "already exists".
        var t = _context.WrapObjectName("round_trip_entity");
        return $@"
CREATE TABLE {t} (
    {_context.WrapObjectName("id")}                   BIGINT         NOT NULL PRIMARY KEY,
    {_context.WrapObjectName("text_value")}           VARCHAR(500)   NOT NULL,
    {_context.WrapObjectName("text_unicode")}         VARCHAR(500)   NOT NULL,
    {_context.WrapObjectName("text_nullable")}        VARCHAR(500),
    {_context.WrapObjectName("int_value")}            INTEGER        NOT NULL,
    {_context.WrapObjectName("long_value")}           BIGINT         NOT NULL,
    {_context.WrapObjectName("decimal_value")}        DECIMAL(18,8)  NOT NULL,
    {_context.WrapObjectName("bool_value")}           SMALLINT       NOT NULL,
    {_context.WrapObjectName("datetimeoffset_value")} TIMESTAMP      NOT NULL,
    {_context.WrapObjectName("guid_value")}           CHAR(36)       NOT NULL,
    {_context.WrapObjectName("binary_value")}         VARBINARY(256) NOT NULL
)";
    }
}
