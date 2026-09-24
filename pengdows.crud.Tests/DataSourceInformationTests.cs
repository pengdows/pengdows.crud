#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public static class DataSourceTestData
{
    public static IEnumerable<object[]> AllDatabases()
    {
        foreach (SupportedDatabase db in Enum.GetValues(typeof(SupportedDatabase)))
        {
            if (db == SupportedDatabase.Unknown)
            {
                continue;
            }

            yield return new object[] { db };
        }
    }

    public static (DataTable schema, Dictionary<string, object> scalars) BuildFixture(SupportedDatabase db)
    {
        var productName = db switch
        {
            SupportedDatabase.SqlServer => "SQL Server",
            SupportedDatabase.MySql => "MySQL",
            SupportedDatabase.AuroraMySql => "MySQL",
            SupportedDatabase.SingleStore => "MySQL",
            SupportedDatabase.MariaDb => "MariaDB",
            SupportedDatabase.PostgreSql => "PostgreSQL",
            SupportedDatabase.AuroraPostgreSql => "PostgreSQL",
            SupportedDatabase.CockroachDb => "CockroachDB",
            SupportedDatabase.YugabyteDb => "YugabyteDB",
            SupportedDatabase.TiDb => "TiDB",
            SupportedDatabase.Sqlite => "SQLite",
            SupportedDatabase.Firebird => "Firebird",
            SupportedDatabase.Oracle => "Oracle Database",
            SupportedDatabase.Snowflake => "Snowflake",
            SupportedDatabase.SybaseASE => "Adaptive Server Enterprise",
            // CONFIRMED live: GetSchema("DataSourceInformation").DataSourceProductName returns
            // "MS Jet" for a real .accdb — see AccessDialect.cs's file-level AI SUMMARY.
            // SchemaProductTokens matches on "ms jet", not "access", so the generic db.ToString()
            // fallback below ("Access") would not resolve detection to AccessDialect here.
            SupportedDatabase.Access => "MS Jet",
            _ => db.ToString()
        };

        var markerFormat = db switch
        {
            SupportedDatabase.PostgreSql or SupportedDatabase.AuroraPostgreSql
                or SupportedDatabase.CockroachDb or SupportedDatabase.YugabyteDb => "@{0}",
            SupportedDatabase.Oracle or SupportedDatabase.Snowflake => ":{0}",
            SupportedDatabase.DuckDB => "$" + "{0}",
            _ => "@{0}"
        };

        var schema = DataSourceInformation.BuildEmptySchema(
            productName,
            "1.2.3",
            db == SupportedDatabase.Sqlite ? "@p[0-9]+" : "@[0-9]+",
            markerFormat,
            64,
            @"@\\w+",
            @"[@:]\w+",
            db != SupportedDatabase.Sqlite
        );

        var factory = new fakeDbFactory(db.ToString());

        SqlDialect dialect = db switch
        {
            SupportedDatabase.SqlServer => new SqlServerDialect(factory, NullLogger.Instance),
            SupportedDatabase.MySql => new MySqlDialect(factory, NullLogger.Instance),
            SupportedDatabase.AuroraMySql => new MySqlDialect(factory, NullLogger.Instance, SupportedDatabase.AuroraMySql),
            SupportedDatabase.SingleStore => new MySqlDialect(factory, NullLogger.Instance, SupportedDatabase.SingleStore),
            SupportedDatabase.MariaDb => new MariaDbDialect(factory, NullLogger.Instance),
            SupportedDatabase.TiDb => new TiDbDialect(factory, NullLogger.Instance),
            SupportedDatabase.PostgreSql => new PostgreSqlDialect(factory, NullLogger.Instance),
            SupportedDatabase.AuroraPostgreSql => new PostgreSqlDialect(factory, NullLogger.Instance, SupportedDatabase.AuroraPostgreSql),
            SupportedDatabase.CockroachDb => new CockroachDbDialect(factory, NullLogger.Instance),
            SupportedDatabase.YugabyteDb => new YugabyteDbDialect(factory, NullLogger.Instance),
            SupportedDatabase.Sqlite => new SqliteDialect(factory, NullLogger.Instance),
            SupportedDatabase.Firebird => new FirebirdDialect(factory, NullLogger.Instance),
            SupportedDatabase.Oracle => new OracleDialect(factory, NullLogger.Instance),
            SupportedDatabase.DuckDB => new DuckDbDialect(factory, NullLogger.Instance),
            SupportedDatabase.Snowflake => new SnowflakeDialect(factory, NullLogger.Instance),
            SupportedDatabase.FlatFile => new FlatFileDialect(factory, NullLogger.Instance),
            SupportedDatabase.SybaseASE => new SybaseDialect(factory, NullLogger.Instance),
            SupportedDatabase.Db2 => new Db2Dialect(factory, NullLogger.Instance),
            SupportedDatabase.Informix => new InformixDialect(factory, NullLogger.Instance),
            SupportedDatabase.SapHana => new HanaDialect(factory, NullLogger.Instance),
            SupportedDatabase.InterBase => new InterBaseDialect(factory, NullLogger.Instance),
            SupportedDatabase.Spanner => new SpannerDialect(factory, NullLogger.Instance),
            SupportedDatabase.Access => new AccessDialect(factory, NullLogger.Instance),
            _ => new Sql92Dialect(factory, NullLogger.Instance)
        };

        // Mirrors SqlDialect.GetDatabaseVersionAsync's own fallback: when a dialect doesn't
        // override GetVersionQuery() (e.g. FlatFile, which has no dedicated dialect yet and falls
        // back to Sql92Dialect's empty default), the real code queries "SELECT version()" instead —
        // the canned scalar must be registered under whichever query actually gets executed.
        var rawVersionQuery = dialect.GetVersionQuery();
        var versionSql = string.IsNullOrWhiteSpace(rawVersionQuery) ? "SELECT version()" : rawVersionQuery;

        var versionString = db switch
        {
            SupportedDatabase.PostgreSql or SupportedDatabase.AuroraPostgreSql => "PostgreSQL 15.0",
            _ => $"{db} v1.2.3"
        };

        var scalars = new Dictionary<string, object>
        {
            [versionSql] = versionString
        };

        // Prevent the YugabyteDB pg_settings probe from false-positive matching in fakeDb.
        // The probe runs for any PostgreSQL-family base detection (detected == PostgreSql or Unknown).
        // Without this entry, the fakeDb fallback scalar ("PostgreSQL 15.0") satisfies
        // `is string { Length: > 0 }` and incorrectly identifies these databases as YugabyteDB.
        if (db is SupportedDatabase.PostgreSql or SupportedDatabase.AuroraPostgreSql)
        {
            const string pgSettingsProbe =
                "SELECT name FROM pg_settings WHERE name = 'yb_enable_optimizer_statistics' LIMIT 1";
            scalars[pgSettingsProbe] = DBNull.Value;

            // Same false-positive shape as the YugabyteDB probe above, for
            // DatabaseDetectionService's Spanner-vs-real-PostgreSQL discriminator: without this
            // entry the fakeDb fallback scalar answers "SHOW SPANNER.OPTIMIZER_VERSION" with a
            // non-null string and DetectFlavorWithDetail misreads plain PostgreSQL/Aurora as
            // Spanner. Verified against a real Spanner Omni + PGAdapter instance that ordinary
            // PostgreSQL has no such setting (real Spanner returns a string; real PostgreSQL
            // would error, which the fake models as DBNull rather than a thrown exception).
            const string spannerOptimizerVersionProbe = "SHOW SPANNER.OPTIMIZER_VERSION";
            scalars[spannerOptimizerVersionProbe] = DBNull.Value;
        }

        return (schema, scalars);
    }
}

