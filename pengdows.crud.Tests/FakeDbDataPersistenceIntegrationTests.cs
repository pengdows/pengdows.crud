#region

using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

/// <summary>
/// Integration tests for FakeDb data persistence functionality
/// </summary>
public class FakeDbDataPersistenceIntegrationTests
{
    [Fact]
    public async Task FakeDbConnection_WithDataPersistence_ShouldPersistInsertedData()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.EnableDataPersistence = true;

        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
        var typeMap = new TypeMapRegistry();

        // Act - Create table and insert data
        using var createTableContainer = context.CreateSqlContainer("CREATE TABLE TestUsers (Id INTEGER PRIMARY KEY, Name TEXT)");
        await createTableContainer.ExecuteNonQueryAsync();

        using var insertContainer = context.CreateSqlContainer("INSERT INTO TestUsers (Name) VALUES (@name)");
        insertContainer.AddParameterWithValue("@name", DbType.String, "John Doe");
        var insertResult = await insertContainer.ExecuteNonQueryAsync();

        // Assert insert worked
        Assert.Equal(1, insertResult);

        // Act - Query the persisted data
        using var selectContainer = context.CreateSqlContainer("SELECT * FROM TestUsers WHERE Name = @name");
        selectContainer.AddParameterWithValue("@name", DbType.String, "John Doe");

        using var reader = await selectContainer.ExecuteReaderAsync();
        var hasData = await reader.ReadAsync();

