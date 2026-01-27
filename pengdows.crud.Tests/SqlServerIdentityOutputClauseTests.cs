using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for SQL Server INT IDENTITY with OUTPUT clause positioning.
/// Regression test for GitHub issue #137.
/// </summary>
#pragma warning disable CS0618 // EntityHelper is obsolete
public class SqlServerIdentityOutputClauseTests
{
    private readonly ITypeMapRegistry _typeMap;

    public SqlServerIdentityOutputClauseTests()
    {
        _typeMap = new TypeMapRegistry();
        _typeMap.Register<UserInfoEntity>();
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

    [Fact]
    public void InsertOutputClauseBeforeValues_InsertsAtCorrectPosition()
    {
        // Arrange - simulate typical INSERT statement
        var builder = new System.Text.StringBuilder("INSERT INTO \"t\" (\"c1\", \"c2\") VALUES (@p0, @p1)");
        var outputClause = " OUTPUT INSERTED.\"id\"";

        // Act
        EntityHelper<UserInfoEntity, int>.InsertOutputClauseBeforeValues(builder, outputClause);
        var result = builder.ToString();

        // Assert - OUTPUT should appear between column list and VALUES
        Assert.Contains("OUTPUT INSERTED.\"id\"", result);
        Assert.Contains("VALUES", result);

        // Verify order: closing paren, then OUTPUT, then VALUES
        var closeParenIndex = result.IndexOf(')');
        var outputIndex = result.IndexOf("OUTPUT");
        var valuesIndex = result.IndexOf("VALUES");

        Assert.True(closeParenIndex < outputIndex, "Closing paren must come before OUTPUT");
        Assert.True(outputIndex < valuesIndex, "OUTPUT must come before VALUES");

        // Verify the exact expected format
        Assert.Equal("INSERT INTO \"t\" (\"c1\", \"c2\") OUTPUT INSERTED.\"id\" VALUES (@p0, @p1)", result);
    }

    [Fact]
    public void InsertOutputClauseBeforeValues_WhenNoValuesFound_AppendsToEnd()
    {
        // Arrange - malformed SQL without VALUES
        var builder = new System.Text.StringBuilder("INSERT INTO \"t\" (\"c1\")");
        var outputClause = " OUTPUT INSERTED.\"id\"";

        // Act
        EntityHelper<UserInfoEntity, int>.InsertOutputClauseBeforeValues(builder, outputClause);
        var result = builder.ToString();

        // Assert - should append to end as fallback
        Assert.EndsWith(outputClause, result);
    }

    [Fact]
    public void InsertOutputClauseBeforeValues_WithEmptyClause_DoesNotModify()
    {
        // Arrange
        var original = "INSERT INTO \"t\" (\"c1\") VALUES (@p0)";
        var builder = new System.Text.StringBuilder(original);

        // Act
        EntityHelper<UserInfoEntity, int>.InsertOutputClauseBeforeValues(builder, "");
        var result = builder.ToString();

        // Assert - should be unchanged
        Assert.Equal(original, result);
    }

    [Fact]
    public void InsertOutputClauseBeforeValues_WithNullBuilder_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            EntityHelper<UserInfoEntity, int>.InsertOutputClauseBeforeValues(null!, " OUTPUT x"));
    }
}
#pragma warning restore CS0618
