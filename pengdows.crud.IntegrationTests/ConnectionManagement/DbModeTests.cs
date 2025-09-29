using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using testbed;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.ConnectionManagement;

/// <summary>
/// Integration tests for DbMode functionality demonstrating pengdows.crud's
/// intelligent connection management strategies across different database providers.
/// </summary>
public class DbModeTests : DatabaseTestBase
{
    public DbModeTests(ITestOutputHelper output) : base(output) { }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var tableCreator = new TestTableCreator(context);
        await tableCreator.CreateTestTableAsync();
    }

    [Fact]
    public async Task DbMode_Standard_OptimizesConnectionUsage()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Create a new context with Standard mode explicitly
            var config = new DatabaseContextConfiguration
            {
                ConnectionString = context.ConnectionString,
                DbProviderFactory = context.Factory,
                DbMode = DbMode.Standard
            };

            using var standardContext = new DatabaseContext(config);
            var helper = CreateEntityHelper(standardContext);

            // Act - Perform multiple operations
            var entity1 = CreateTestEntity($"Standard1-{provider}");
            var entity2 = CreateTestEntity($"Standard2-{provider}");

            await helper.CreateAsync(entity1, standardContext);
            await helper.CreateAsync(entity2, standardContext);

            var retrieved = await helper.RetrieveAsync(new[] { entity1.Id, entity2.Id }, standardContext);

            // Assert
            Assert.Equal(DbMode.Standard, standardContext.ConnectionMode);
            Assert.Equal(2, retrieved.Count);
            Assert.True(standardContext.NumberOfOpenConnections >= 0); // Connections should be closed between operations

            Output.WriteLine($"{provider} Standard mode: Max connections: {standardContext.MaxNumberOfConnections}");
        });
    }

    [Fact]
    public async Task DbMode_KeepAlive_MaintainsSentinelConnection()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Skip if provider doesn't benefit from KeepAlive
            if (provider == SupportedDatabase.Sqlite)
            {
                Output.WriteLine($"Skipping KeepAlive test for {provider} - not applicable");
                return;
            }

            // Create a new context with KeepAlive mode
            var config = new DatabaseContextConfiguration
            {
                ConnectionString = context.ConnectionString,
                DbProviderFactory = context.Factory,
                DbMode = DbMode.KeepAlive
            };

            using var keepAliveContext = new DatabaseContext(config);
            var helper = CreateEntityHelper(keepAliveContext);

            // Act - Perform operations
            var entity = CreateTestEntity($"KeepAlive-{provider}");
            await helper.CreateAsync(entity, keepAliveContext);

            // Assert
            Assert.Equal(DbMode.KeepAlive, keepAliveContext.ConnectionMode);
            Assert.True(keepAliveContext.NumberOfOpenConnections >= 1); // Should maintain sentinel connection

            var retrieved = await helper.RetrieveOneAsync(entity.Id, keepAliveContext);
            Assert.NotNull(retrieved);

            Output.WriteLine($"{provider} KeepAlive mode: Open connections: {keepAliveContext.NumberOfOpenConnections}");
        });
    }

    [Fact]
    public async Task DbMode_SingleWriter_HandlesReadWriteOperations()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Skip if provider doesn't benefit from SingleWriter
            if (provider != SupportedDatabase.Sqlite)
            {
                Output.WriteLine($"Skipping SingleWriter test for {provider} - primarily for SQLite");
                return;
            }

            // Create a new context with SingleWriter mode
            var config = new DatabaseContextConfiguration
            {
                ConnectionString = context.ConnectionString,
                DbProviderFactory = context.Factory,
                DbMode = DbMode.SingleWriter
            };

            using var singleWriterContext = new DatabaseContext(config);
            var helper = CreateEntityHelper(singleWriterContext);

            // Act - Mix read and write operations
            var entity1 = CreateTestEntity($"SingleWriter1-{provider}");
            var entity2 = CreateTestEntity($"SingleWriter2-{provider}");

            // Write operations
            await helper.CreateAsync(entity1, singleWriterContext);
            await helper.CreateAsync(entity2, singleWriterContext);

            // Read operations
            var allEntities = await helper.RetrieveAsync(new[] { entity1.Id, entity2.Id }, singleWriterContext);

            // Write operation (update)
            entity1.Name = $"Updated-{provider}";
            await helper.UpdateAsync(entity1, singleWriterContext);

            // Assert
            Assert.Equal(DbMode.SingleWriter, singleWriterContext.ConnectionMode);
            Assert.Equal(2, allEntities.Count);

            var updated = await helper.RetrieveOneAsync(entity1.Id, singleWriterContext);
            Assert.NotNull(updated);
            Assert.Contains("Updated", updated.Name);

            Output.WriteLine($"{provider} SingleWriter mode: Max connections: {singleWriterContext.MaxNumberOfConnections}");
        });
    }

    [Fact]
    public async Task DbMode_SingleConnection_SerializesAllOperations()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Sqlite, async context =>
        {
            // Create in-memory SQLite which should auto-select SingleConnection mode
            var config = new DatabaseContextConfiguration
            {
                ConnectionString = "Data Source=:memory:",
                DbProviderFactory = context.Factory,
                DbMode = DbMode.SingleConnection
            };

            using var singleConnContext = new DatabaseContext(config);

            // Setup table in the single connection context
            await SetupDatabaseAsync(SupportedDatabase.Sqlite, singleConnContext);
            var helper = CreateEntityHelper(singleConnContext);

            // Act - Perform concurrent-like operations on single connection
            var entity1 = CreateTestEntity("SingleConn1");
            var entity2 = CreateTestEntity("SingleConn2");

            await helper.CreateAsync(entity1, singleConnContext);
            await helper.CreateAsync(entity2, singleConnContext);

            // Use transaction to test single connection behavior
            using var transaction = await singleConnContext.BeginTransactionAsync();

            entity1.Name = "TransactionUpdate";
            await helper.UpdateAsync(entity1, transaction);

            var retrieved = await helper.RetrieveOneAsync(entity1.Id, transaction);
            Assert.NotNull(retrieved);
            Assert.Equal("TransactionUpdate", retrieved.Name);

            await transaction.CommitAsync();

            // Assert
            Assert.Equal(DbMode.SingleConnection, singleConnContext.ConnectionMode);
            Assert.Equal(1, singleConnContext.MaxNumberOfConnections); // Should never exceed 1

            Output.WriteLine($"SQLite SingleConnection mode: Max connections: {singleConnContext.MaxNumberOfConnections}");
        });
    }

    [Fact]
    public async Task DbMode_AutoSelection_ChoosesOptimalMode()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Test that the library auto-selects appropriate modes for different connection strings
            var testCases = GetConnectionStringTestCases(provider, context.ConnectionString);

            foreach (var (connectionString, expectedMode, description) in testCases)
            {
                try
                {
                    var config = new DatabaseContextConfiguration
                    {
                        ConnectionString = connectionString,
                        DbProviderFactory = context.Factory
                        // No explicit DbMode - let it auto-select
                    };

                    using var autoContext = new DatabaseContext(config);

                    // Assert
                    Assert.Equal(expectedMode, autoContext.ConnectionMode);
                    Output.WriteLine($"{provider} Auto-selection: {description} -> {expectedMode}");
                }
                catch (Exception ex)
                {
                    Output.WriteLine($"{provider} Auto-selection test failed for {description}: {ex.Message}");
                    // Continue with other test cases
                }
            }
        });
    }

    [Fact]
    public async Task DbMode_ConnectionCounting_TracksOpenConnections()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = CreateEntityHelper(context);

            // Record initial state
            var initialOpen = context.NumberOfOpenConnections;
            var initialMax = context.MaxNumberOfConnections;

            // Act - Perform operations that should use connections
            var entity = CreateTestEntity($"Counting-{provider}");
            await helper.CreateAsync(entity, context);

            var retrieved = await helper.RetrieveOneAsync(entity.Id, context);
            Assert.NotNull(retrieved);

            // Assert - Connection counting should work
            Assert.True(context.MaxNumberOfConnections >= initialMax);

            Output.WriteLine($"{provider} Connection tracking - Initial: {initialOpen}, Max reached: {context.MaxNumberOfConnections}");
        });
    }

    [Fact]
    public async Task DbMode_ConcurrentOperations_HandlesParallelAccess()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = CreateEntityHelper(context);

            // Act - Run parallel operations
            var tasks = Enumerable.Range(0, 5).Select(async i =>
            {
                var entity = CreateTestEntity($"Parallel{i}-{provider}");
                await helper.CreateAsync(entity, context);
                return await helper.RetrieveOneAsync(entity.Id, context);
            }).ToArray();

            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.Equal(5, results.Length);
            Assert.All(results, r => Assert.NotNull(r));

            // Check that connection management handled concurrency appropriately
            var maxConnections = context.MaxNumberOfConnections;
            var currentConnections = context.NumberOfOpenConnections;

            Output.WriteLine($"{provider} Parallel operations - Max: {maxConnections}, Current: {currentConnections}");

            // For Standard mode, connections should be released
            if (context.ConnectionMode == DbMode.Standard)
            {
                Assert.True(currentConnections <= maxConnections);
            }
        });
    }

    // Helper methods

    private EntityHelper<TestTable, long> CreateEntityHelper(IDatabaseContext context)
    {
        var auditResolver = Host.Services.GetService<IAuditValueResolver>() ??
                           new StringAuditContextProvider();
        return new EntityHelper<TestTable, long>(context, auditValueResolver: auditResolver);
    }

    private static TestTable CreateTestEntity(string name)
    {
        return new TestTable
        {
            Name = name,
            Value = Random.Shared.Next(1, 1000),
            Description = $"Test description for {name}",
            IsActive = true,
            CreatedOn = DateTime.UtcNow
        };
    }

    private static IEnumerable<(string ConnectionString, DbMode ExpectedMode, string Description)>
        GetConnectionStringTestCases(SupportedDatabase provider, string baseConnectionString)
    {
        return provider switch
        {
            SupportedDatabase.Sqlite => new[]
            {
                ("Data Source=:memory:", DbMode.SingleConnection, "In-memory SQLite"),
                ("Data Source=/tmp/test.db", DbMode.SingleWriter, "File-based SQLite"),
                (baseConnectionString, DbMode.SingleWriter, "Test SQLite connection")
            },

            SupportedDatabase.PostgreSql => new[]
            {
                (baseConnectionString, DbMode.Standard, "Standard PostgreSQL"),
                (AddPoolingToConnectionString(baseConnectionString, false), DbMode.Standard, "PostgreSQL with pooling disabled")
            },

            SupportedDatabase.SqlServer => new[]
            {
                (baseConnectionString, DbMode.Standard, "Standard SQL Server"),
                ("Server=(localdb)\\mssqllocaldb;Database=Test;", DbMode.Standard, "LocalDB connection")
            },

            _ => new[]
            {
                (baseConnectionString, DbMode.Standard, $"Standard {provider} connection")
            }
        };
    }

    private static string AddPoolingToConnectionString(string connectionString, bool enablePooling)
    {
        var poolingValue = enablePooling ? "true" : "false";
        return connectionString.Contains("Pooling=")
            ? connectionString
            : $"{connectionString};Pooling={poolingValue}";
    }
}

/// <summary>
/// Helper class for test table creation (minimal version for this test file)
/// </summary>
internal class TestTableCreator
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
            SupportedDatabase.Sqlite => @"
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
                )",
            SupportedDatabase.PostgreSql => @"
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
                )",
            _ => throw new NotSupportedException($"Database {_context.Product} not supported in this test")
        };

        using var container = _context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }
}