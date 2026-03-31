using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using testbed;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.ErrorHandling;

/// <summary>
/// Integration tests for database constraint violations including
/// primary key, unique constraint, and foreign key violations.
/// </summary>
[Collection("IntegrationTests")]
public class ConstraintViolationTests : DatabaseTestBase
{
    private static long _nextId;
    private readonly ConditionalWeakTable<IDatabaseContext, TableGateway<TestTable, long>> _gatewayCache = new();

    public ConstraintViolationTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders()
    {
        return base.GetSupportedProviders();
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        if (provider == SupportedDatabase.Snowflake)
        {
            // Snowflake constraints are not enforced; skip setup to avoid unsupported FK/unique assertions.
            return;
        }

        var tableCreator = new TestTableCreator(context);
        await tableCreator.CreateTestTableAsync();

        // Create a second table with foreign key for FK tests
        await CreateRelatedTableAsync(provider, context);
    }

    [SkippableFact]
    public async Task PrimaryKeyViolation_DuplicateId_ThrowsException()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (!context.Dialect.EnforcesConstraints)
            {
                Output.WriteLine($"Skipping constraint test for {provider} (constraints are not enforced)");
                return;
            }

            // Arrange
            var helper = CreateTableGateway(context);
            var entity1 = CreateTestEntity(NameEnum.Test, 100);

            // Create first entity
            await helper.CreateAsync(entity1, context);

            // Act & Assert - Try to insert duplicate ID
            var entity2 = CreateTestEntity(NameEnum.Test2, 200);
            entity2.Id = entity1.Id; // Same ID as first entity

