using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for INSERT identity population across different scenarios and dialects.
/// Includes SQL Server OUTPUT clause positioning (regression test for GitHub issue #137).
/// </summary>
#pragma warning disable CS0618 // EntityHelper is obsolete
public class SqlServerIdentityOutputClauseTests
{
    private readonly ITypeMapRegistry _typeMap;

    public SqlServerIdentityOutputClauseTests()
    {
        _typeMap = new TypeMapRegistry();
        _typeMap.Register<UserInfoEntity>();
        _typeMap.Register<TestEntityWithAutoId>();
        _typeMap.Register<TestEntityWithWritableId>();
        _typeMap.Register<TestEntityWithGuidId>();
    }

    /// <summary>
    /// Entity matching the user's reported schema: INT IDENTITY pseudo key + varchar primary key.
    /// </summary>
    [Table("user_info_temp")]
    public class UserInfoEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey]
        [Column("user_id", DbType.String)]
        public string Username { get; set; } = string.Empty;

        [Column("user_pass", DbType.String)]
        public string? Password { get; set; }

        [Column("role", DbType.String)]
        public string? Role { get; set; }

        [Column("mobile", DbType.String)]
        public string? Mobile { get; set; }

        [Column("daily_update", DbType.Boolean)]
        public bool? IsDailyUpdate { get; set; }

        [Column("active", DbType.Boolean)]
        public bool? IsActive { get; set; }

        [Column("login_alert", DbType.Boolean)]
        public bool? IsLoginAlert { get; set; }

        [Column("receive_otp", DbType.Boolean)]
        public bool? IsOtpReceived { get; set; }
    }

    [Fact]
    public void BuildCreateWithReturning_SqlServer_IntIdentity_GeneratesValidOutputClause()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory, _typeMap);
        var helper = new EntityHelper<UserInfoEntity, int>(context);

        var entity = new UserInfoEntity
        {
            Username = "testuser",
            Password = "hashedpass",
            Role = "Admin",
            Mobile = "1234567890",
            IsDailyUpdate = false,
            IsActive = true,
            IsLoginAlert = false,
            IsOtpReceived = false
        };

        // Act
        var container = helper.BuildCreateWithReturning(entity, true, context);
        var sql = container.Query.ToString();

        // Assert - Verify SQL structure
        // The SQL should be: INSERT INTO "table" (columns) OUTPUT INSERTED."id" VALUES (params)
        Assert.Contains("INSERT INTO", sql);
        Assert.Contains("OUTPUT INSERTED.", sql);
        Assert.Contains("VALUES", sql);

        // Verify OUTPUT is positioned BEFORE VALUES (SQL Server requirement)
        var outputIndex = sql.IndexOf("OUTPUT INSERTED.");
        var valuesIndex = sql.IndexOf("VALUES");
        Assert.True(outputIndex > 0, "OUTPUT clause should be present");
        Assert.True(valuesIndex > 0, "VALUES clause should be present");
        Assert.True(outputIndex < valuesIndex, $"OUTPUT clause must come BEFORE VALUES. SQL: {sql}");

        // Verify the id column is NOT in the insert columns (since it's [Id(false)])
        // The column list is between "(" and ")" before VALUES
        var firstParenIndex = sql.IndexOf('(');
        var closingParenBeforeValues = sql.IndexOf(')', firstParenIndex);
        var columnList = sql.Substring(firstParenIndex + 1, closingParenBeforeValues - firstParenIndex - 1);

        // id column should NOT be in the column list
        Assert.DoesNotContain("\"id\"", columnList);

        // user_id column SHOULD be in the column list
        Assert.Contains("\"user_id\"", columnList);
    }

    [Fact]
    public void BuildCreate_SqlServer_IntIdentity_ExcludesIdFromInsertColumns()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory, _typeMap);
        var helper = new EntityHelper<UserInfoEntity, int>(context);

        var entity = new UserInfoEntity
        {
            Username = "testuser",
            Password = "hashedpass",
            Role = "Admin"
        };

        // Act
        var container = helper.BuildCreate(entity, context);
        var sql = container.Query.ToString();

        // Assert
        Assert.Contains("INSERT INTO", sql);
        Assert.Contains("VALUES", sql);

        // The id column should NOT be in the INSERT - it's [Id(false)]
        Assert.DoesNotContain("OUTPUT", sql); // BuildCreate doesn't add OUTPUT

        // Verify column list doesn't have id
        var firstParenIndex = sql.IndexOf('(');
        var closingParenBeforeValues = sql.IndexOf(')', firstParenIndex);
        var columnList = sql.Substring(firstParenIndex + 1, closingParenBeforeValues - firstParenIndex - 1);
        Assert.DoesNotContain("\"id\"", columnList);
    }

    [Fact]
    public async Task CreateAsync_SqlServer_IntIdentity_PopulatesGeneratedId()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        factory.SetIdPopulationResult(42, 1); // Set the generated ID to return

        var context = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory, _typeMap);
        var helper = new EntityHelper<UserInfoEntity, int>(context);

        var entity = new UserInfoEntity
        {
            Username = "testuser",
            Password = "hashedpass",
            Role = "Admin",
            IsActive = true
        };

        // Act
        var result = await helper.CreateAsync(entity, context);

        // Assert
        Assert.True(result);
        Assert.Equal(42, entity.Id); // Generated ID should be populated
    }

    [Fact]
    public void SqlServerDialect_InsertReturningClauseBeforeValues_IsTrue()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory);
        var dialect = context.Dialect;

        // Assert
        Assert.True(dialect.SupportsInsertReturning);
        Assert.True(dialect.InsertReturningClauseBeforeValues);
    }

    [Fact]
    public void BuildCreateWithReturning_SqlServer_ValidSqlSyntax()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory, _typeMap);
        var helper = new EntityHelper<UserInfoEntity, int>(context);

        var entity = new UserInfoEntity
        {
            Username = "testuser",
            Password = "hashedpass",
            Role = "Admin"
        };

        // Act
        var container = helper.BuildCreateWithReturning(entity, true, context);
        var sql = container.Query.ToString();

        // Assert - The SQL should follow this pattern:
        // INSERT INTO "table" (col1, col2, ...) OUTPUT INSERTED."id" VALUES (@p1, @p2, ...)

        // Split and verify structure
        var parts = sql.Split(new[] { " OUTPUT INSERTED." }, StringSplitOptions.None);
        Assert.Equal(2, parts.Length); // Should split into exactly 2 parts

        var beforeOutput = parts[0];
        var afterOutput = parts[1];

        // Before OUTPUT should end with closing paren of column list
        Assert.True(beforeOutput.TrimEnd().EndsWith(")"),
            $"Before OUTPUT should end with ')'. Got: {beforeOutput}");

        // After OUTPUT should start with the id column name and then VALUES
        Assert.Contains("VALUES", afterOutput);
    }

    // ============================================================================
    // Broader ID Population Tests (ported from 1.0 EntityHelperIdPopulationTests)
    // ============================================================================

    [Fact]
    public async Task CreateAsync_Should_Populate_Generated_Id_For_Auto_Increment_Column()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        factory.SetIdPopulationResult(42, rowsAffected: 1);

        var context = new DatabaseContext("test", factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithAutoId, int>(context);
        var entity = new TestEntityWithAutoId { Name = "Test Entity" };

        // Act
        var result = await helper.CreateAsync(entity);

        // Assert
        Assert.True(result);
        Assert.Equal(42, entity.Id);
    }

    [Fact]
    public async Task CreateAsync_Should_Not_Populate_Id_For_Writable_Id_Column()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var context = new DatabaseContext("test", factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithWritableId, int>(context);

        factory.SetNonQueryResult(1);

        var entity = new TestEntityWithWritableId { Id = 100, Name = "Test Entity" };

        // Act
        var result = await helper.CreateAsync(entity);

        // Assert
        Assert.True(result);
        Assert.Equal(100, entity.Id); // ID should remain unchanged
    }

    [Fact]
    public void CreateAsync_Should_Throw_For_Entity_Without_Id_Column()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("test", factory, _typeMap);

        // Assert: constructing helper should fail due to missing [Id]/[PrimaryKey]
        Assert.Throws<InvalidOperationException>(() => new EntityHelper<TestEntityWithoutId, int>(context));
    }

    [Fact]
    public async Task CreateAsync_Should_Handle_Dialect_With_Returning_Populates_Id()
    {
        // Arrange - Use Sqlite which uses RETURNING clause
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithAutoId, int>(context);

        factory.SetNonQueryResult(1);
        factory.SetScalarResult(42);

        var entity = new TestEntityWithAutoId { Name = "Test Entity" };

        // Act
        var result = await helper.CreateAsync(entity);

        // Assert
        Assert.True(result);
        Assert.Equal(42, entity.Id);
    }

    [Fact]
    public async Task CreateAsync_Should_Handle_Null_Generated_Id_Result()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var context = new DatabaseContext("test", factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithAutoId, int>(context);

        factory.SetNonQueryResult(1);
        factory.SetScalarResult(null);

        var entity = new TestEntityWithAutoId { Name = "Test Entity" };

        // Act
        var result = await helper.CreateAsync(entity);

        // Assert
        Assert.True(result);
        Assert.Equal(0, entity.Id); // Should handle null gracefully
    }

    [Fact]
    public async Task CreateAsync_Should_Handle_Database_Exception_During_Id_Population()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        factory.SetException(new InvalidOperationException("Database connection lost"));

        var context = new DatabaseContext("test", factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithAutoId, int>(context);

        factory.SetNonQueryResult(1);

        var entity = new TestEntityWithAutoId { Name = "Test Entity" };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => helper.CreateAsync(entity));
    }

    [Fact]
    public async Task CreateAsync_Should_Not_Attempt_Id_Population_When_Insert_Fails()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Unknown);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Unknown", factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithAutoId, int>(context);

        factory.SetNonQueryResult(0); // Insert fails

        var entity = new TestEntityWithAutoId { Name = "Test Entity" };

        // Act
        var result = await helper.CreateAsync(entity);

        // Assert
        Assert.False(result);
        Assert.Equal(0, entity.Id);
    }

    [Fact]
    public async Task CreateAsync_Should_Handle_Multiple_Rows_Affected_Gracefully()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Unknown);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Unknown", factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithAutoId, int>(context);

        factory.SetNonQueryResult(2); // Multiple rows affected

        var entity = new TestEntityWithAutoId { Name = "Test Entity" };

        // Act
        var result = await helper.CreateAsync(entity);

        // Assert
        Assert.False(result);
        Assert.Equal(0, entity.Id);
    }

    [Fact]
    public void BuildCreateWithReturning_SqlServer_Places_Output_Before_Values()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithAutoId, int>(context);

        var sc = helper.BuildCreateWithReturning(new TestEntityWithAutoId { Name = "Test Entity" }, true, context);
        var sql = sc.Query.ToString();

        var outputIndex = sql.IndexOf(" OUTPUT INSERTED.", StringComparison.Ordinal);
        var valuesIndex = sql.IndexOf(" VALUES ", StringComparison.Ordinal);

        Assert.True(outputIndex > 0);
        Assert.True(valuesIndex > outputIndex);
    }

    [Fact]
    public void BuildCreateWithReturning_Sqlite_Appends_Returning_After_Values()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithAutoId, int>(context);

        var sc = helper.BuildCreateWithReturning(new TestEntityWithAutoId { Name = "Test Entity" }, true, context);
        var sql = sc.Query.ToString();

        var returningIndex = sql.IndexOf(" RETURNING ", StringComparison.Ordinal);
        var valuesIndex = sql.IndexOf(" VALUES ", StringComparison.Ordinal);

        Assert.True(returningIndex > 0);
        Assert.True(returningIndex > valuesIndex);
    }

    [Fact]
    public async Task CreateAsync_WithReturningGuidString_Populates_Guid_Id()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var guidString = Guid.NewGuid().ToString();
        factory.SetScalarResult(guidString);
        factory.SetNonQueryResult(1);

        var context = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithGuidId, Guid>(context);
        var entity = new TestEntityWithGuidId { Name = "Guid Entity" };

        var result = await helper.CreateAsync(entity);

        Assert.True(result);
        Assert.Equal(Guid.Parse(guidString), entity.Id);
    }

    [Fact]
    public async Task CreateAsync_WithCancellationToken_Uses_Returning_Value()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        factory.SetScalarResult(73);
        factory.SetNonQueryResult(1);

        var context = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithAutoId, int>(context);
        var entity = new TestEntityWithAutoId { Name = "Cancellation Test" };

        var result = await helper.CreateAsync(entity, context, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(73, entity.Id);
    }

    // ============================================================================
    // SQL Syntax Verification Tests - Verify correct OUTPUT/RETURNING syntax per dialect
    // ============================================================================

    [Theory]
    [InlineData(SupportedDatabase.SqlServer, "OUTPUT INSERTED.", true)]  // Before VALUES
    [InlineData(SupportedDatabase.PostgreSql, " RETURNING ", false)]     // After VALUES
    [InlineData(SupportedDatabase.Sqlite, " RETURNING ", false)]         // After VALUES
    [InlineData(SupportedDatabase.Firebird, " RETURNING ", false)]       // After VALUES
    public void BuildCreateWithReturning_GeneratesCorrectSyntaxForDialect(
        SupportedDatabase provider, string expectedClause, bool beforeValues)
    {
        var factory = new fakeDbFactory(provider);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={provider}", factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithAutoId, int>(context);

        var sc = helper.BuildCreateWithReturning(new TestEntityWithAutoId { Name = "Test" }, true, context);
        var sql = sc.Query.ToString();

        // Verify the expected clause is present
        Assert.Contains(expectedClause, sql);

        // Verify positioning relative to VALUES
        var clauseIndex = sql.IndexOf(expectedClause, StringComparison.Ordinal);
        var valuesIndex = sql.IndexOf("VALUES", StringComparison.Ordinal);

        if (beforeValues)
        {
            Assert.True(clauseIndex < valuesIndex,
                $"Expected {expectedClause} before VALUES for {provider}. SQL: {sql}");
        }
        else
        {
            Assert.True(clauseIndex > valuesIndex,
                $"Expected {expectedClause} after VALUES for {provider}. SQL: {sql}");
        }
    }

    [Theory]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.Unknown)]
    public void BuildCreateWithReturning_NoReturningClauseForUnsupportedDialects(SupportedDatabase provider)
    {
        var factory = new fakeDbFactory(provider);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={provider}", factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithAutoId, int>(context);

        var sc = helper.BuildCreateWithReturning(new TestEntityWithAutoId { Name = "Test" }, true, context);
        var sql = sc.Query.ToString();

        // Should NOT contain OUTPUT or RETURNING
        Assert.DoesNotContain("OUTPUT", sql);
        Assert.DoesNotContain("RETURNING", sql);

        // Should still be a valid INSERT statement
        Assert.Contains("INSERT INTO", sql);
        Assert.Contains("VALUES", sql);
    }

    [Fact]
    public void BuildCreate_NeverContainsReturningClause()
    {
        // BuildCreate (without Returning) should never have OUTPUT or RETURNING
        foreach (var provider in new[] { SupportedDatabase.SqlServer, SupportedDatabase.PostgreSql,
            SupportedDatabase.Sqlite, SupportedDatabase.MySql })
        {
            var factory = new fakeDbFactory(provider);
            var context = new DatabaseContext($"Data Source=test;EmulatedProduct={provider}", factory, _typeMap);
            var helper = new EntityHelper<TestEntityWithAutoId, int>(context);

            var sc = helper.BuildCreate(new TestEntityWithAutoId { Name = "Test" }, context);
            var sql = sc.Query.ToString();

            Assert.DoesNotContain("OUTPUT", sql);
            Assert.DoesNotContain("RETURNING", sql);
        }
    }

    // ============================================================================
    // Fallback Behavior Tests - Databases without RETURNING support
    // ============================================================================

    [Fact]
    public async Task CreateAsync_MySql_InsertsWithoutReturningClause()
    {
        // MySQL doesn't support RETURNING - INSERT succeeds but ID is not populated via RETURNING
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        factory.SetNonQueryResult(1);

        var context = new DatabaseContext("Data Source=test;EmulatedProduct=MySql", factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithAutoId, int>(context);
        var entity = new TestEntityWithAutoId { Name = "MySQL Test" };

        var result = await helper.CreateAsync(entity, context);

        // INSERT should succeed
        Assert.True(result);
        // ID is NOT populated via RETURNING (MySQL doesn't support it)
        Assert.Equal(0, entity.Id);
    }

    [Fact]
    public async Task CreateAsync_Unknown_InsertsWithoutReturningClause()
    {
        // Unknown database - no RETURNING support
        var factory = new fakeDbFactory(SupportedDatabase.Unknown);
        factory.SetNonQueryResult(1);

        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Unknown", factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithAutoId, int>(context);
        var entity = new TestEntityWithAutoId { Name = "Unknown DB Test" };

        var result = await helper.CreateAsync(entity, context);

        // INSERT should succeed but ID is not populated
        Assert.True(result);
        Assert.Equal(0, entity.Id);
    }

    [Theory]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.Unknown)]
    public void Dialect_SupportsInsertReturning_IsFalseForUnsupportedDatabases(SupportedDatabase provider)
    {
        var factory = new fakeDbFactory(provider);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={provider}", factory, _typeMap);

        Assert.False(context.Dialect.SupportsInsertReturning);
    }

    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.Firebird)]
    public void Dialect_SupportsInsertReturning_IsTrueForSupportedDatabases(SupportedDatabase provider)
    {
        var factory = new fakeDbFactory(provider);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={provider}", factory, _typeMap);

        Assert.True(context.Dialect.SupportsInsertReturning);
    }

    // ============================================================================
    // Test Entities
    // ============================================================================

    [Table("test_auto_id")]
    public class TestEntityWithAutoId
    {
        [Id(writable: false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Table("test_guid_auto_id")]
    public class TestEntityWithGuidId
    {
        [Id(writable: false)]
        [Column("id", DbType.String)]
        public Guid Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Table("test_writable_id")]
    public class TestEntityWithWritableId
    {
        [Id(writable: true)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Table("test_no_id")]
    public class TestEntityWithoutId
    {
        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }
}
#pragma warning restore CS0618
