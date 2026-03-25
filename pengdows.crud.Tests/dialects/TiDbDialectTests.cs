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

        [Fact]
        public void GetBaseSessionSettings_IncludesTiDbPessimisticMode()
        {
            var factory = new fakeDbFactory(SupportedDatabase.TiDb);
            var dialect = new TiDbDialect(factory, NullLogger.Instance);

            var result = dialect.GetBaseSessionSettings();

            Assert.Contains("tidb_pessimistic_txn_default", result, StringComparison.OrdinalIgnoreCase);
        }
    }
}
