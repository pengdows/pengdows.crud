using pengdows.crud;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using System.Data;
using System.Data.Common;
using testbed;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.ErrorHandling;

/// <summary>
/// Integration tests for database constraint violations including
/// primary key, unique constraint, and foreign key violations.
/// </summary>
public class ConstraintViolationTests : DatabaseTestBase
{
    private static long _nextId;

    public ConstraintViolationTests(ITestOutputHelper output) : base(output) { }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var tableCreator = new TestTableCreator(context);
        await tableCreator.CreateTestTableAsync();

        // Create a second table with foreign key for FK tests
        await CreateRelatedTableAsync(provider, context);
    }

    [Fact]
    public async Task PrimaryKeyViolation_DuplicateId_ThrowsException()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var helper = CreateEntityHelper(context);
            var entity1 = CreateTestEntity(NameEnum.Test, 100);

            // Create first entity
            await helper.CreateAsync(entity1, context);

            // Act & Assert - Try to insert duplicate ID
            var entity2 = CreateTestEntity(NameEnum.Test2, 200);
            entity2.Id = entity1.Id; // Same ID as first entity

            await Assert.ThrowsAnyAsync<DbException>(async () =>
            {
                using var container = helper.BuildCreate(entity2, context);
                await container.ExecuteNonQueryAsync();
            });
        });
    }

    [Fact]
    public async Task PrimaryKeyViolation_ManualInsert_ThrowsException()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var id = Interlocked.Increment(ref _nextId);

            // Insert first record
            using (var container = context.CreateSqlContainer())
            {
                container.Query.Append("INSERT INTO test_table (id, name, value, is_active, created_at) VALUES (");
                container.Query.Append(container.MakeParameterName("id")).Append(", ");
                container.Query.Append(container.MakeParameterName("name")).Append(", ");
                container.Query.Append(container.MakeParameterName("value")).Append(", ");
                container.Query.Append(container.MakeParameterName("active")).Append(", ");
                container.Query.Append(container.MakeParameterName("created")).Append(")");

                container.AddParameterWithValue("id", DbType.Int64, id);
                container.AddParameterWithValue("name", DbType.String, "Test");
                container.AddParameterWithValue("value", DbType.Int32, 100);
                container.AddParameterWithValue("active", GetBooleanDbType(provider), true);
                container.AddParameterWithValue("created", DbType.DateTime, DateTime.UtcNow);

                await container.ExecuteNonQueryAsync();
            }

            // Act & Assert - Try to insert duplicate
            await Assert.ThrowsAnyAsync<DbException>(async () =>
            {
                using var container = context.CreateSqlContainer();
                container.Query.Append("INSERT INTO test_table (id, name, value, is_active, created_at) VALUES (");
                container.Query.Append(container.MakeParameterName("id")).Append(", ");
                container.Query.Append(container.MakeParameterName("name")).Append(", ");
                container.Query.Append(container.MakeParameterName("value")).Append(", ");
                container.Query.Append(container.MakeParameterName("active")).Append(", ");
                container.Query.Append(container.MakeParameterName("created")).Append(")");

                container.AddParameterWithValue("id", DbType.Int64, id); // Same ID
                container.AddParameterWithValue("name", DbType.String, "Test2");
                container.AddParameterWithValue("value", DbType.Int32, 200);
                container.AddParameterWithValue("active", GetBooleanDbType(provider), true);
                container.AddParameterWithValue("created", DbType.DateTime, DateTime.UtcNow);

                await container.ExecuteNonQueryAsync();
            });
        });
    }

    [Fact]
    public async Task UniqueConstraint_DuplicateValue_ThrowsException()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Skip providers where we can't easily add unique constraints
            if (!SupportsUniqueConstraints(provider))
            {
                Output.WriteLine($"Skipping unique constraint test for {provider}");
                return;
            }

            // Arrange - Add unique constraint on name field
            await AddUniqueConstraintAsync(provider, context);

            var helper = CreateEntityHelper(context);
            var entity1 = CreateTestEntity(NameEnum.Test, 100);
            await helper.CreateAsync(entity1, context);

            // Act & Assert - Try to insert duplicate name
            var entity2 = CreateTestEntity(NameEnum.Test2, 200);
            entity2.Name = entity1.Name; // Same as entity1

            await Assert.ThrowsAnyAsync<DbException>(async () =>
            {
                await helper.CreateAsync(entity2, context);
            });
        });
    }

    [Fact]
    public async Task ForeignKeyViolation_InvalidReference_ThrowsException()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Skip SQLite which doesn't enforce FK by default
            if (provider == SupportedDatabase.Sqlite)
            {
                Output.WriteLine("Skipping FK test for SQLite");
                return;
            }

            // Arrange - Try to insert into related table with non-existent parent ID
            var nonExistentId = -99999L;

            // Act & Assert
            await Assert.ThrowsAnyAsync<DbException>(async () =>
            {
                using var container = context.CreateSqlContainer();
                AppendInsertRelatedTable(container, provider, nonExistentId, "Related Item");
                await container.ExecuteNonQueryAsync();
            });
        });
    }

    [Fact]
    public async Task ForeignKeyViolation_DeleteParent_ThrowsException()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (provider == SupportedDatabase.Sqlite)
            {
                Output.WriteLine("Skipping FK delete test for SQLite");
                return;
            }

            // Arrange - Create parent record
            var helper = CreateEntityHelper(context);
            var parent = CreateTestEntity(NameEnum.Test, 300);
            await helper.CreateAsync(parent, context);

            // Create child record
            using (var container = context.CreateSqlContainer())
            {
                AppendInsertRelatedTable(container, provider, parent.Id, "Child Item");
                await container.ExecuteNonQueryAsync();
            }

            // Act & Assert - Try to delete parent (should fail due to FK)
            await Assert.ThrowsAnyAsync<DbException>(async () =>
            {
                await helper.DeleteAsync(parent.Id, context);
            });
        });
    }

    [Fact]
    public async Task NotNullViolation_NullRequiredField_ThrowsException()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Act & Assert - Try to insert NULL into NOT NULL column
            await Assert.ThrowsAnyAsync<DbException>(async () =>
            {
                using var container = context.CreateSqlContainer();

                // name is NOT NULL, so this should fail
                container.Query.Append("INSERT INTO test_table (id, name, value, is_active, created_at) VALUES (");
                container.Query.Append(container.MakeParameterName("id")).Append(", ");
                container.Query.Append("NULL, "); // NULL in NOT NULL column
                container.Query.Append(container.MakeParameterName("value")).Append(", ");
                container.Query.Append(container.MakeParameterName("active")).Append(", ");
                container.Query.Append(container.MakeParameterName("created")).Append(")");

                container.AddParameterWithValue("id", DbType.Int64, Interlocked.Increment(ref _nextId));
                container.AddParameterWithValue("value", DbType.Int32, 400);
                container.AddParameterWithValue("active", GetBooleanDbType(provider), true);
                container.AddParameterWithValue("created", DbType.DateTime, DateTime.UtcNow);

                await container.ExecuteNonQueryAsync();
            });
        });
    }

    [Fact]
    public async Task CheckConstraint_InvalidValue_ThrowsException()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Skip providers that don't support CHECK constraints well
            if (!SupportsCheckConstraints(provider))
            {
                Output.WriteLine($"Skipping CHECK constraint test for {provider}");
                return;
            }

            // Arrange - Add CHECK constraint
            await AddCheckConstraintAsync(provider, context);

            // Act & Assert - Try to insert value that violates CHECK
            await Assert.ThrowsAnyAsync<DbException>(async () =>
            {
                using var container = context.CreateSqlContainer();
                container.Query.Append("INSERT INTO test_table (id, name, value, is_active, created_at) VALUES (");
                container.Query.Append(container.MakeParameterName("id")).Append(", ");
                container.Query.Append(container.MakeParameterName("name")).Append(", ");
                container.Query.Append(container.MakeParameterName("value")).Append(", ");
                container.Query.Append(container.MakeParameterName("active")).Append(", ");
                container.Query.Append(container.MakeParameterName("created")).Append(")");

                container.AddParameterWithValue("id", DbType.Int64, Interlocked.Increment(ref _nextId));
                container.AddParameterWithValue("name", DbType.String, "Test");
                container.AddParameterWithValue("value", DbType.Int32, -100); // Negative value should violate CHECK
                container.AddParameterWithValue("active", GetBooleanDbType(provider), true);
                container.AddParameterWithValue("created", DbType.DateTime, DateTime.UtcNow);

                await container.ExecuteNonQueryAsync();
            });
        });
    }

    [Fact]
    public async Task Transaction_RollbackOnConstraintViolation_LeavesNoSideEffects()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var helper = CreateEntityHelper(context);
            var validEntity = CreateTestEntity(NameEnum.Test, 500);

            // Act
            try
            {
                using var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);
                var txHelper = CreateEntityHelper(transaction);

                // Insert valid entity
                await txHelper.CreateAsync(validEntity, transaction);

                // Try to insert duplicate ID (should fail)
                var duplicateEntity = CreateTestEntity(NameEnum.Test2, 501);
                duplicateEntity.Id = validEntity.Id;

                using var container = txHelper.BuildCreate(duplicateEntity, transaction);
                await container.ExecuteNonQueryAsync(); // This should throw

                transaction.Commit(); // Should never reach here
            }
            catch (DbException)
            {
                // Expected - constraint violation
            }

            // Assert - First entity should NOT exist (transaction rolled back)
            var retrieved = await helper.RetrieveOneAsync(validEntity.Id, context);
            Assert.Null(retrieved);
        });
    }

    [Fact]
    public async Task BatchInsert_OneConstraintViolation_DoesNotAffectOthers()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var helper = CreateEntityHelper(context);
            var validEntities = new[]
            {
                CreateTestEntity(NameEnum.Test, 600),
                CreateTestEntity(NameEnum.Test2, 601),
                CreateTestEntity(NameEnum.Test, 602)
            };

            // Insert first entity to create conflict later
            await helper.CreateAsync(validEntities[0], context);

            // Act - Try batch insert with one duplicate
            var successCount = 0;
            var failureCount = 0;

            foreach (var entity in validEntities)
            {
                try
                {
                    await helper.CreateAsync(entity, context);
                    successCount++;
                }
                catch (DbException)
                {
                    failureCount++;
                }
            }

            // Assert
            Assert.Equal(2, successCount); // validEntities[1] and validEntities[2]
            Assert.Equal(1, failureCount); // validEntities[0] duplicate

            // Verify successful inserts persisted
            var retrieved = await helper.RetrieveAsync(
                new[] { validEntities[1].Id, validEntities[2].Id },
                context);

            Assert.Equal(2, retrieved.Count);
        });
    }

    private EntityHelper<TestTable, long> CreateEntityHelper(IDatabaseContext context)
    {
        var auditResolver = (IAuditValueResolver?)Host.Services.GetService(typeof(IAuditValueResolver)) ??
                           new StringAuditContextProvider();
        return new EntityHelper<TestTable, long>(context, auditValueResolver: auditResolver);
    }

    private static TestTable CreateTestEntity(NameEnum name, int value)
    {
        return new TestTable
        {
            Id = Interlocked.Increment(ref _nextId),
            Name = name,
            Value = value,
            Description = $"Constraint test: {name}",
            IsActive = true,
            CreatedOn = DateTime.UtcNow
        };
    }

    private async Task CreateRelatedTableAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var sql = provider switch
        {
            SupportedDatabase.Sqlite => @"
                CREATE TABLE IF NOT EXISTS test_related (
                    id INTEGER PRIMARY KEY,
                    test_table_id INTEGER NOT NULL,
                    name TEXT NOT NULL,
                    FOREIGN KEY (test_table_id) REFERENCES test_table(id)
                )",
            SupportedDatabase.PostgreSql => @"
                CREATE TABLE IF NOT EXISTS test_related (
                    id BIGSERIAL PRIMARY KEY,
                    test_table_id BIGINT NOT NULL,
                    name VARCHAR(255) NOT NULL,
                    FOREIGN KEY (test_table_id) REFERENCES test_table(id)
                )",
            SupportedDatabase.SqlServer => @"
                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[test_related]') AND type in (N'U'))
                CREATE TABLE [dbo].[test_related] (
                    [id] BIGINT IDENTITY(1,1) PRIMARY KEY,
                    [test_table_id] BIGINT NOT NULL,
                    [name] NVARCHAR(255) NOT NULL,
                    FOREIGN KEY ([test_table_id]) REFERENCES [dbo].[test_table]([id])
                )",
            SupportedDatabase.MySql or SupportedDatabase.MariaDb => @"
                CREATE TABLE IF NOT EXISTS test_related (
                    id BIGINT AUTO_INCREMENT PRIMARY KEY,
                    test_table_id BIGINT NOT NULL,
                    name VARCHAR(255) NOT NULL,
                    FOREIGN KEY (test_table_id) REFERENCES test_table(id)
                )",
            _ => throw new NotSupportedException($"Provider {provider} not supported for related table")
        };

        using var container = context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    private void AppendInsertRelatedTable(ISqlContainer container, SupportedDatabase provider, long testTableId, string name)
    {
        container.Query.Append("INSERT INTO test_related (test_table_id, name) VALUES (");
        container.Query.Append(container.MakeParameterName("fk_id")).Append(", ");
        container.Query.Append(container.MakeParameterName("name")).Append(")");

        container.AddParameterWithValue("fk_id", DbType.Int64, testTableId);
        container.AddParameterWithValue("name", DbType.String, name);
    }

    private async Task AddUniqueConstraintAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var sql = provider switch
        {
            SupportedDatabase.PostgreSql => "ALTER TABLE test_table ADD CONSTRAINT uq_name UNIQUE (name)",
            SupportedDatabase.SqlServer => "ALTER TABLE test_table ADD CONSTRAINT uq_name UNIQUE ([name])",
            SupportedDatabase.MySql or SupportedDatabase.MariaDb => "ALTER TABLE test_table ADD CONSTRAINT uq_name UNIQUE (name)",
            _ => null
        };

        if (sql != null)
        {
            try
            {
                using var container = context.CreateSqlContainer(sql);
                await container.ExecuteNonQueryAsync();
            }
            catch (DbException)
            {
                // Constraint might already exist, ignore
            }
        }
    }

    private async Task AddCheckConstraintAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var sql = provider switch
        {
            SupportedDatabase.PostgreSql => "ALTER TABLE test_table ADD CONSTRAINT chk_value_positive CHECK (value >= 0)",
            SupportedDatabase.SqlServer => "ALTER TABLE test_table ADD CONSTRAINT chk_value_positive CHECK (value >= 0)",
            SupportedDatabase.MySql or SupportedDatabase.MariaDb => "ALTER TABLE test_table ADD CONSTRAINT chk_value_positive CHECK (value >= 0)",
            _ => null
        };

        if (sql != null)
        {
            try
            {
                using var container = context.CreateSqlContainer(sql);
                await container.ExecuteNonQueryAsync();
            }
            catch (DbException)
            {
                // Constraint might already exist, ignore
            }
        }
    }

    private static DbType GetBooleanDbType(SupportedDatabase provider)
    {
        return provider == SupportedDatabase.Sqlite ? DbType.Int32 : DbType.Boolean;
    }

    private static bool SupportsUniqueConstraints(SupportedDatabase provider)
    {
        return provider is SupportedDatabase.PostgreSql or
                          SupportedDatabase.SqlServer or
                          SupportedDatabase.MySql or
                          SupportedDatabase.MariaDb;
    }

    private static bool SupportsCheckConstraints(SupportedDatabase provider)
    {
        return provider is SupportedDatabase.PostgreSql or
                          SupportedDatabase.SqlServer or
                          SupportedDatabase.MySql or
                          SupportedDatabase.MariaDb;
    }
}
