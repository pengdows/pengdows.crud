using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for TableGateway internal utility methods.
/// These methods are internal and accessed directly via InternalsVisibleTo.
/// </summary>
[Collection("SqlLiteContext")]
public class TableGatewayUtilityMethodTests : SqlLiteContextTestBase
{
    public TableGatewayUtilityMethodTests()
    {
        TypeMap.Register<TestEntity>();
        TypeMap.Register<GuidEntity>();
    }

    #region BuildValueExtractor Tests

    [Fact]
    public void BuildValueExtractor_HandlesAllPrimitiveTypes()
    {
        // Test all TypeCode cases that are handled specially
        var typeCodeTests = new[]
        {
            typeof(int), // TypeCode.Int32
            typeof(long), // TypeCode.Int64
            typeof(string), // TypeCode.String
            typeof(DateTime), // TypeCode.DateTime
            typeof(decimal), // TypeCode.Decimal
            typeof(bool), // TypeCode.Boolean
            typeof(short), // TypeCode.Int16
            typeof(byte), // TypeCode.Byte
            typeof(double), // TypeCode.Double
            typeof(float), // TypeCode.Single
            typeof(Guid), // Special case - not a TypeCode
            typeof(byte[]), // Special case - byte array
            typeof(object) // Default case - fallback
        };

        foreach (var testType in typeCodeTests)
        {
            var extractor = TableGateway<TestEntity, int>.BuildValueExtractor(testType);
            Assert.NotNull(extractor);
        }
    }

    #endregion

    #region NormalizeDateTimeOffset Tests

    [Fact]
    public void NormalizeDateTimeOffset_FromDateTimeOffset_ReturnsUnchanged()
    {
        var testDto = new DateTimeOffset(2023, 1, 1, 12, 0, 0, TimeSpan.FromHours(5));

        var result = TableGateway<TestEntity, int>.NormalizeDateTimeOffset(testDto);

        Assert.Equal(testDto, result);
    }

    [Fact]
    public void NormalizeDateTimeOffset_FromUtcDateTime_CreatesZeroOffset()
    {
        var testDt = new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var result = TableGateway<TestEntity, int>.NormalizeDateTimeOffset(testDt);

        Assert.Equal(TimeSpan.Zero, result.Offset);
        Assert.Equal(testDt, result.UtcDateTime);
    }

    [Fact]
    public void NormalizeDateTimeOffset_FromLocalDateTime_PreservesOffset()
    {
        var testDt = new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Local);

        var result = TableGateway<TestEntity, int>.NormalizeDateTimeOffset(testDt);