            await Assert.ThrowsAsync<UniqueConstraintViolationException>(async () =>
            {
                await using var container = helper.BuildCreate(entity2, context);
                await container.ExecuteNonQueryAsync();
            });
        });
    }

    [SkippableFact]
    public async Task PrimaryKeyViolation_DuplicateId_ClassifiesAsUniqueConstraintViolation()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (!context.Dialect.EnforcesConstraints)
            {
                Output.WriteLine($"Skipping constraint test for {provider} (constraints are not enforced)");
                return;
            }

            var helper = CreateTableGateway(context);
            var entity1 = CreateTestEntity(NameEnum.Test, 101);
            await helper.CreateAsync(entity1, context);

            var entity2 = CreateTestEntity(NameEnum.Test2, 201);
            entity2.Id = entity1.Id;

            var ex = await CaptureDatabaseExceptionAsync(async () =>
            {
                await using var container = helper.BuildCreate(entity2, context);
                await container.ExecuteNonQueryAsync();
            });

            Assert.Equal(provider, ex.Database);
            Assert.IsType<UniqueConstraintViolationException>(ex);
            var info = context.GetDialect().AnalyzeException(ExtractInnerDbException(ex));
            Assert.Equal(DbErrorCategory.ConstraintViolation, info.Category);
            Assert.Equal(DbConstraintKind.Unique, info.ConstraintKind);
            Assert.True(context.GetDialect().IsUniqueViolation(ExtractInnerDbException(ex)));
        });
    }

    [SkippableFact]
    public async Task PrimaryKeyViolation_ManualInsert_ThrowsException()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (!context.Dialect.EnforcesConstraints)
            {
                Output.WriteLine($"Skipping constraint test for {provider} (constraints are not enforced)");
                return;
            }

            // Arrange
            var id = Interlocked.Increment(ref _nextId);
            var tableName = IntegrationObjectNameHelper.Table(context, "test_table");
            var idColumn = context.WrapObjectName("id");
            var nameColumn = context.WrapObjectName("name");
            var valueColumn = context.WrapObjectName("value");
            var activeColumn = context.WrapObjectName("is_active");
            var createdColumn = context.WrapObjectName("created_at");

            // Insert first record
            await using (var container = context.CreateSqlContainer())
            {
                container.Query.Append("INSERT INTO ").Append(tableName).Append(" (");
                container.Query.Append(idColumn).Append(", ");
                container.Query.Append(nameColumn).Append(", ");
                container.Query.Append(valueColumn).Append(", ");
                container.Query.Append(activeColumn).Append(", ");
                container.Query.Append(createdColumn).Append(") VALUES (");
                container.Query.Append(container.MakeParameterName("id")).Append(", ");
                container.Query.Append(container.MakeParameterName("name")).Append(", ");
                container.Query.Append(container.MakeParameterName("value")).Append(", ");
                container.Query.Append(container.MakeParameterName("active")).Append(", ");
                container.Query.Append(container.MakeParameterName("created")).Append(")");

                container.AddParameterWithValue("id", DbType.Int64, id);
                container.AddParameterWithValue("name", DbType.String, "Test");
                container.AddParameterWithValue("value", DbType.Int32, 100);
                container.AddParameterWithValue("active", context.Dialect.BooleanDbType, true);
                container.AddParameterWithValue("created", DbType.DateTime, DateTime.UtcNow);

                await container.ExecuteNonQueryAsync();
            }

            // Act & Assert - Try to insert duplicate
            await Assert.ThrowsAsync<UniqueConstraintViolationException>(async () =>
            {
                await using var container = context.CreateSqlContainer();
                container.Query.Append("INSERT INTO ").Append(tableName).Append(" (");
                container.Query.Append(idColumn).Append(", ");
                container.Query.Append(nameColumn).Append(", ");
                container.Query.Append(valueColumn).Append(", ");
                container.Query.Append(activeColumn).Append(", ");
                container.Query.Append(createdColumn).Append(") VALUES (");
                container.Query.Append(container.MakeParameterName("id")).Append(", ");
                container.Query.Append(container.MakeParameterName("name")).Append(", ");
                container.Query.Append(container.MakeParameterName("value")).Append(", ");
                container.Query.Append(container.MakeParameterName("active")).Append(", ");
                container.Query.Append(container.MakeParameterName("created")).Append(")");

                container.AddParameterWithValue("id", DbType.Int64, id); // Same ID
                container.AddParameterWithValue("name", DbType.String, "Test2");
                container.AddParameterWithValue("value", DbType.Int32, 200);
                container.AddParameterWithValue("active", context.Dialect.BooleanDbType, true);
                container.AddParameterWithValue("created", DbType.DateTime, DateTime.UtcNow);

                await container.ExecuteNonQueryAsync();
            });
        });
    }

    [SkippableFact]
    public async Task UniqueConstraint_DuplicateValue_ThrowsException()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (!context.Dialect.EnforcesConstraints)
            {
                Output.WriteLine($"Skipping constraint test for {provider} (constraints are not enforced)");
                return;
            }

            // Skip providers where we can't easily add unique constraints
            if (!context.Dialect.SupportsUniqueConstraints)
            {
                Output.WriteLine($"Skipping unique constraint test for {provider}");
                return;
            }

            // Arrange - Add unique constraint on name field
            await AddUniqueConstraintAsync(provider, context);

            var helper = CreateTableGateway(context);
            var entity1 = CreateTestEntity(NameEnum.Test, 100);
            await helper.CreateAsync(entity1, context);

            // Act & Assert - Try to insert duplicate name
            var entity2 = CreateTestEntity(NameEnum.Test2, 200);
            entity2.Name = entity1.Name; // Same as entity1

            await Assert.ThrowsAsync<UniqueConstraintViolationException>(async () => { await helper.CreateAsync(entity2, context); });
        });
    }

    [SkippableFact]
    public async Task UniqueConstraint_DuplicateValue_ClassifiesAsUniqueConstraintViolation()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (!context.Dialect.EnforcesConstraints)
            {
                Output.WriteLine($"Skipping constraint test for {provider} (constraints are not enforced)");
                return;
            }

            if (!context.Dialect.SupportsUniqueConstraints)
            {
                Output.WriteLine($"Skipping unique constraint test for {provider}");
                return;
            }

            await AddUniqueConstraintAsync(provider, context);

            var helper = CreateTableGateway(context);
            var entity1 = CreateTestEntity(NameEnum.Test, 110);
            await helper.CreateAsync(entity1, context);

            var entity2 = CreateTestEntity(NameEnum.Test2, 210);
            entity2.Name = entity1.Name;

            var ex = await CaptureDatabaseExceptionAsync(() => helper.CreateAsync(entity2, context).AsTask());

            Assert.IsType<UniqueConstraintViolationException>(ex);
            var info = context.GetDialect().AnalyzeException(ExtractInnerDbException(ex));
            Assert.Equal(DbErrorCategory.ConstraintViolation, info.Category);
            Assert.Equal(DbConstraintKind.Unique, info.ConstraintKind);
            Assert.True(context.GetDialect().IsUniqueViolation(ExtractInnerDbException(ex)));
        });
    }

    [SkippableFact]
    public async Task ForeignKeyViolation_InvalidReference_ThrowsException()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (!context.Dialect.EnforcesConstraints)
            {
                Output.WriteLine($"Skipping constraint test for {provider} (constraints are not enforced)");
                return;
            }

            // Skip SQLite and TiDB which don't enforce FK by default
            if (!context.Dialect.EnforcesForeignKeyConstraints)
            {
                Output.WriteLine($"Skipping FK test for {provider} (FK not enforced by default)");
                return;
            }

            // Arrange - Try to insert into related table with non-existent parent ID
            var nonExistentId = -99999L;

            // Act & Assert
            await Assert.ThrowsAsync<ForeignKeyViolationException>(async () =>
            {
                await using var container = context.CreateSqlContainer();
                AppendInsertRelatedTable(container, provider, nonExistentId, "Related Item");
                await container.ExecuteNonQueryAsync();
            });
        });
    }

    [SkippableFact]
    public async Task ForeignKeyViolation_InvalidReference_ClassifiesAsConstraintButNotUnique()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (!context.Dialect.EnforcesConstraints)
            {
                Output.WriteLine($"Skipping constraint test for {provider} (constraints are not enforced)");
                return;
            }

            if (!context.Dialect.EnforcesForeignKeyConstraints)
            {
                Output.WriteLine($"Skipping FK test for {provider} (FK not enforced by default)");
                return;
            }

            var ex = await CaptureDatabaseExceptionAsync(async () =>
            {
                await using var container = context.CreateSqlContainer();
                AppendInsertRelatedTable(container, provider, -99999L, "Related Item");
                await container.ExecuteNonQueryAsync();
            });

            Assert.IsType<ForeignKeyViolationException>(ex);
            var info = context.GetDialect().AnalyzeException(ExtractInnerDbException(ex));
            Assert.Equal(DbErrorCategory.ConstraintViolation, info.Category);
            Assert.Equal(DbConstraintKind.ForeignKey, info.ConstraintKind);
            Assert.False(context.GetDialect().IsUniqueViolation(ExtractInnerDbException(ex)));
        });
    }

    [SkippableFact]
    public async Task ForeignKeyViolation_DeleteParent_ThrowsException()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (!context.Dialect.EnforcesConstraints)
            {
                Output.WriteLine($"Skipping constraint test for {provider} (constraints are not enforced)");
                return;
            }

            if (!context.Dialect.EnforcesForeignKeyConstraints)
            {
                Output.WriteLine($"Skipping FK delete test for {provider} (FK not enforced by default)");
                return;
            }

            // Arrange - Create parent record
            var helper = CreateTableGateway(context);
            var parent = CreateTestEntity(NameEnum.Test, 300);
            await helper.CreateAsync(parent, context);

            // Create child record
            await using (var container = context.CreateSqlContainer())
            {
                AppendInsertRelatedTable(container, provider, parent.Id, "Child Item");
                await container.ExecuteNonQueryAsync();
            }

            // Act & Assert - Try to delete parent (should fail due to FK)
            await Assert.ThrowsAsync<ForeignKeyViolationException>(async () => { await helper.DeleteAsync(parent.Id, context); });
        });
    }

    [SkippableFact]
    public async Task NotNullViolation_NullRequiredField_ThrowsException()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (!context.Dialect.EnforcesConstraints)
            {
                Output.WriteLine($"Skipping constraint test for {provider} (constraints are not enforced)");
                return;
            }

            var tableName = IntegrationObjectNameHelper.Table(context, "test_table");
            var idColumn = context.WrapObjectName("id");
            var nameColumn = context.WrapObjectName("name");
            var valueColumn = context.WrapObjectName("value");
            var activeColumn = context.WrapObjectName("is_active");
            var createdColumn = context.WrapObjectName("created_at");

            // Act & Assert - Try to insert NULL into NOT NULL column
            await Assert.ThrowsAsync<NotNullViolationException>(async () =>
            {
                await using var container = context.CreateSqlContainer();

                // name is NOT NULL, so this should fail
                container.Query.Append("INSERT INTO ").Append(tableName).Append(" (");
                container.Query.Append(idColumn).Append(", ");
                container.Query.Append(nameColumn).Append(", ");
                container.Query.Append(valueColumn).Append(", ");
                container.Query.Append(activeColumn).Append(", ");
                container.Query.Append(createdColumn).Append(") VALUES (");
                container.Query.Append(container.MakeParameterName("id")).Append(", ");
                container.Query.Append("NULL, "); // NULL in NOT NULL column
                container.Query.Append(container.MakeParameterName("value")).Append(", ");
                container.Query.Append(container.MakeParameterName("active")).Append(", ");
                container.Query.Append(container.MakeParameterName("created")).Append(")");

                container.AddParameterWithValue("id", DbType.Int64, Interlocked.Increment(ref _nextId));
                container.AddParameterWithValue("value", DbType.Int32, 400);
                container.AddParameterWithValue("active", context.Dialect.BooleanDbType, true);
                container.AddParameterWithValue("created", DbType.DateTime, DateTime.UtcNow);

                await container.ExecuteNonQueryAsync();
            });
        });
    }

    [SkippableFact]
    public async Task NotNullViolation_NullRequiredField_ClassifiesAsConstraintButNotUnique()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (!context.Dialect.EnforcesConstraints)
            {
                Output.WriteLine($"Skipping constraint test for {provider} (constraints are not enforced)");
                return;
            }

            var tableName = IntegrationObjectNameHelper.Table(context, "test_table");
            var idColumn = context.WrapObjectName("id");
            var nameColumn = context.WrapObjectName("name");
            var valueColumn = context.WrapObjectName("value");
            var activeColumn = context.WrapObjectName("is_active");
            var createdColumn = context.WrapObjectName("created_at");

            var ex = await CaptureDatabaseExceptionAsync(async () =>
            {
                await using var container = context.CreateSqlContainer();
                container.Query.Append("INSERT INTO ").Append(tableName).Append(" (");
                container.Query.Append(idColumn).Append(", ");
                container.Query.Append(nameColumn).Append(", ");
                container.Query.Append(valueColumn).Append(", ");
                container.Query.Append(activeColumn).Append(", ");
                container.Query.Append(createdColumn).Append(") VALUES (");
                container.Query.Append(container.MakeParameterName("id")).Append(", ");
                container.Query.Append("NULL, ");
                container.Query.Append(container.MakeParameterName("value")).Append(", ");
                container.Query.Append(container.MakeParameterName("active")).Append(", ");
                container.Query.Append(container.MakeParameterName("created")).Append(")");

                container.AddParameterWithValue("id", DbType.Int64, Interlocked.Increment(ref _nextId));
                container.AddParameterWithValue("value", DbType.Int32, 401);
                container.AddParameterWithValue("active", context.Dialect.BooleanDbType, true);
                container.AddParameterWithValue("created", DbType.DateTime, DateTime.UtcNow);

                await container.ExecuteNonQueryAsync();
            });

            Assert.IsType<NotNullViolationException>(ex);
            var info = context.GetDialect().AnalyzeException(ExtractInnerDbException(ex));
            Assert.Equal(DbErrorCategory.ConstraintViolation, info.Category);
            Assert.Equal(DbConstraintKind.NotNull, info.ConstraintKind);
            Assert.False(context.GetDialect().IsUniqueViolation(ExtractInnerDbException(ex)));
        });
    }

    [SkippableFact]
    public async Task CheckConstraint_InvalidValue_ThrowsException()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (!context.Dialect.EnforcesConstraints)
            {
                Output.WriteLine($"Skipping constraint test for {provider} (constraints are not enforced)");
                return;
            }

            // Skip providers that don't support CHECK constraints well
            if (!context.Dialect.SupportsCheckConstraints)
            {
                Output.WriteLine($"Skipping CHECK constraint test for {provider}");
                return;
            }

            // Arrange - Add CHECK constraint
            await AddCheckConstraintAsync(provider, context);

            var tableName = IntegrationObjectNameHelper.Table(context, "test_table");
            var idColumn = context.WrapObjectName("id");
            var nameColumn = context.WrapObjectName("name");
            var valueColumn = context.WrapObjectName("value");
            var activeColumn = context.WrapObjectName("is_active");
            var createdColumn = context.WrapObjectName("created_at");

            // Act & Assert - Try to insert value that violates CHECK
            await Assert.ThrowsAsync<CheckConstraintViolationException>(async () =>
            {
                await using var container = context.CreateSqlContainer();
                container.Query.Append("INSERT INTO ").Append(tableName).Append(" (");
                container.Query.Append(idColumn).Append(", ");
                container.Query.Append(nameColumn).Append(", ");
                container.Query.Append(valueColumn).Append(", ");
                container.Query.Append(activeColumn).Append(", ");
                container.Query.Append(createdColumn).Append(") VALUES (");
                container.Query.Append(container.MakeParameterName("id")).Append(", ");
                container.Query.Append(container.MakeParameterName("name")).Append(", ");
                container.Query.Append(container.MakeParameterName("value")).Append(", ");
                container.Query.Append(container.MakeParameterName("active")).Append(", ");
                container.Query.Append(container.MakeParameterName("created")).Append(")");

                container.AddParameterWithValue("id", DbType.Int64, Interlocked.Increment(ref _nextId));
                container.AddParameterWithValue("name", DbType.String, "Test");
                container.AddParameterWithValue("value", DbType.Int32, -100); // Negative value should violate CHECK
                container.AddParameterWithValue("active", context.Dialect.BooleanDbType, true);
                container.AddParameterWithValue("created", DbType.DateTime, DateTime.UtcNow);

                await container.ExecuteNonQueryAsync();
            });
        });
    }

    [SkippableFact]
    public async Task CheckConstraint_InvalidValue_ClassifiesAsCheckConstraint()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (!context.Dialect.EnforcesConstraints)
            {
                Output.WriteLine($"Skipping constraint test for {provider} (constraints are not enforced)");
                return;
            }

            if (!context.Dialect.SupportsCheckConstraints)
            {
                Output.WriteLine($"Skipping CHECK constraint test for {provider}");
                return;
            }

            await AddCheckConstraintAsync(provider, context);

            var tableName = IntegrationObjectNameHelper.Table(context, "test_table");
            var idColumn = context.WrapObjectName("id");
            var nameColumn = context.WrapObjectName("name");
            var valueColumn = context.WrapObjectName("value");
            var activeColumn = context.WrapObjectName("is_active");
            var createdColumn = context.WrapObjectName("created_at");

            var ex = await CaptureDatabaseExceptionAsync(async () =>
            {
                await using var container = context.CreateSqlContainer();
                container.Query.Append("INSERT INTO ").Append(tableName).Append(" (");
                container.Query.Append(idColumn).Append(", ");
                container.Query.Append(nameColumn).Append(", ");
                container.Query.Append(valueColumn).Append(", ");
                container.Query.Append(activeColumn).Append(", ");
                container.Query.Append(createdColumn).Append(") VALUES (");
                container.Query.Append(container.MakeParameterName("id")).Append(", ");
                container.Query.Append(container.MakeParameterName("name")).Append(", ");
                container.Query.Append(container.MakeParameterName("value")).Append(", ");
                container.Query.Append(container.MakeParameterName("active")).Append(", ");
                container.Query.Append(container.MakeParameterName("created")).Append(")");

                container.AddParameterWithValue("id", DbType.Int64, Interlocked.Increment(ref _nextId));
                container.AddParameterWithValue("name", DbType.String, "Test");
                container.AddParameterWithValue("value", DbType.Int32, -100);
                container.AddParameterWithValue("active", context.Dialect.BooleanDbType, true);
                container.AddParameterWithValue("created", DbType.DateTime, DateTime.UtcNow);

                await container.ExecuteNonQueryAsync();
            });

            Assert.IsType<CheckConstraintViolationException>(ex);
            var info = context.GetDialect().AnalyzeException(ExtractInnerDbException(ex));
            Assert.Equal(DbErrorCategory.ConstraintViolation, info.Category);
            Assert.Equal(DbConstraintKind.Check, info.ConstraintKind);
            Assert.False(context.GetDialect().IsUniqueViolation(ExtractInnerDbException(ex)));
        });
    }

    [SkippableFact]
    public async Task Transaction_RollbackOnConstraintViolation_LeavesNoSideEffects()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (!context.Dialect.EnforcesConstraints)
            {
                Output.WriteLine($"Skipping constraint test for {provider} (constraints are not enforced)");
                return;
            }

            if (context.Dialect.ReadCommittedCompatibleIsolationLevel != System.Data.IsolationLevel.ReadCommitted)
            {
                Output.WriteLine($"Skipping ReadCommitted transaction test for {provider} (only Serializable isolation is supported)");
                return;
            }

            // Arrange
            var helper = CreateTableGateway(context);
            var validEntity = CreateTestEntity(NameEnum.Test, 500);

            // Act
            try
            {
                await using var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);
                var txHelper = CreateTableGateway(context);

                // Insert valid entity
                await txHelper.CreateAsync(validEntity, transaction);

                // Try to insert duplicate ID (should fail)
                var duplicateEntity = CreateTestEntity(NameEnum.Test2, 501);
                duplicateEntity.Id = validEntity.Id;

                await using var container = txHelper.BuildCreate(duplicateEntity, transaction);
                await container.ExecuteNonQueryAsync(); // This should throw

                transaction.Commit(); // Should never reach here
            }
            catch (DatabaseException)
            {
                // Expected - constraint violation
            }

            // Assert - First entity should NOT exist (transaction rolled back)
            var retrieved = await helper.RetrieveOneAsync(validEntity.Id, context);
            Assert.Null(retrieved);
        });
    }

    [SkippableFact]
    public async Task BatchInsert_OneConstraintViolation_DoesNotAffectOthers()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (!context.Dialect.EnforcesConstraints)
            {
                Output.WriteLine($"Skipping constraint test for {provider} (constraints are not enforced)");
                return;
            }

            // Arrange
            var helper = CreateTableGateway(context);
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
                catch (ConstraintViolationException)
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

    private TableGateway<TestTable, long> CreateTableGateway(IDatabaseContext context)
    {
        return _gatewayCache.GetValue(context, ctx =>
        {
            var auditResolver = GetAuditResolver();
            return new TableGateway<TestTable, long>(ctx, auditResolver);
        });
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
        // DuckDB requires a sequence for auto-increment and needs two separate DDL statements
        if (provider == SupportedDatabase.DuckDB)
        {
            await using var seq = context.CreateSqlContainer("CREATE SEQUENCE IF NOT EXISTS seq_test_related");
            await seq.ExecuteNonQueryAsync();

            await using var tbl = context.CreateSqlContainer(@"
                CREATE TABLE IF NOT EXISTS test_related (
                    id BIGINT DEFAULT nextval('seq_test_related') PRIMARY KEY,
                    test_table_id BIGINT NOT NULL,
                    name VARCHAR(255) NOT NULL,
                    FOREIGN KEY (test_table_id) REFERENCES test_table(id)
                )");
            await tbl.ExecuteNonQueryAsync();
            return;
        }

        // Firebird has no CREATE TABLE IF NOT EXISTS; catch "already exists" instead
        if (provider == SupportedDatabase.Firebird)
        {
            var qpFb = context.QuotePrefix;
            var qsFb = context.QuoteSuffix;
            var fbSql = $@"CREATE TABLE {qpFb}test_related{qsFb} (
                {qpFb}id{qsFb} BIGINT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                {qpFb}test_table_id{qsFb} BIGINT NOT NULL,
                {qpFb}name{qsFb} VARCHAR(255) NOT NULL,
                FOREIGN KEY ({qpFb}test_table_id{qsFb}) REFERENCES {qpFb}test_table{qsFb}({qpFb}id{qsFb})
            )";
            try
            {
                await using var tbl = context.CreateSqlContainer(fbSql);
                await tbl.ExecuteNonQueryAsync();
            }
            catch (Exception ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                // Table already present; swallow
            }
            return;
        }

        var qp = context.QuotePrefix;
        var qs = context.QuoteSuffix;
        var sql = provider switch
        {
            SupportedDatabase.Sqlite => @"
                CREATE TABLE IF NOT EXISTS test_related (
                    id INTEGER PRIMARY KEY,
                    test_table_id INTEGER NOT NULL,
                    name TEXT NOT NULL,
                    FOREIGN KEY (test_table_id) REFERENCES test_table(id)
                )",
            SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb or SupportedDatabase.YugabyteDb => @"
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
            SupportedDatabase.MySql or SupportedDatabase.MariaDb or SupportedDatabase.TiDb => @"
                CREATE TABLE IF NOT EXISTS test_related (
                    id BIGINT AUTO_INCREMENT PRIMARY KEY,
                    test_table_id BIGINT NOT NULL,
                    name VARCHAR(255) NOT NULL,
                    FOREIGN KEY (test_table_id) REFERENCES test_table(id)
                )",
            SupportedDatabase.Oracle => string.Format(@"
                DECLARE
                    table_exists NUMBER;
                BEGIN
                    SELECT COUNT(*) INTO table_exists FROM user_tables WHERE table_name = 'TEST_RELATED';
                    IF table_exists = 0 THEN
                        EXECUTE IMMEDIATE '
                            CREATE TABLE {0}test_related{1} (
                                {0}id{1} NUMBER GENERATED BY DEFAULT ON NULL AS IDENTITY PRIMARY KEY,
                                {0}test_table_id{1} NUMBER NOT NULL,
                                {0}name{1} VARCHAR2(255) NOT NULL,
                                CONSTRAINT fk_test_related FOREIGN KEY ({0}test_table_id{1}) REFERENCES {0}test_table{1}({0}id{1})
                            )';
                    END IF;
                END;", qp, qs),
            _ => throw new NotSupportedException($"Provider {provider} not supported for related table")
        };

        await using var container = context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    private void AppendInsertRelatedTable(ISqlContainer container, SupportedDatabase provider, long testTableId,
        string name)
    {
        var table = container.WrapObjectName("test_related");
        var fkColumn = container.WrapObjectName("test_table_id");
        var nameColumn = container.WrapObjectName("name");

        container.Query.Append("INSERT INTO ")
            .Append(table)
            .Append(" (")
            .Append(fkColumn)
            .Append(", ")
            .Append(nameColumn)
            .Append(") VALUES (");
        container.Query.Append(container.MakeParameterName("fk_id")).Append(", ");
        container.Query.Append(container.MakeParameterName("name")).Append(")");

        container.AddParameterWithValue("fk_id", DbType.Int64, testTableId);
        container.AddParameterWithValue("name", DbType.String, name);
    }

    private async Task AddUniqueConstraintAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var qp = context.QuotePrefix;
        var qs = context.QuoteSuffix;
        var sql = provider switch
        {
            SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb or SupportedDatabase.YugabyteDb =>
                "ALTER TABLE test_table ADD CONSTRAINT uq_name UNIQUE (name)",
            SupportedDatabase.SqlServer => "ALTER TABLE test_table ADD CONSTRAINT uq_name UNIQUE ([name])",
            SupportedDatabase.MySql or SupportedDatabase.MariaDb or SupportedDatabase.TiDb =>
                "ALTER TABLE test_table ADD CONSTRAINT uq_name UNIQUE (name)",
            SupportedDatabase.Oracle =>
                string.Format("ALTER TABLE {0}test_table{1} ADD CONSTRAINT uq_name UNIQUE ({0}name{1})", qp, qs),
            SupportedDatabase.Firebird =>
                string.Format("ALTER TABLE {0}test_table{1} ADD CONSTRAINT uq_name UNIQUE ({0}name{1})", qp, qs),
            _ => null
        };

        if (sql != null)
        {
            try
            {
                await using var container = context.CreateSqlContainer(sql);
                await container.ExecuteNonQueryAsync();
            }
            catch (DatabaseException)
            {
                // Constraint might already exist, ignore
            }
        }
    }

    private async Task AddCheckConstraintAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var qp = context.QuotePrefix;
        var qs = context.QuoteSuffix;
        var sql = provider switch
        {
            SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb or SupportedDatabase.YugabyteDb =>
                "ALTER TABLE test_table ADD CONSTRAINT chk_value_positive CHECK (value >= 0)",
            SupportedDatabase.SqlServer =>
                "ALTER TABLE test_table ADD CONSTRAINT chk_value_positive CHECK (value >= 0)",
            SupportedDatabase.MySql or SupportedDatabase.MariaDb or SupportedDatabase.TiDb =>
                "ALTER TABLE test_table ADD CONSTRAINT chk_value_positive CHECK (value >= 0)",
            SupportedDatabase.Oracle =>
                string.Format("ALTER TABLE {0}test_table{1} ADD CONSTRAINT chk_value_positive CHECK ({0}value{1} >= 0)", qp, qs),
            SupportedDatabase.Firebird =>
                string.Format("ALTER TABLE {0}test_table{1} ADD CONSTRAINT chk_value_positive CHECK ({0}value{1} >= 0)", qp, qs),
            _ => null
        };

        if (sql != null)
        {
            try
            {
                await using var container = context.CreateSqlContainer(sql);
                await container.ExecuteNonQueryAsync();
            }
            catch (DatabaseException)
            {
                // Constraint might already exist, ignore
            }
        }
    }

    private static DbException ExtractInnerDbException(DatabaseException exception)
    {
        return Assert.IsAssignableFrom<DbException>(exception.InnerException);
    }

    private static async Task<DatabaseException> CaptureDatabaseExceptionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (DatabaseException ex)
        {
            return ex;
        }

        throw new Xunit.Sdk.XunitException("Expected a DatabaseException to be thrown.");
    }
}