public class DataSourceInformationTests
{
    [Theory]
    [MemberData(nameof(DataSourceTestData.AllDatabases), MemberType = typeof(DataSourceTestData))]
    public void DataSourceInformation_Should_Configure_Each_Database(SupportedDatabase db)
    {
        var (schema, scalars) = DataSourceTestData.BuildFixture(db);
        var factory = new fakeDbFactory(db.ToString());
        // Arrange
        var x = factory.CreateConnection();
        x.ConnectionString = $"Data Source=test;Data Source=test;EmulatedProduct={db}";
        var conn = new FakeTrackedConnection(x, schema, scalars);

        // Act
        var info = DataSourceInformation.Create(conn, factory, NullLoggerFactory.Instance);

        // Assert: product detection
        //Assert.Equal(db, info.Product);

        // Assert: version parsing
        //Assert.Contains("v1.2.3", info.DatabaseProductVersion);

        // Assert: parameter marker
        var expectedMarker = db switch
        {
            SupportedDatabase.PostgreSql or SupportedDatabase.AuroraPostgreSql
                or SupportedDatabase.CockroachDb or SupportedDatabase.YugabyteDb => "@",
            SupportedDatabase.Oracle or SupportedDatabase.Snowflake => ":",
            SupportedDatabase.DuckDB => "$",
            // FlatFile's own SQL grammar supports only positional ? parameters (see
            // FlatFileDialect.SupportsNamedParameters) — verified against its README, not assumed.
            SupportedDatabase.FlatFile => "?",
            // Informix's ADO.NET driver has no named-parameter support at all - positional "?"
            // only (see InformixDialect.SupportsNamedParameters). SAP HANA likewise (confirmed
            // live via DataSourceInformation.ParameterMarkerFormat == "?"). Access's OLE DB
            // provider is positional-only too (confirmed live: ParameterMarkerFormat == "?").
            SupportedDatabase.Informix or SupportedDatabase.SapHana or SupportedDatabase.Access => "?",
            _ => "@"
        };
        Assert.Equal(expectedMarker, info.ParameterMarker);

        // Assert: major version parsing
        var expectedMajor = (db == SupportedDatabase.PostgreSql || db == SupportedDatabase.AuroraPostgreSql) ? 15 : 1;
        Assert.Equal(expectedMajor, info.ParsedVersion?.Major);

        // Assert: merge support
        var canMerge = (db == SupportedDatabase.SqlServer && info.ParsedVersion?.Major >= 10)
                       || db == SupportedDatabase.Oracle
                       || db == SupportedDatabase.Snowflake
                       || (db == SupportedDatabase.Firebird && info.ParsedVersion?.Major >= 2)
                       || ((db == SupportedDatabase.PostgreSql || db == SupportedDatabase.AuroraPostgreSql) && info.ParsedVersion?.Major > 14)
                       || (db == SupportedDatabase.YugabyteDb && info.ParsedVersion?.Major > 14)
                       || db == SupportedDatabase.SybaseASE
                       || db == SupportedDatabase.Db2
                       || db == SupportedDatabase.SapHana;
        Assert.Equal(canMerge, info.SupportsMerge);
        Assert.NotEqual(!canMerge, info.SupportsMerge);

        // Assert: insert-on-conflict support
        var canConflict = new[]
        {
            SupportedDatabase.PostgreSql,
            SupportedDatabase.AuroraPostgreSql,
            SupportedDatabase.CockroachDb,
            SupportedDatabase.YugabyteDb,
            // Verified against a real Spanner Omni + PGAdapter instance: `INSERT ... ON CONFLICT
            // (id) DO UPDATE`/`DO NOTHING` both execute correctly against Spanner's PostgreSQL
            // interface, unlike MERGE (see SpannerDialect.SupportsMerge).
            SupportedDatabase.Spanner,
            SupportedDatabase.Sqlite,
            SupportedDatabase.DuckDB
        }.Contains(db);
        Assert.Equal(canConflict, info.SupportsInsertOnConflict);

        // SingleStore supports ON DUPLICATE KEY UPDATE as part of its MySQL wire-compatible SQL
        // surface (documented SingleStore feature, not just an assumption from delegating to
        // MySqlDialect).
        var canOnDuplicateKey = new[]
        {
            SupportedDatabase.MySql,
            SupportedDatabase.AuroraMySql,
            SupportedDatabase.MariaDb,
            SupportedDatabase.TiDb,
            SupportedDatabase.SingleStore
        }.Contains(db);
        Assert.Equal(canOnDuplicateKey, info.SupportsOnDuplicateKey);

        // Assert: proc wrap style
        var expectedWrap = db switch
        {
            SupportedDatabase.SqlServer or SupportedDatabase.SybaseASE => ProcWrappingStyle.Exec,
            SupportedDatabase.Oracle => ProcWrappingStyle.Oracle,
            SupportedDatabase.MySql or SupportedDatabase.AuroraMySql
                or SupportedDatabase.MariaDb or SupportedDatabase.Snowflake
                or SupportedDatabase.SingleStore or SupportedDatabase.Db2
                or SupportedDatabase.SapHana => ProcWrappingStyle.Call,
            SupportedDatabase.TiDb => ProcWrappingStyle.None,
            SupportedDatabase.PostgreSql or SupportedDatabase.AuroraPostgreSql
                or SupportedDatabase.CockroachDb or SupportedDatabase.YugabyteDb => ProcWrappingStyle.PostgreSQL,
            SupportedDatabase.Firebird or SupportedDatabase.InterBase => ProcWrappingStyle.ExecuteProcedure,
            _ => ProcWrappingStyle.None
        };
        var expectedRequiresStoredProcParameterNameMatch = db switch
        {
            // FlatFile and Informix both have no named parameters at all (positional ? only) and
            // no stored-procedure support (ProcWrappingStyle.None), so there is nothing to
            // name-match against. SAP HANA has no named parameters either, even though it DOES
            // support stored procedures (ProcWrappingStyle.Call) - same "nothing to name-match"
            // conclusion for a different reason. Access matches FlatFile/Informix's reasoning
            // exactly: positional ? only, no ADO.NET-invocable stored procedures at all.
            SupportedDatabase.FlatFile or SupportedDatabase.Informix or SupportedDatabase.SapHana
                or SupportedDatabase.Access => false,
            SupportedDatabase.Firebird or SupportedDatabase.Sqlite or SupportedDatabase.SqlServer
                or SupportedDatabase.MySql or SupportedDatabase.AuroraMySql
                or SupportedDatabase.MariaDb or SupportedDatabase.DuckDB
                or SupportedDatabase.TiDb or SupportedDatabase.Snowflake
                or SupportedDatabase.SingleStore or SupportedDatabase.SybaseASE
                or SupportedDatabase.Db2 or SupportedDatabase.InterBase => false,
            SupportedDatabase.PostgreSql or SupportedDatabase.AuroraPostgreSql
                or SupportedDatabase.CockroachDb or SupportedDatabase.YugabyteDb
                or SupportedDatabase.Oracle => true,
            _ => true
        };

        Assert.Equal(expectedWrap, info.ProcWrappingStyle);

        // Assert: named parameters flags
        // FlatFile's own SQL grammar supports only positional ? parameters (see
        // FlatFileDialect.SupportsNamedParameters) — verified against its README, not assumed.
        // Informix's ADO.NET driver likewise has no named-parameter support at all (see
        // InformixDialect.SupportsNamedParameters), nor does SAP HANA's (confirmed live via
        // DataSourceInformation.ParameterMarkerFormat == "?"). Access's OLE DB provider is
        // positional-only too (confirmed live: ParameterMarkerFormat == "?").
        var expectedSupportsNamedParameters = db != SupportedDatabase.FlatFile
            && db != SupportedDatabase.Informix && db != SupportedDatabase.SapHana
            && db != SupportedDatabase.Access;
        Assert.Equal(expectedSupportsNamedParameters, info.SupportsNamedParameters);
        Assert.Equal(expectedRequiresStoredProcParameterNameMatch, info.RequiresStoredProcParameterNameMatch);

        // Assert: output parameter limits
        var expectedOutputParams = db switch
        {
            SupportedDatabase.SqlServer => 1024,
            SupportedDatabase.MySql or SupportedDatabase.AuroraMySql
                or SupportedDatabase.MariaDb or SupportedDatabase.TiDb
                or SupportedDatabase.Snowflake or SupportedDatabase.SingleStore => 65535,
            SupportedDatabase.PostgreSql or SupportedDatabase.AuroraPostgreSql
                or SupportedDatabase.CockroachDb or SupportedDatabase.YugabyteDb
                or SupportedDatabase.Spanner => 100,
            SupportedDatabase.Oracle => 1024,
            SupportedDatabase.Sqlite => 0,
            SupportedDatabase.Firebird => 1499,
            _ => 0
        };
        Assert.Equal(expectedOutputParams, info.MaxOutputParameters);
    }

