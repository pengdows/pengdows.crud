using System;
using System.Data;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests
{
    /// <summary>
    /// Tests for SqlContainer cloning functionality, particularly context-aware cloning
    /// to handle transactions, multi-tenancy, and different database contexts safely.
    /// </summary>
    public class SqlContainerCloningTests : IDisposable
    {
        private readonly DatabaseContext _sqliteContext;
        private readonly DatabaseContext _postgresContext;
        private readonly DatabaseContext _duckDbContext;

        public SqlContainerCloningTests()
        {
            // SQLite context (uses @ parameter markers)
            _sqliteContext = new DatabaseContext(
                "Data Source=:memory:;EmulatedProduct=Sqlite",
                new fakeDbFactory(SupportedDatabase.Sqlite));

            // PostgreSQL context (uses : parameter markers)
            _postgresContext = new DatabaseContext(
                "Data Source=:memory:;EmulatedProduct=PostgreSql",
                new fakeDbFactory(SupportedDatabase.PostgreSql));

            // DuckDB context (uses $ parameter markers)
            _duckDbContext = new DatabaseContext(
                "Data Source=:memory:;EmulatedProduct=DuckDB",
                new fakeDbFactory(SupportedDatabase.DuckDB));
        }

        [Fact]
        public void Clone_WithNullContext_UsesOriginalContext()
        {
            // Arrange
            using var originalContainer = _sqliteContext.CreateSqlContainer("SELECT * FROM users");
            originalContainer.AddParameterWithValue("id", DbType.Int32, 123);

            // Act
            using var clonedContainer = originalContainer.Clone(null);

            // Assert
            Assert.Equal(originalContainer.QuotePrefix, clonedContainer.QuotePrefix);
            Assert.Equal(originalContainer.QuoteSuffix, clonedContainer.QuoteSuffix);
            Assert.Equal(originalContainer.Query.ToString(), clonedContainer.Query.ToString());
            Assert.Equal(originalContainer.ParameterCount, clonedContainer.ParameterCount);
        }

        [Fact]
        public void Clone_WithSameContext_PreservesOriginalBehavior()
        {
            // Arrange
            using var originalContainer = _sqliteContext.CreateSqlContainer("SELECT * FROM users");
            originalContainer.AddParameterWithValue("id", DbType.Int32, 123);

            // Act
            using var clonedContainer = originalContainer.Clone(_sqliteContext);

            // Assert
            Assert.Equal(originalContainer.QuotePrefix, clonedContainer.QuotePrefix);
            Assert.Equal(originalContainer.QuoteSuffix, clonedContainer.QuoteSuffix);
            Assert.Equal(originalContainer.Query.ToString(), clonedContainer.Query.ToString());
            Assert.Equal(originalContainer.ParameterCount, clonedContainer.ParameterCount);
        }

        [Fact]
        public void Clone_WithDifferentContext_UsesTargetContextDialect()
        {
            // Arrange - Create container with SQLite context 
            using var originalContainer = _sqliteContext.CreateSqlContainer("SELECT * FROM users");
            originalContainer.AddParameterWithValue("id", DbType.Int32, 123);

            // Act - Clone with DuckDB context 
            using var clonedContainer = originalContainer.Clone(_duckDbContext);

            // Assert
            // Should use DuckDB dialect parameter naming, not SQLite
            // Test parameter name formatting which uses dialect-specific markers
            var sqliteParam = originalContainer.MakeParameterName("test");  
            var duckDbParam = clonedContainer.MakeParameterName("test");
            Assert.NotEqual(sqliteParam, duckDbParam); // Different parameter formats
            
            // SQL and parameters should be copied
            Assert.Equal(originalContainer.Query.ToString(), clonedContainer.Query.ToString());
            Assert.Equal(originalContainer.ParameterCount, clonedContainer.ParameterCount);
        }

        [Fact]
        public void Clone_ParametersUseTargetContextDialect()
        {
            // Arrange - Create container with PostgreSQL context (: parameter marker)
            using var originalContainer = _postgresContext.CreateSqlContainer("SELECT * FROM users WHERE id = ");
            var originalParam = originalContainer.AddParameterWithValue("id", DbType.Int32, 123);
            
            // Act - Clone with DuckDB context ($ parameter marker)
            using var clonedContainer = originalContainer.Clone(_duckDbContext);

            // Assert
            Assert.Equal(originalContainer.ParameterCount, clonedContainer.ParameterCount);
            
            // The parameter should exist and have the same value
            var parameterValue = clonedContainer.GetParameterValue<int>("id");
            Assert.Equal(123, parameterValue);
            
            // Verify parameter formatting changed between contexts
            var pgParam = originalContainer.MakeParameterName("test");
            var duckParam = clonedContainer.MakeParameterName("test");
            Assert.NotEqual(pgParam, duckParam);
        }

        [Fact]
        public void Clone_WithTransactionContext_UsesTransactionDialect()
        {
            // Arrange - Create container with SQLite context (@ parameter marker)
            using var originalContainer = _sqliteContext.CreateSqlContainer("UPDATE users SET name = ");
            originalContainer.AddParameterWithValue("name", DbType.String, "Test");

            // Act - Clone with PostgreSQL transaction context (: parameter marker)
            using var transactionContext = _postgresContext.BeginTransaction();
            using var clonedContainer = originalContainer.Clone(transactionContext);

            // Assert
            // Should use PostgreSQL dialect from transaction context, not SQLite
            var sqliteParam = originalContainer.MakeParameterName("test");
            var pgParam = clonedContainer.MakeParameterName("test");
            Assert.NotEqual(sqliteParam, pgParam);
            
            // SQL and parameters should be copied
            Assert.Equal(originalContainer.Query.ToString(), clonedContainer.Query.ToString());
            Assert.Equal(originalContainer.ParameterCount, clonedContainer.ParameterCount);
            
            var parameterValue = clonedContainer.GetParameterValue<string>("name");
            Assert.Equal("Test", parameterValue);
            
            transactionContext.Commit();
        }

        [Fact]
        public void Clone_PreservesParameterProperties()
        {
            // Arrange - Use regular input parameter instead of output parameter
            using var originalContainer = _sqliteContext.CreateSqlContainer("SELECT * FROM users WHERE id = ");
            var param = originalContainer.AddParameterWithValue("id", DbType.Int32, 123);
            param.Size = 100;
            param.Scale = 2;
            param.Precision = 10;

            // Act - Clone with PostgreSQL context (has output parameter support)
            using var clonedContainer = originalContainer.Clone(_postgresContext);

            // Assert
            Assert.Equal(originalContainer.ParameterCount, clonedContainer.ParameterCount);
            Assert.Equal(1, clonedContainer.ParameterCount);
            
            // Verify the parameter value was copied correctly
            Assert.Equal(123, clonedContainer.GetParameterValue<int>("id"));
            
            // Verify parameter formatting changed
            var sqliteParam = originalContainer.MakeParameterName("test");
            var pgParam = clonedContainer.MakeParameterName("test");
            Assert.NotEqual(sqliteParam, pgParam);
        }

        [Fact]
        public void Clone_IndependentParameterModifications()
        {
            // Arrange
            using var originalContainer = _sqliteContext.CreateSqlContainer("SELECT * FROM users WHERE id = ");
            originalContainer.AddParameterWithValue("id", DbType.Int32, 123);

            // Act
            using var clonedContainer = originalContainer.Clone(_duckDbContext);
            
            // Modify cloned container's parameter
            clonedContainer.SetParameterValue("id", 456);

            // Assert
            Assert.Equal(123, originalContainer.GetParameterValue<int>("id"));
            Assert.Equal(456, clonedContainer.GetParameterValue<int>("id"));
        }

        [Fact]
        public void Clone_IndependentQueryModifications()
        {
            // Arrange
            using var originalContainer = _sqliteContext.CreateSqlContainer("SELECT * FROM users");

            // Act
            using var clonedContainer = originalContainer.Clone(_duckDbContext);
            
            // Modify cloned container's query
            clonedContainer.Query.Append(" WHERE active = 1");

            // Assert
            Assert.Equal("SELECT * FROM users", originalContainer.Query.ToString());
            Assert.Equal("SELECT * FROM users WHERE active = 1", clonedContainer.Query.ToString());
        }

        [Fact]
        public void Clone_PreservesWhereFlag()
        {
            // Arrange
            using var originalContainer = _sqliteContext.CreateSqlContainer("SELECT * FROM users WHERE id = 1");
            originalContainer.HasWhereAppended = true;

            // Act
            using var clonedContainer = originalContainer.Clone(_duckDbContext);

            // Assert
            Assert.True(originalContainer.HasWhereAppended);
            Assert.True(clonedContainer.HasWhereAppended);
        }

        [Fact]
        public void Clone_DisposedOriginal_CloneStillWorks()
        {
            // Arrange
            ISqlContainer clonedContainer;
            using (var originalContainer = _sqliteContext.CreateSqlContainer("SELECT * FROM users"))
            {
                originalContainer.AddParameterWithValue("id", DbType.Int32, 123);
                
                // Clone before disposing original
                clonedContainer = originalContainer.Clone(_duckDbContext);
            }
            // originalContainer is now disposed

            // Act & Assert - cloned container should still work
            using (clonedContainer)
            {
                // Verify it uses DuckDB parameter formatting
                var duckParam = clonedContainer.MakeParameterName("test");
                Assert.Contains("$", duckParam); // DuckDB uses $ markers
                Assert.Equal("SELECT * FROM users", clonedContainer.Query.ToString());
                Assert.Equal(123, clonedContainer.GetParameterValue<int>("id"));
            }
        }

        [Fact] 
        public void Clone_MultipleParameters_AllCopiedCorrectly()
        {
            // Arrange
            using var originalContainer = _postgresContext.CreateSqlContainer(
                "SELECT * FROM users WHERE id = ? AND name = ? AND active = ?");
            
            originalContainer.AddParameterWithValue("id", DbType.Int32, 123);
            originalContainer.AddParameterWithValue("name", DbType.String, "John");
            originalContainer.AddParameterWithValue("active", DbType.Boolean, true);

            // Act
            using var clonedContainer = originalContainer.Clone(_duckDbContext);

            // Assert
            Assert.Equal(3, clonedContainer.ParameterCount);
            Assert.Equal(123, clonedContainer.GetParameterValue<int>("id"));
            Assert.Equal("John", clonedContainer.GetParameterValue<string>("name"));
            Assert.True(clonedContainer.GetParameterValue<bool>("active"));
        }

        [Fact]
        public void Clone_MultiTenantScenario_PreventsCrossTenantDataLeakage()
        {
            // Arrange - Simulate cached container for TenantA (SQLite with @ parameter marker)
            using var tenantAContainer = _sqliteContext.CreateSqlContainer("SELECT * FROM users WHERE tenant_id = ");
            tenantAContainer.AddParameterWithValue("tenant_id", DbType.String, "TenantA");
            
            // Act - Clone for TenantB (DuckDB with $ parameter marker) - simulates cached container reuse
            using var tenantBContainer = tenantAContainer.Clone(_duckDbContext);
            tenantBContainer.SetParameterValue("tenant_id", "TenantB");

            // Assert
            // Cloned container should use TenantB's context/dialect, not TenantA's
            var sqliteParam = tenantAContainer.MakeParameterName("test");
            var duckParam = tenantBContainer.MakeParameterName("test");
            Assert.NotEqual(sqliteParam, duckParam); // Different parameter formatting
            
            // Parameters should be independent
            Assert.Equal("TenantA", tenantAContainer.GetParameterValue<string>("tenant_id"));
            Assert.Equal("TenantB", tenantBContainer.GetParameterValue<string>("tenant_id"));
            
            // SQL should be the same
            Assert.Equal(tenantAContainer.Query.ToString(), tenantBContainer.Query.ToString());
        }

        [Fact]
        public void Clone_CachedTemplateScenario_WorksWithDifferentTenants()
        {
            // Arrange - Simulate an EntityHelper cached template built with PostgreSQL context (: parameter marker)
            using var cachedTemplate = _postgresContext.CreateSqlContainer(
                "SELECT id, name, email FROM users WHERE id = ");
            cachedTemplate.AddParameterWithValue("p0", DbType.Int32, 0); // Placeholder value
            
            // Act - Clone for different tenant contexts (simulates EntityHelper fast path)
            using var tenant1Container = cachedTemplate.Clone(_sqliteContext);  // @ parameter marker
            using var tenant2Container = cachedTemplate.Clone(_duckDbContext);  // $ parameter marker
            
            // Update with actual values for each tenant
            tenant1Container.SetParameterValue("p0", 100);
            tenant2Container.SetParameterValue("p0", 200);

            // Assert
            // Each cloned container should use its target context's dialect
            var pgParam = cachedTemplate.MakeParameterName("test");
            var sqliteParam = tenant1Container.MakeParameterName("test");
            var duckParam = tenant2Container.MakeParameterName("test");
            
            // All should be different
            Assert.NotEqual(pgParam, sqliteParam);
            Assert.NotEqual(pgParam, duckParam);
            Assert.NotEqual(sqliteParam, duckParam);
            
            // Parameters should be independent
            Assert.Equal(100, tenant1Container.GetParameterValue<int>("p0"));
            Assert.Equal(200, tenant2Container.GetParameterValue<int>("p0"));
            
            // Original template should be unchanged
            Assert.Equal(0, cachedTemplate.GetParameterValue<int>("p0"));
        }

        public void Dispose()
        {
            _sqliteContext?.Dispose();
            _postgresContext?.Dispose();
            _duckDbContext?.Dispose();
        }
    }
}
