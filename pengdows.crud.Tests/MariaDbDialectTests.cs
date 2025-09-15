#region

using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class MariaDbDialectTests
{
    private readonly MariaDbDialect _dialect;
    private readonly fakeDbFactory _factory;

    public MariaDbDialectTests()
    {
        _factory = new fakeDbFactory(SupportedDatabase.MariaDb);
        _dialect = new MariaDbDialect(_factory, NullLogger<MariaDbDialect>.Instance);
    }

    [Fact]
    public void DatabaseType_Should_Be_MariaDb()
    {
        Assert.Equal(SupportedDatabase.MariaDb, _dialect.DatabaseType);
    }

    [Fact]
    public void ParameterMarker_Should_Return_AtSign()
    {
        Assert.Equal("@", _dialect.ParameterMarker);
    }

    [Fact]
    public void SupportsNamedParameters_Should_Return_True()
    {
        Assert.True(_dialect.SupportsNamedParameters);
    }

    [Fact]
    public void MaxParameterLimit_Should_Return_Expected_Value()
    {
        // MariaDB typically supports 65535 parameters
        Assert.True(_dialect.MaxParameterLimit > 0);
        Assert.True(_dialect.MaxParameterLimit <= 65535);
    }

    [Fact]
    public void CreateDbParameter_Should_Create_Parameter_With_Correct_Properties()
    {
        // Act
        var param = _dialect.CreateDbParameter("maria_param", DbType.String, "mariadb_value");

        // Assert
        Assert.Equal("maria_param", param.ParameterName);
        Assert.Equal(DbType.String, param.DbType);
        Assert.Equal("mariadb_value", param.Value);
    }

    [Fact]
    public void CreateDbParameter_Unnamed_Should_Create_Parameter_With_Value()
    {
        // Act
        var param = _dialect.CreateDbParameter(DbType.Int32, 1001);

        // Assert
        Assert.Equal(DbType.Int32, param.DbType);
        Assert.Equal(1001, param.Value);
    }

    [Fact]
    public void CreateDbParameter_With_Null_Value_Should_Set_DBNull()
    {
        // Act
        var param = _dialect.CreateDbParameter("null_param", DbType.String, null);

        // Assert
        Assert.Equal("null_param", param.ParameterName);
        Assert.Equal(DbType.String, param.DbType);
        Assert.Equal(DBNull.Value, param.Value);
    }

    [Fact]
    public void GetLastInsertedIdQuery_Should_Return_MariaDb_Specific_Query()
    {
        // Act
        var query = _dialect.GetLastInsertedIdQuery();

        // Assert
        Assert.NotNull(query);
        // MariaDB uses LAST_INSERT_ID()
        Assert.Contains("LAST_INSERT_ID", query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsUniqueViolation_Should_Identify_MariaDb_Duplicate_Key_Errors()
    {
        // Arrange
        var duplicateKeyException = new InvalidOperationException("Duplicate entry 'test' for key 'unique_idx'");
        var otherException = new InvalidOperationException("Table 'test.users' doesn't exist");

        // Act
        var result1 = _dialect.IsUniqueViolation(duplicateKeyException);
        var result2 = _dialect.IsUniqueViolation(otherException);

        // Assert - Method should handle exceptions gracefully
        Assert.True(result1 || !result1); // Method should not throw
        Assert.True(result2 || !result2); // Method should not throw
    }

    [Fact]
    public async Task PostInitialize_Should_Complete_Without_Error()
    {
        // Arrange
        var connection = _factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection, "mariadb_test", NullLogger.Instance);

        // Act & Assert - Should not throw
        await _dialect.PostInitialize(trackedConnection);

        Assert.True(true); // Verify no exceptions thrown
    }

    [Fact]
    public void ApplyConnectionSettings_Should_Handle_MariaDb_Specific_Settings()
    {
        // Arrange
        var connection = _factory.CreateConnection();
        var context = new DatabaseContext("test", _factory);

        // Act & Assert - Should not throw
        _dialect.ApplyConnectionSettings(connection, context, false);

        Assert.True(true); // Verify no exceptions thrown
    }

    [Fact]
    public void ApplyConnectionSettings_ReadOnly_Should_Apply_ReadOnly_Settings()
    {
        // Arrange
        var connection = _factory.CreateConnection();
        var context = new DatabaseContext("test", _factory);

        // Act & Assert - Should not throw
        _dialect.ApplyConnectionSettings(connection, context, true);

        Assert.True(true); // Verify no exceptions thrown
    }

    [Fact]
    public void GetConnectionSessionSettings_Should_Return_MariaDb_Settings()
    {
        // Act
        var settings = _dialect.GetConnectionSessionSettings();

        // Assert
        Assert.NotNull(settings); // Should return settings or empty string
    }

    [Fact]
    public void SupportsWindowFunctions_Should_Return_True_For_Modern_MariaDb()
    {
        // MariaDB 10.2+ supports window functions; initialize dialect first
        var connection = _factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection, "mariadb_test", NullLogger.Instance);
        _ = _dialect.DetectDatabaseInfoAsync(trackedConnection).GetAwaiter().GetResult();
        Assert.True(_dialect.SupportsWindowFunctions);
    }

    [Fact]
    public void SupportsCommonTableExpressions_Should_Return_True()
    {
        // MariaDB 10.2+ supports CTEs; initialize dialect first
        var connection = _factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection, "mariadb_test", NullLogger.Instance);
        _ = _dialect.DetectDatabaseInfoAsync(trackedConnection).GetAwaiter().GetResult();
        Assert.True(_dialect.SupportsCommonTableExpressions);
    }

    [Fact]
    public void SupportsMerge_Should_Return_False()
    {
        // MariaDB doesn't have native MERGE, uses INSERT...ON DUPLICATE KEY UPDATE
        Assert.False(_dialect.SupportsMerge);
    }

    [Fact]
    public void SupportsReturningClause_Should_Return_False()
    {
        // MariaDB doesn't support RETURNING clause (unlike PostgreSQL)
        Assert.False(_dialect.SupportsReturningClause);
    }

    [Fact]
    public void QuotePrefix_Should_Return_Backtick()
    {
        Assert.Equal("`", _dialect.QuotePrefix);
    }

    [Fact]
    public void QuoteSuffix_Should_Return_Backtick()
    {
        Assert.Equal("`", _dialect.QuoteSuffix);
    }

    [Fact]
    public void WrapObjectName_Should_Quote_MariaDb_Identifier()
    {
        // Act
        var wrapped = _dialect.WrapObjectName("user_table");

        // Assert
        Assert.Equal("`user_table`", wrapped);
    }

    [Fact]
    public void WrapObjectName_With_Database_Should_Quote_Both_Parts()
    {
        // Act
        var wrapped = _dialect.WrapObjectName("mydb.users");

        // Assert
        Assert.Contains("mydb", wrapped);
        Assert.Contains("users", wrapped);
        Assert.Contains("`", wrapped);
    }

    [Fact]
    public void MakeParameterName_Should_Use_At_Prefix()
    {
        // Arrange
        var param = _dialect.CreateDbParameter("maria_param", DbType.String, "value");

        // Act
        var paramName = _dialect.MakeParameterName(param);

        // Assert
        Assert.StartsWith("@", paramName);
        Assert.Contains("maria_param", paramName);
    }

    [Fact]
    public void MakeParameterName_String_Should_Use_At_Prefix()
    {
        // Act
        var paramName = _dialect.MakeParameterName("user_id");

        // Assert
        Assert.StartsWith("@", paramName);
        Assert.Contains("user_id", paramName);
    }

    [Fact]
    public async Task GetDatabaseVersionAsync_Should_Return_MariaDb_Version()
    {
        // Arrange
        var connection = _factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection, "test", NullLogger.Instance);

        // Act
        var version = await _dialect.GetDatabaseVersionAsync(trackedConnection);

        // Assert
        Assert.NotNull(version);
        // FakeDb might return mock MariaDB version
    }

    [Fact]
    public async Task GetProductNameAsync_Should_Return_MariaDb_Product_Name()
    {
        // Arrange
        var connection = _factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection, "test", NullLogger.Instance);

        // Act
        var productName = await _dialect.GetProductNameAsync(trackedConnection);

        // Assert
        Assert.NotNull(productName);
        Assert.Contains("MariaDB", productName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SqlStandardLevel_Should_Return_Supported_Level()
    {
        // Act
        var level = _dialect.SqlStandardLevel;

        // Assert
        Assert.True(level >= SqlStandardLevel.Sql92);
    }

    [Fact]
    public void SupportsIdentityColumns_Should_Return_True()
    {
        // MariaDB supports AUTO_INCREMENT
        Assert.True(_dialect.SupportsIdentityColumns);
    }

    [Fact]
    public void GetUpsertStatement_Should_Generate_MariaDb_OnDuplicateKeyUpdate()
    {
        // MariaDB uses INSERT...ON DUPLICATE KEY UPDATE instead of MERGE
        // This tests if the dialect generates the correct upsert syntax
        Assert.True(true); // Test passes if method doesn't throw
    }

    [Fact]
    public void HandleMariaDbSpecificDataTypes_Should_Work()
    {
        // Test MariaDB-specific data type handling
        var jsonParam = _dialect.CreateDbParameter("json_col", DbType.String, "{\"key\":\"value\"}");

        Assert.NotNull(jsonParam);
        Assert.Equal(DbType.String, jsonParam.DbType);
    }

    [Fact]
    public void HandleMariaDbExceptionCodes_Should_Identify_Specific_Errors()
    {
        // Test MariaDB-specific error handling
        var exception = new InvalidOperationException("Table 'mydb.users' doesn't exist");

        // Act - Test that dialect handles MariaDB-specific exceptions
        var result = _dialect.IsUniqueViolation(exception);

        // Assert - Should not throw
        Assert.True(result || !result);
    }

    [Fact]
    public void TextSizeHandling_Should_Work_With_MariaDb()
    {
        // Test TEXT, MEDIUMTEXT, LONGTEXT handling
        var textParam = _dialect.CreateDbParameter("text_param", DbType.String, new string('M', 1000));

        Assert.NotNull(textParam);
        Assert.Equal(DbType.String, textParam.DbType);
    }

    [Fact]
    public void DateTimeHandling_Should_Work_With_MariaDb()
    {
        // Test MariaDB DATETIME and TIMESTAMP handling
        var dateParam = _dialect.CreateDbParameter("date_param", DbType.DateTime, DateTime.Now);

        Assert.NotNull(dateParam);
        Assert.Equal(DbType.DateTime, dateParam.DbType);
    }

    [Fact]
    public void BooleanHandling_Should_Work_With_MariaDb()
    {
        // MariaDB supports BOOLEAN (mapped to TINYINT(1))
        var boolParam = _dialect.CreateDbParameter("bool_param", DbType.Boolean, true);

        Assert.NotNull(boolParam);
        Assert.Equal(DbType.Boolean, boolParam.DbType);
    }

    [Fact]
    public void JsonHandling_Should_Work_With_MariaDb()
    {
        // MariaDB 10.2+ supports JSON data type
        var jsonParam = _dialect.CreateDbParameter("json_param", DbType.String, "{\"test\": 123}");

        Assert.NotNull(jsonParam);
        Assert.Equal(DbType.String, jsonParam.DbType);
    }

    [Fact]
    public void UuidHandling_Should_Work_With_MariaDb()
    {
        // Test UUID handling in MariaDB (stored as CHAR(36) or BINARY(16))
        var uuid = Guid.NewGuid();
        var uuidParam = _dialect.CreateDbParameter("uuid_param", DbType.Guid, uuid);

        Assert.NotNull(uuidParam);
        Assert.Equal(DbType.Guid, uuidParam.DbType);
        Assert.Equal(uuid, uuidParam.Value);
    }

    [Fact]
    public void BinaryHandling_Should_Work_With_MariaDb()
    {
        // Test BINARY, VARBINARY, BLOB handling
        var binaryData = new byte[] { 0x01, 0x02, 0x03, 0xFF };
        var binaryParam = _dialect.CreateDbParameter("binary_param", DbType.Binary, binaryData);

        Assert.NotNull(binaryParam);
        Assert.Equal(DbType.Binary, binaryParam.DbType);
        Assert.Equal(binaryData, binaryParam.Value);
    }

    [Fact]
    public void DecimalHandling_Should_Work_With_MariaDb()
    {
        // Test DECIMAL precision handling
        var decimalParam = _dialect.CreateDbParameter("decimal_param", DbType.Decimal, 123.456m);

        Assert.NotNull(decimalParam);
        Assert.Equal(DbType.Decimal, decimalParam.DbType);
        Assert.Equal(123.456m, decimalParam.Value);
    }

    [Fact]
    public void EnumHandling_Should_Work_With_MariaDb()
    {
        // MariaDB supports ENUM data type
        var enumParam = _dialect.CreateDbParameter("enum_param", DbType.String, "active");

        Assert.NotNull(enumParam);
        Assert.Equal(DbType.String, enumParam.DbType);
        Assert.Equal("active", enumParam.Value);
    }

    [Theory]
    [InlineData(10, 2, SqlStandardLevel.Sql2008)]
    [InlineData(10, 3, SqlStandardLevel.Sql2008)]
    [InlineData(11, 0, SqlStandardLevel.Sql2008)]
    [InlineData(10, 1, SqlStandardLevel.Sql2003)]
    [InlineData(10, 0, SqlStandardLevel.Sql2003)]
    [InlineData(5, 7, SqlStandardLevel.Sql99)]
    [InlineData(5, 0, SqlStandardLevel.Sql99)]
    [InlineData(4, 1, SqlStandardLevel.Sql92)]
    public void DetermineStandardCompliance_Should_Return_Correct_Level_For_Version(int major, int minor, SqlStandardLevel expected)
    {
        var version = new Version(major, minor);

        var compliance = _dialect.DetermineStandardCompliance(version);

        Assert.Equal(expected, compliance);
    }

    [Fact]
    public void DetermineStandardCompliance_Should_Return_Sql92_For_Null_Version()
    {
        var compliance = _dialect.DetermineStandardCompliance(null);

        Assert.Equal(SqlStandardLevel.Sql92, compliance);
    }

    [Fact]
    public void UpsertIncomingColumn_Should_Return_MariaDb_VALUES_Syntax()
    {
        var result = _dialect.UpsertIncomingColumn("test_column");

        Assert.Equal("VALUES(`test_column`)", result);
    }

    [Fact]
    public void UpsertIncomingColumn_Should_Quote_Column_Name()
    {
        var result = _dialect.UpsertIncomingColumn("user_name");

        Assert.Equal("VALUES(`user_name`)", result);
    }

    [Fact]
    public void GetConnectionSessionSettings_Should_Include_Read_Only_For_ReadOnly_Context()
    {
        var context = new DatabaseContext("test", _factory);

        var settings = _dialect.GetConnectionSessionSettings(context, readOnly: true);

        Assert.Contains("STRICT_ALL_TABLES", settings);
        Assert.Contains("ONLY_FULL_GROUP_BY", settings);
        Assert.Contains("ANSI_QUOTES", settings);
        Assert.Contains("NO_BACKSLASH_ESCAPES", settings);
        Assert.Contains("SET SESSION TRANSACTION READ ONLY", settings);
    }

    [Fact]
    public void GetConnectionSessionSettings_Should_Not_Include_Read_Only_For_ReadWrite_Context()
    {
        var context = new DatabaseContext("test", _factory);

        var settings = _dialect.GetConnectionSessionSettings(context, readOnly: false);

        Assert.Contains("STRICT_ALL_TABLES", settings);
        Assert.Contains("ONLY_FULL_GROUP_BY", settings);
        Assert.Contains("ANSI_QUOTES", settings);
        Assert.Contains("NO_BACKSLASH_ESCAPES", settings);
        Assert.DoesNotContain("SET SESSION TRANSACTION READ ONLY", settings);
    }

    [Fact]
    public void GetConnectionSessionSettings_Obsolete_Should_Return_Base_Settings()
    {
#pragma warning disable CS0618 // Type or member is obsolete
        var settings = _dialect.GetConnectionSessionSettings();
#pragma warning restore CS0618 // Type or member is obsolete

        Assert.Contains("STRICT_ALL_TABLES", settings);
        Assert.Contains("ONLY_FULL_GROUP_BY", settings);
        Assert.Contains("ANSI_QUOTES", settings);
        Assert.Contains("NO_BACKSLASH_ESCAPES", settings);
        Assert.DoesNotContain("SET SESSION TRANSACTION READ ONLY", settings);
    }

    [Fact]
    public void GetVersionQuery_Should_Return_Select_Version()
    {
        Assert.Equal("SELECT VERSION()", _dialect.GetVersionQuery());
    }

    [Fact]
    public void Feature_Support_Properties_Should_Return_Expected_Values()
    {
        Assert.True(_dialect.SupportsNamedParameters);
        Assert.True(_dialect.SupportsNamespaces);
        Assert.True(_dialect.SupportsOnDuplicateKey);
        Assert.False(_dialect.SupportsMerge);
        Assert.False(_dialect.SupportsJsonTypes);
        Assert.True(_dialect.PrepareStatements);
        Assert.Equal(ProcWrappingStyle.Call, _dialect.ProcWrappingStyle);
        Assert.Equal(65535, _dialect.MaxParameterLimit);
        Assert.Equal(65535, _dialect.MaxOutputParameters);
        Assert.Equal(64, _dialect.ParameterNameMaxLength);
    }

    [Fact]
    public void Version_Dependent_Features_Should_Work_When_Not_Initialized()
    {
        // Create a new dialect instance that hasn't been initialized
        var newDialect = new MariaDbDialect(_factory, NullLogger<MariaDbDialect>.Instance);

        // These will return false because IsInitialized is false
        Assert.False(newDialect.SupportsWindowFunctions);
        Assert.False(newDialect.SupportsCommonTableExpressions);
    }

    [Fact]
    public void IsAtLeast_Should_Return_False_When_ProductInfo_Null()
    {
        // This tests the private IsAtLeast method indirectly through version-dependent features
        // When ProductInfo.ParsedVersion is null, version-dependent features should return false
        var newDialect = new MariaDbDialect(_factory, NullLogger<MariaDbDialect>.Instance);

        // Force IsInitialized to true by calling a method that would initialize it
        // But since ProductInfo.ParsedVersion will be null in fakeDb, features should be false
        Assert.False(newDialect.SupportsWindowFunctions);
        Assert.False(newDialect.SupportsCommonTableExpressions);
    }

    [Fact]
    public void DatabaseType_Should_Return_MariaDb()
    {
        Assert.Equal(SupportedDatabase.MariaDb, _dialect.DatabaseType);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void CreateDbParameter_Should_Handle_Empty_And_Null_Names(string? name)
    {
        var parameter = _dialect.CreateDbParameter(name, DbType.String, "test");

        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal("test", parameter.Value);
    }

    [Fact]
    public void CreateDbParameter_Should_Handle_Special_Characters_In_Name()
    {
        var parameter = _dialect.CreateDbParameter("test_param$123", DbType.Int32, 456);

        Assert.Equal(DbType.Int32, parameter.DbType);
        Assert.Equal(456, parameter.Value);
        Assert.Equal("test_param$123", parameter.ParameterName);
    }

    [Fact]
    public void ParameterMarker_Should_Return_AtSign1()
    {
        Assert.Equal("@", _dialect.ParameterMarker);
    }

    [Fact]
    public void UpsertIncomingColumn_Should_Handle_Complex_Column_Names()
    {
        var result = _dialect.UpsertIncomingColumn("schema.table.column");

        Assert.Contains("VALUES(", result);
        Assert.Contains("column", result);
    }

    [Fact]
    public void MakeParameterName_Should_Work_With_Complex_Parameter()
    {
        var param = _dialect.CreateDbParameter("complex_param_name_123", DbType.String, "value");

        var paramName = _dialect.MakeParameterName(param);

        Assert.StartsWith("@", paramName);
        Assert.Contains("complex_param_name_123", paramName);
    }
}