    [Theory]
    [MemberData(nameof(DataSourceTestData.AllDatabases), MemberType = typeof(DataSourceTestData))]
    public void GetDatabaseVersion_Returns_Version(SupportedDatabase db)
    {
        var (schema, scalars) = DataSourceTestData.BuildFixture(db);
        var factory = new fakeDbFactory(db.ToString());
        var connection = factory.CreateConnection();
        connection.ConnectionString = $"Data Source=test;EmulatedProduct={db}";
        var tracked = new FakeTrackedConnection(connection, schema, scalars);

        var dialect = SqlDialectFactory.CreateDialect(tracked, factory, NullLoggerFactory.Instance);
        var info = new DataSourceInformation(dialect);

        var result = dialect.GetDatabaseVersion(tracked);

        // FlatFile, InterBase, and Access have no version()-style SQL function at all — their
        // dialects read ADO.NET's standard ServerVersion property directly instead of executing a
        // canned scalar query (see FlatFileDialect.GetDatabaseVersionAsync; InterBaseDialect.cs's
        // ServerVersion-based override — confirmed live that neither Firebird's rdb$get_context
        // nor a mon$-table equivalent exists in InterBase 15; AccessDialect.cs's identical
        // ServerVersion-based override — confirmed live that Jet SQL has no version()-style
        // function either). Every other dialect still executes a dialect-specific SQL version
        // query, matched against the canned scalar below.
        var expected = db is SupportedDatabase.FlatFile or SupportedDatabase.InterBase or SupportedDatabase.Access
            ? tracked.ServerVersion
            : scalars.Values.First().ToString();
        Assert.Equal(expected, result);
    }