        // Local DateTime should preserve the system's local offset
        var expectedOffset = TimeZoneInfo.Local.GetUtcOffset(testDt);
        Assert.Equal(expectedOffset, result.Offset);
    }

    [Fact]
    public void NormalizeDateTimeOffset_FromUnspecifiedDateTime_CreatesZeroOffset()
    {
        var testDt = new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);

        var result = TableGateway<TestEntity, int>.NormalizeDateTimeOffset(testDt);

        Assert.Equal(TimeSpan.Zero, result.Offset);
    }

    [Fact]
    public void NormalizeDateTimeOffset_FromValidString_ParsesCorrectly()
    {
        var testString = "2023-01-01T12:00:00+05:00";

        var result = TableGateway<TestEntity, int>.NormalizeDateTimeOffset(testString);

        Assert.Equal(new DateTimeOffset(2023, 1, 1, 12, 0, 0, TimeSpan.FromHours(5)), result);
    }

    [Fact]
    public void NormalizeDateTimeOffset_FromConvertibleValue_UsesConvert()
    {
        // Test with a string that Convert.ToDateTime can handle
        var testValue = "2023-01-01T12:00:00"; // DateTime string that Convert.ToDateTime can handle

        // This will exercise the default path that calls Convert.ToDateTime
        var result = TableGateway<TestEntity, int>.NormalizeDateTimeOffset(testValue);

        // Should have zero offset since it goes through NormalizeDateTime
        Assert.Equal(TimeSpan.Zero, result.Offset);
    }

    #endregion

    #region ValuesAreEqual Tests

    [Fact]
    public void ValuesAreEqual_BothNull_ReturnsTrue()
    {
        var result = TableGateway<TestEntity, int>.ValuesAreEqual(null, null, DbType.String);
        Assert.True(result);
    }

    [Fact]
    public void ValuesAreEqual_OneNull_ReturnsFalse()
    {
        var result1 = TableGateway<TestEntity, int>.ValuesAreEqual("test", null, DbType.String);
        Assert.False(result1);

        var result2 = TableGateway<TestEntity, int>.ValuesAreEqual(null, "test", DbType.String);
        Assert.False(result2);
    }

    [Fact]
    public void ValuesAreEqual_ByteArrays_ComparesSequence()
    {
        var bytes1 = new byte[] { 1, 2, 3 };
        var bytes2 = new byte[] { 1, 2, 3 };
        var bytes3 = new byte[] { 1, 2, 4 };

        var result1 = TableGateway<TestEntity, int>.ValuesAreEqual(bytes1, bytes2, DbType.Binary);
        Assert.True(result1);

        var result2 = TableGateway<TestEntity, int>.ValuesAreEqual(bytes1, bytes3, DbType.Binary);
        Assert.False(result2);
    }

    [Fact]
    public void ValuesAreEqual_DecimalTypes_UsesDecimalComparison()
    {
        // Test all decimal-related DbTypes
        var decimalTypes = new[] { DbType.Decimal, DbType.Currency, DbType.VarNumeric };

        foreach (var dbType in decimalTypes)
        {
            // Test equal decimals
            var result1 = TableGateway<TestEntity, int>.ValuesAreEqual(123.45m, 123.45d, dbType);
            Assert.True(result1);

            // Test unequal decimals
            var result2 = TableGateway<TestEntity, int>.ValuesAreEqual(123.45m, 123.46m, dbType);
            Assert.False(result2);
        }
    }

    [Fact]
    public void ValuesAreEqual_DateTimeTypes_UsesNormalizedComparison()
    {
        var dateTimeTypes = new[] { DbType.DateTime, DbType.DateTime2 };

        foreach (var dbType in dateTimeTypes)
        {
            // Test equivalent DateTimes with different kinds
            var utcTime = new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var localTime = utcTime.ToLocalTime();

            var result = TableGateway<TestEntity, int>.ValuesAreEqual(utcTime, localTime, dbType);
            Assert.True(result); // Should be equal after normalization
        }
    }

    [Fact]
    public void ValuesAreEqual_DateTimeOffsetType_ComparesUtcDateTime()
    {
        var dto1 = new DateTimeOffset(2023, 1, 1, 12, 0, 0, TimeSpan.FromHours(2));
        var dto2 = new DateTimeOffset(2023, 1, 1, 10, 0, 0, TimeSpan.Zero); // Same UTC time

        var result = TableGateway<TestEntity, int>.ValuesAreEqual(dto1, dto2, DbType.DateTimeOffset);
        Assert.True(result); // Should be equal as they represent the same UTC time
    }

    [Fact]
    public void ValuesAreEqual_DefaultCase_UsesEquals()
    {
        // Test default case with strings
        var result1 = TableGateway<TestEntity, int>.ValuesAreEqual("test", "test", DbType.String);
        Assert.True(result1);

        var result2 = TableGateway<TestEntity, int>.ValuesAreEqual("test1", "test2", DbType.String);
        Assert.False(result2);

        // Test with integers
        var result3 = TableGateway<TestEntity, int>.ValuesAreEqual(42, 42, DbType.Int32);
        Assert.True(result3);
    }

    #endregion

    #region CreateAsync Edge Cases

    [Fact]
    public async Task CreateAsync_WithGuidIdGeneration_ParsesStringGuid()
    {
        // Create a helper for entity with Guid ID to test Guid parsing logic
        TypeMap.Register<GuidEntity>();
        var guidHelper = new TableGateway<GuidEntity, Guid>(Context);

        // Build table for Guid entity
        var createTable = Context.CreateSqlContainer("""
                                                     CREATE TABLE GuidEntities (
                                                         Id TEXT PRIMARY KEY,
                                                         Name TEXT
                                                     )
                                                     """);
        await createTable.ExecuteNonQueryAsync();

        var entity = new GuidEntity { Name = "GuidTest" };

        // This will exercise the Guid.TryParse path in CreateAsync
        var result = await guidHelper.CreateAsync(entity, Context);

        Assert.True(result);
        Assert.NotEqual(Guid.Empty, entity.Id);
    }

    #endregion

    #region Test Entities

    [Table("GuidEntities")]
    public class GuidEntity
    {
        [Id(true)] // Allow writing so TableGateway can generate GUID
        [Column("id", DbType.String)]
        public Guid Id { get; set; }

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
    }

    #endregion
}
