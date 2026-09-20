using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlServerDialectSettingsTests
{
    private sealed class UserOptionsCommand : DbCommand
    {
        private readonly DbConnection _connection;
        private readonly fakeDbDataReader _reader;

        public UserOptionsCommand(DbConnection connection, fakeDbDataReader reader)
        {
            _connection = connection;
            _reader = reader;
        }

        [AllowNull] public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }

        [AllowNull]
        protected override DbConnection DbConnection
        {
            get => _connection;
            set { }
        }

        protected override DbParameterCollection DbParameterCollection { get; } = new FakeParameterCollection();

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery()
        {
            return 0;
        }

        public override object ExecuteScalar()
        {
            return DBNull.Value;
        }

        public override void Prepare()
        {
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            return _reader;
        }

        protected override DbParameter CreateDbParameter()
        {
            return new fakeDbParameter();
        }
    }

    private sealed class UserOptionsConnection : fakeDbConnection
    {
        private readonly fakeDbDataReader _reader;

        public UserOptionsConnection(IEnumerable<Dictionary<string, object>> rows)
        {
            EmulatedProduct = SupportedDatabase.SqlServer;
            _reader = new fakeDbDataReader(rows);
        }

        protected override DbCommand CreateDbCommand()
        {
            return new UserOptionsCommand(this, _reader);
        }
    }

    private static ITrackedConnection BuildConnection(IEnumerable<Dictionary<string, object>> rows)
    {
        var inner = new UserOptionsConnection(rows)
        {
            ConnectionString = $"Data Source=:memory:;EmulatedProduct={SupportedDatabase.SqlServer}"
        };
        inner.Open();
        return new TrackedConnection(inner);
    }

    // Backs the compatibility-level probe (GetSqlServerSessionSettings's TryGetCompatibilityLevel):
    // returns a configurable scalar value for ANY command, modeling
    // "SELECT compatibility_level FROM sys.databases WHERE database_id = DB_ID()".
    private sealed class CompatibilityLevelCommand : DbCommand
    {
        private readonly DbConnection _connection;
        private readonly object _scalarResult;

        public CompatibilityLevelCommand(DbConnection connection, object scalarResult)
        {
            _connection = connection;
            _scalarResult = scalarResult;
        }

        [AllowNull] public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }

        [AllowNull]
        protected override DbConnection DbConnection
        {
            get => _connection;
            set { }
        }

        protected override DbParameterCollection DbParameterCollection { get; } = new FakeParameterCollection();

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }
        public override int ExecuteNonQuery() => 0;
        public override object ExecuteScalar() => _scalarResult;
        public override void Prepare() { }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            return new fakeDbDataReader(Array.Empty<Dictionary<string, object>>());
        }

        protected override DbParameter CreateDbParameter() => new fakeDbParameter();
    }

    private sealed class CompatibilityLevelConnection : fakeDbConnection
    {
        private readonly object _scalarResult;

        public CompatibilityLevelConnection(object scalarResult)
        {
            EmulatedProduct = SupportedDatabase.SqlServer;
            _scalarResult = scalarResult;
        }

        protected override DbCommand CreateDbCommand()
        {
            return new CompatibilityLevelCommand(this, _scalarResult);
        }
    }

    private static ITrackedConnection BuildCompatibilityLevelConnection(object scalarResult)
    {
        var inner = new CompatibilityLevelConnection(scalarResult)
        {
            ConnectionString = $"Data Source=:memory:;EmulatedProduct={SupportedDatabase.SqlServer}"
        };
        inner.Open();
        return new TrackedConnection(inner);
    }

    [Fact]
    public void QuotePrefix_IsDoubleQuotes()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var dialect = new SqlServerDialect(factory, NullLogger<SqlServerDialect>.Instance);
        Assert.Equal("\"", dialect.QuotePrefix);
        Assert.Equal("\"", dialect.QuoteSuffix);
    }

    [Fact]
    public async Task GetConnectionSessionSettings_OptimalSettings_StillEnforcesBaseline()
    {
        var rows = new[]
        {
            new Dictionary<string, object> { { "ANSI_NULLS", "SET" } },
            new Dictionary<string, object> { { "ANSI_PADDING", "SET" } },
            new Dictionary<string, object> { { "ANSI_WARNINGS", "SET" } },
            new Dictionary<string, object> { { "ARITHABORT", "SET" } },
            new Dictionary<string, object> { { "CONCAT_NULL_YIELDS_NULL", "SET" } },
            new Dictionary<string, object> { { "QUOTED_IDENTIFIER", "SET" } },
            new Dictionary<string, object> { { "NUMERIC_ROUNDABORT", "OFF" } }
        };
        await using var conn = BuildConnection(rows);
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var dialect = new SqlServerDialect(factory, NullLogger<SqlServerDialect>.Instance);
        await dialect.DetectDatabaseInfoAsync(conn);
        using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory);
        var settings = dialect.GetConnectionSessionSettings(ctx, false);

        // Even when all settings are already optimal, the full baseline is enforced
        // to protect against pooled connection drift
        Assert.Contains("SET QUOTED_IDENTIFIER ON", settings);
        Assert.Contains("SET ANSI_NULLS ON", settings);
        Assert.Contains("SET ARITHABORT ON", settings);
    }

    [Fact]
    public async Task GetConnectionSessionSettings_LowercaseKeys_StillEnforcesBaseline()
    {
        var rows = new[]
        {
            new Dictionary<string, object> { { "ansi_nulls", "SET" } },
            new Dictionary<string, object> { { "ansi_padding", "SET" } },
            new Dictionary<string, object> { { "ansi_warnings", "SET" } },
            new Dictionary<string, object> { { "arithabort", "SET" } },
            new Dictionary<string, object> { { "concat_null_yields_null", "SET" } },
            new Dictionary<string, object> { { "quoted_identifier", "SET" } },
            new Dictionary<string, object> { { "numeric_roundabort", "OFF" } }
        };
        await using var conn = BuildConnection(rows);
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var dialect = new SqlServerDialect(factory, NullLogger<SqlServerDialect>.Instance);
        await dialect.DetectDatabaseInfoAsync(conn);
        using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory);
        var settings = dialect.GetConnectionSessionSettings(ctx, false);

        // Even with lowercase keys, baseline is enforced
        Assert.Contains("SET QUOTED_IDENTIFIER ON", settings);
        Assert.Contains("SET ANSI_NULLS ON", settings);
    }

    [Fact]
    public async Task GetConnectionSessionSettings_QuotedIdentifierOff_BuildsSettingsScript()
    {
        var rows = new[]
        {
            new Dictionary<string, object> { { "ANSI_NULLS", "SET" } },
            new Dictionary<string, object> { { "ANSI_PADDING", "SET" } },
            new Dictionary<string, object> { { "ANSI_WARNINGS", "SET" } },
            new Dictionary<string, object> { { "ARITHABORT", "SET" } },
            new Dictionary<string, object> { { "CONCAT_NULL_YIELDS_NULL", "SET" } },
            new Dictionary<string, object> { { "QUOTED_IDENTIFIER", "OFF" } },
            new Dictionary<string, object> { { "NUMERIC_ROUNDABORT", "OFF" } }
        };
        await using var conn = BuildConnection(rows);
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var dialect = new SqlServerDialect(factory, NullLogger<SqlServerDialect>.Instance);
        await dialect.DetectDatabaseInfoAsync(conn);
        using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory);
        var settings = dialect.GetConnectionSessionSettings(ctx, false);
        Assert.Contains("SET QUOTED_IDENTIFIER ON", settings);
        Assert.DoesNotContain("NOCOUNT", settings);
    }

    // Live-verified (2026-09-20, real SQL Server 2017 and 2022 engines) that at compatibility
    // level >= 90, ANSI_WARNINGS ON implicitly promotes ARITHABORT to effectively ON, and the
    // driver's own login sequence + sp_reset_connection already guarantee every one of the
    // seven settings unconditionally — no explicit SET script is needed at all. See
    // SqlServerDialect.cs's SessionSettingsDef comment for the full investigation trail.
    [Fact]
    public async Task GetConnectionSessionSettings_ModernCompatibilityLevel_ReturnsNoSessionSettings()
    {
        await using var conn = BuildCompatibilityLevelConnection((byte)150);
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var dialect = new SqlServerDialect(factory, NullLogger<SqlServerDialect>.Instance);
        await dialect.DetectDatabaseInfoAsync(conn);
        using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory);
        var settings = dialect.GetConnectionSessionSettings(ctx, false);

        Assert.True(string.IsNullOrWhiteSpace(settings),
            $"Expected no session settings at compatibility level 150, got: {settings}");
    }

    // No SQL Server version this dialect targets (2017+) can even be CREATEd below
    // compatibility level 100 (confirmed live: ALTER DATABASE ... SET COMPATIBILITY_LEVEL = 80/90
    // is rejected outright on both a real SQL Server 2017 and 2022 engine). This test exists for
    // the deliberately-kept defensive fallback: a database somehow reporting a pre-2005
    // compatibility level still gets the full legacy baseline, on the off chance a genuinely old,
    // unsupported SQL Server/database ends up connected anyway.
    [Fact]
    public async Task GetConnectionSessionSettings_LegacyCompatibilityLevel_EnforcesFullBaseline()
    {
        await using var conn = BuildCompatibilityLevelConnection((byte)80);
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var dialect = new SqlServerDialect(factory, NullLogger<SqlServerDialect>.Instance);
        await dialect.DetectDatabaseInfoAsync(conn);
        using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory);
        var settings = dialect.GetConnectionSessionSettings(ctx, false);

        Assert.Contains("SET ANSI_NULLS ON", settings);
        Assert.Contains("SET ARITHABORT ON", settings);
        Assert.Contains("SET QUOTED_IDENTIFIER ON", settings);
    }

    // If the compatibility level genuinely cannot be determined (query fails, returns an
    // unexpected type, etc.), the safe default is to enforce the full baseline rather than
    // silently assume a modern engine.
    [Fact]
    public async Task GetConnectionSessionSettings_CompatibilityLevelUnavailable_FallsBackToFullBaseline()
    {
        await using var conn = BuildCompatibilityLevelConnection(DBNull.Value);
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var dialect = new SqlServerDialect(factory, NullLogger<SqlServerDialect>.Instance);
        await dialect.DetectDatabaseInfoAsync(conn);
        using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory);
        var settings = dialect.GetConnectionSessionSettings(ctx, false);

        Assert.Contains("SET ANSI_NULLS ON", settings);
        Assert.Contains("SET ARITHABORT ON", settings);
    }
}