    private static ITrackedConnection BuildSqliteConnectionMock()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var conn = (fakeDbConnection)factory.CreateConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";

        var row = new Dictionary<string, object?> { { "version", "3.0" } };
        // One result for IsSqliteAsync, one for GetVersionAsync, and one for IsSqliteSync
        conn.EnqueueReaderResult(new[] { row });
        conn.EnqueueReaderResult(new[] { row });
        conn.EnqueueReaderResult(new[] { row });

        conn.Open();
        return new TrackedConnection(conn);
    }

    [Fact]
    public void GetSchema_UsesEmbeddedForSqlite()
    {
        var tracked = BuildSqliteConnectionMock();
        var info = DataSourceInformation.Create(tracked, new fakeDbFactory(SupportedDatabase.Sqlite),
            NullLoggerFactory.Instance);

        var schema = info.GetSchema(tracked);
        Assert.Equal("SQLite", schema.Rows[0].Field<string>("DataSourceProductName"));
        Assert.Equal("@{0}", schema.Rows[0].Field<string>("ParameterMarkerFormat"));
    }

    [Fact]
    public void GetSchema_NonSqlite_UsesConnectionSchema()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var conn = factory.CreateConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.SqlServer}";
        using var tracked = new TrackedConnection(conn);
        var info = DataSourceInformation.Create(tracked, factory, NullLoggerFactory.Instance);

        var schema = info.GetSchema(tracked);
        Assert.Contains("SQL Server", schema.Rows[0].Field<string>("DataSourceProductName"));
        Assert.Equal("{0}", schema.Rows[0].Field<string>("ParameterMarkerFormat"));
    }
}
