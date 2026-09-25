using System;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

// Minimal DbProviderFactory stub whose namespace contains "MySqlConnector".
// MySqlDialect checks factory.GetType().Namespace for "MySqlConnector" to set _isMySqlConnector = true.
// TiDbDialect inherits MySqlDialect and uses the same flag for PrepareStatements.
namespace pengdows.crud.Tests.MySqlConnector
{
    internal sealed class MinimalConnectorFactory : DbProviderFactory
    {
        // All DbProviderFactory methods are virtual returning null — no overrides needed.
        // The namespace alone is what triggers _isMySqlConnector detection.
    }
}

namespace pengdows.crud.Tests.dialects
{
    /// <summary>
    /// Tests for TiDB-specific dialect behaviour.
    /// TiDB inherits MySqlDialect. The key override is PrepareStatements, which returns
    /// _isMySqlConnector (true only for MySqlConnector provider, false for Oracle MySql.Data).
    /// </summary>
    public class TiDbDialectTests
    {
        [Fact]
        public void DatabaseType_IsTiDb()
        {
            var factory = new fakeDbFactory(SupportedDatabase.TiDb);
            var dialect = new TiDbDialect(factory, NullLogger.Instance);

            Assert.Equal(SupportedDatabase.TiDb, dialect.DatabaseType);
        }

        [Fact]
        public void PrepareStatements_WithOracleProvider_ReturnsFalse()
        {
            // fakeDbFactory namespace does not contain "MySqlConnector" → _isMySqlConnector = false
            var factory = new fakeDbFactory(SupportedDatabase.TiDb);
            var dialect = new TiDbDialect(factory, NullLogger.Instance);

            Assert.False(dialect.PrepareStatements);
        }

        [Fact]
        public void PrepareStatements_WithMySqlConnectorProvider_ReturnsTrue()
        {
            // MinimalConnectorFactory namespace contains "MySqlConnector" → _isMySqlConnector = true
            var factory = new MySqlConnector.MinimalConnectorFactory();
            var dialect = new TiDbDialect(factory, NullLogger.Instance);

            Assert.True(dialect.PrepareStatements);
        }

        [Fact]
        public void ProcWrappingStyle_IsNone()
        {
            // TiDB's Go AST parser does not implement stored procedure DDL
            var factory = new fakeDbFactory(SupportedDatabase.TiDb);
            var dialect = new TiDbDialect(factory, NullLogger.Instance);

            Assert.Equal(ProcWrappingStyle.None, dialect.ProcWrappingStyle);
        }

        // BP-119 (3.0 3eb997c): TiDB rejects the MySQL 8.0.20+ "INSERT ... AS incoming" alias
        // (confirmed live on TiDB v7.5.1: syntax error near "AS incoming"). The alias gate compares
        // the parsed version against 8.0.20, and TiDB's parsed version is its own release number,
        // so TiDB v8.x would cross the gate and emit the unsupported alias.
        [Theory]
        [InlineData("8.0.11-TiDB-v7.5.1")]
        [InlineData("8.0.11-TiDB-v8.5.1")]
        public async System.Threading.Tasks.Task UpsertIncoming_NeverUsesAlias_AnyTiDbVersion(string version)
        {
            var factory = new fakeDbFactory(SupportedDatabase.TiDb);
            var connection = new fakeDbConnection { EmulatedProduct = SupportedDatabase.TiDb };
            connection.SetServerVersion(version);
            connection.SetScalarResultForCommand("SELECT VERSION()", version);
            var tracked = new pengdows.crud.wrappers.TrackedConnection(connection);
            tracked.Open();

            var dialect = new TiDbDialect(factory, NullLogger.Instance);
            await dialect.DetectDatabaseInfoAsync(tracked);

            Assert.Null(dialect.UpsertIncomingAlias);
            Assert.Equal("VALUES(\"col\")", dialect.UpsertIncomingColumn("col"));
        }