        // Assert data was persisted and retrieved
        Assert.True(hasData);
        Assert.Equal("John Doe", reader["Name"]);
    }

    [Fact]
    public async Task FakeDbConnection_WithoutDataPersistence_ShouldNotPersistData()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.EnableDataPersistence = false; // Explicitly disabled

        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);

        // Act - Create table and insert data
        using var createTableContainer = context.CreateSqlContainer("CREATE TABLE TestUsers (Id INTEGER PRIMARY KEY, Name TEXT)");
        await createTableContainer.ExecuteNonQueryAsync();

        using var insertContainer = context.CreateSqlContainer("INSERT INTO TestUsers (Name) VALUES (@name)");
        insertContainer.AddParameterWithValue("@name", DbType.String, "John Doe");
        var insertResult = await insertContainer.ExecuteNonQueryAsync();

        // Assert insert appeared to work (returns 1)
        Assert.Equal(1, insertResult);

        // Act - Try to query the data (should not be persisted)
        using var selectContainer = context.CreateSqlContainer("SELECT * FROM TestUsers WHERE Name = @name");
        selectContainer.AddParameterWithValue("@name", DbType.String, "John Doe");

        using var reader = await selectContainer.ExecuteReaderAsync();
        var hasData = await reader.ReadAsync();

        // Assert data was not persisted (default FakeDb behavior)
        Assert.False(hasData);
    }

    [Fact]
    public async Task EntityHelper_WithDataPersistence_ShouldPersistEntityOperations()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.EnableDataPersistence = true;

        var typeMap = new TypeMapRegistry();
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory, typeMap);
        var helper = new EntityHelper<TestUser, int>(context);

        // Create table first
        using var createTableContainer = context.CreateSqlContainer(@"
            CREATE TABLE TestUsers (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                Email TEXT
            )");
        await createTableContainer.ExecuteNonQueryAsync();

        // Act - Create an entity
        var user = new TestUser { Id = 1, Name = "Jane Smith", Email = "jane@example.com" };
        var createResult = await helper.CreateAsync(user, context);
        Assert.True(createResult);

        // Act - Retrieve the entity using helper
        var retrievedUser = await helper.RetrieveOneAsync(1, context);

        // Assert entity was persisted and retrieved correctly
        Assert.NotNull(retrievedUser);
        Assert.Equal("Jane Smith", retrievedUser.Name);
        Assert.Equal("jane@example.com", retrievedUser.Email);
    }

    [Fact]
    public async Task FakeDbConnection_UpdateOperations_ShouldModifyPersistedData()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.EnableDataPersistence = true;

        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);

        // Setup - Create table and insert initial data
        using var createTableContainer = context.CreateSqlContainer("CREATE TABLE TestUsers (Id INTEGER PRIMARY KEY, Name TEXT, Status TEXT)");
        await createTableContainer.ExecuteNonQueryAsync();

        using var insertContainer = context.CreateSqlContainer("INSERT INTO TestUsers (Id, Name, Status) VALUES (@id, @name, @status)");
        insertContainer.AddParameterWithValue("@id", DbType.Int32, 1);
        insertContainer.AddParameterWithValue("@name", DbType.String, "John Doe");
        insertContainer.AddParameterWithValue("@status", DbType.String, "Active");
        await insertContainer.ExecuteNonQueryAsync();

        // Act - Update the data
        using var updateContainer = context.CreateSqlContainer("UPDATE TestUsers SET Status = @newStatus WHERE Id = @id");
        updateContainer.AddParameterWithValue("@newStatus", DbType.String, "Inactive");
        updateContainer.AddParameterWithValue("@id", DbType.Int32, 1);
        var updateResult = await updateContainer.ExecuteNonQueryAsync();

        Assert.Equal(1, updateResult);

        // Act - Verify the update persisted
        using var selectContainer = context.CreateSqlContainer("SELECT Status FROM TestUsers WHERE Id = @id");
        selectContainer.AddParameterWithValue("@id", DbType.Int32, 1);
        var status = await selectContainer.ExecuteScalarAsync<string>();

        // Assert
        Assert.Equal("Inactive", status);
    }

    [Fact]
    public async Task FakeDbConnection_DeleteOperations_ShouldRemovePersistedData()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.EnableDataPersistence = true;

        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);

        // Setup - Create table and insert data
        using var createTableContainer = context.CreateSqlContainer("CREATE TABLE TestUsers (Id INTEGER PRIMARY KEY, Name TEXT)");
        await createTableContainer.ExecuteNonQueryAsync();

        using var insertContainer = context.CreateSqlContainer("INSERT INTO TestUsers (Id, Name) VALUES (@id, @name)");
        insertContainer.AddParameterWithValue("@id", DbType.Int32, 1);
        insertContainer.AddParameterWithValue("@name", DbType.String, "John Doe");
        await insertContainer.ExecuteNonQueryAsync();

        // Verify data exists
        using var countBeforeContainer = context.CreateSqlContainer("SELECT COUNT(*) FROM TestUsers");
        var countBefore = await countBeforeContainer.ExecuteScalarAsync<long>();
        Assert.Equal(1L, countBefore);

        // Act - Delete the data
        using var deleteContainer = context.CreateSqlContainer("DELETE FROM TestUsers WHERE Id = @id");
        deleteContainer.AddParameterWithValue("@id", DbType.Int32, 1);
        var deleteResult = await deleteContainer.ExecuteNonQueryAsync();

        Assert.Equal(1, deleteResult);

        // Act - Verify deletion persisted
        using var countAfterContainer = context.CreateSqlContainer("SELECT COUNT(*) FROM TestUsers");
        var countAfter = await countAfterContainer.ExecuteScalarAsync<long>();

        // Assert
        Assert.Equal(0L, countAfter);
    }

    [Fact]
    public async Task FakeDbConnection_QueuedResultsTakePrecedence_OverDataPersistence()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.EnableDataPersistence = true;

        var connection = (fakeDbConnection)factory.CreateConnection();
        connection.EnableDataPersistence = true;

        // Queue a specific scalar result
        connection.EnqueueScalarResult(42);

        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);

        // Setup data persistence
        using var createTableContainer = context.CreateSqlContainer("CREATE TABLE TestTable (Value INTEGER)");
        await createTableContainer.ExecuteNonQueryAsync();

        using var insertContainer = context.CreateSqlContainer("INSERT INTO TestTable (Value) VALUES (100)");
        await insertContainer.ExecuteNonQueryAsync();

        // Act - Execute scalar query (should return queued result, not persisted data)
        using var selectContainer = context.CreateSqlContainer("SELECT Value FROM TestTable");
        var result = await selectContainer.ExecuteScalarAsync<int>();

        // Assert - Queued result takes precedence
        Assert.Equal(42, result);
    }
}

[Table("TestUsers")]
public class TestUser
{
    [Id]
    [Column("Id", DbType.Int32)]
    public int Id { get; set; }

    [Column("Name", DbType.String)]
    public string Name { get; set; } = string.Empty;

    [Column("Email", DbType.String)]
    public string? Email { get; set; }
}