        [Fact]
        public void GetBaseSessionSettings_IncludesTiDbPessimisticMode()
        {
            var factory = new fakeDbFactory(SupportedDatabase.TiDb);
            var dialect = new TiDbDialect(factory, NullLogger.Instance);

            var result = dialect.GetBaseSessionSettings();

            Assert.Contains("tidb_pessimistic_txn_default", result, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SessionSettings_OmitNoBackslashEscapes(bool initialized)
        {
            // MySql.Data substitutes parameters into text-protocol SQL with backslash escapes.
            // TiDB does not report NO_BACKSLASH_ESCAPES back to the driver, so with that mode set
            // the escapes are taken literally: confirmed live on 2.0.6 as a syntax error for
            // "O'Brien" and silently corrupted backslash/JSON strings.
            SqlDialect dialect;
            DatabaseContext? context = null;
            if (initialized)
            {
                var factory = new fakeDbFactory(SupportedDatabase.TiDb);
                context = new DatabaseContext("Data Source=test;EmulatedProduct=TiDb", factory);
                Assert.Equal(SupportedDatabase.TiDb, context.Product);
                dialect = (SqlDialect)context.Dialect;
            }
            else
            {
                dialect = new TiDbDialect(new fakeDbFactory(SupportedDatabase.TiDb), NullLogger.Instance);
            }

            try
            {
                foreach (var settings in new[]
                         {
                             dialect.GetBaseSessionSettings(),
                             dialect.GetFinalSessionSettings(false),
                             dialect.GetFinalSessionSettings(true)
                         })
                {
                    Assert.DoesNotContain("NO_BACKSLASH_ESCAPES", settings, StringComparison.OrdinalIgnoreCase);
                    Assert.Contains("ANSI_QUOTES", settings, StringComparison.OrdinalIgnoreCase);
                }
            }
            finally
            {
                context?.Dispose();
            }
        }

        // Same corruption on MySQL itself via MySql.Data (confirmed live on 2.0.6: "O'Brien" ->
        // syntax error; JSON with escapes -> "Invalid JSON text"). Oracle's MySql.Data escapes
        // with backslashes regardless, so the mode must not be set when it is the driver.
        [Theory]
        [InlineData(SupportedDatabase.MySql, false)]
        [InlineData(SupportedDatabase.AuroraMySql, false)]
        [InlineData(SupportedDatabase.MariaDb, false)]
        [InlineData(SupportedDatabase.MySql, true)]
        [InlineData(SupportedDatabase.AuroraMySql, true)]
        [InlineData(SupportedDatabase.MariaDb, true)]
        public void MySqlFamily_OnMySqlData_SessionSettings_OmitNoBackslashEscapes(SupportedDatabase db, bool initialized)
        {
            SqlDialect dialect;
            DatabaseContext? context = null;
            if (initialized)
            {
                var factory = new fakeDbFactory(db);
                context = new DatabaseContext($"Data Source=test;EmulatedProduct={db}", factory);
                dialect = (SqlDialect)context.Dialect;
            }
            else
            {
                var factory = new fakeDbFactory(db);
                dialect = db == SupportedDatabase.MariaDb
                    ? new MariaDbDialect(factory, NullLogger.Instance)
                    : new MySqlDialect(factory, NullLogger.Instance, db);
            }

            try
            {
                foreach (var settings in new[]
                         {
                             dialect.GetBaseSessionSettings(),
                             dialect.GetFinalSessionSettings(false),
                             dialect.GetFinalSessionSettings(true)
                         })
                {
                    Assert.DoesNotContain("NO_BACKSLASH_ESCAPES", settings, StringComparison.OrdinalIgnoreCase);
                    Assert.Contains("ANSI_QUOTES", settings, StringComparison.OrdinalIgnoreCase);
                }
            }
            finally
            {
                context?.Dispose();
            }
        }

        // MySqlConnector reads the server's NO_BACKSLASH_ESCAPES status flag and escapes
        // accordingly, so its behavior is unchanged.
        [Theory]
        [InlineData(SupportedDatabase.MySql)]
        [InlineData(SupportedDatabase.MariaDb)]
        public void MySqlFamily_OnMySqlConnector_SessionSettings_KeepNoBackslashEscapes(SupportedDatabase db)
        {
            var factory = new MySqlConnector.MinimalConnectorFactory();
            SqlDialect dialect = db == SupportedDatabase.MariaDb
                ? new MariaDbDialect(factory, NullLogger.Instance, isMySqlConnector: true)
                : new MySqlDialect(factory, NullLogger.Instance, isMySqlConnector: true, db);

            Assert.Contains("NO_BACKSLASH_ESCAPES", dialect.GetBaseSessionSettings(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("NO_BACKSLASH_ESCAPES", dialect.GetFinalSessionSettings(false), StringComparison.OrdinalIgnoreCase);
        }
    }
}
