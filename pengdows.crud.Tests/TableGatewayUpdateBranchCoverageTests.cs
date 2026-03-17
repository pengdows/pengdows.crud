using System;
using System.Data;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

[Collection("SqliteSerial")]
public class TableGatewayUpdateBranchCoverageTests : SqlLiteContextTestBase
{
    public TableGatewayUpdateBranchCoverageTests()
    {
        TypeMap.Register<TestEntity>();
        TypeMap.Register<NoIdEntity>();
    }

    [Fact]
    public async Task BuildUpdateAsync_WithoutIdColumn_ThrowsNotSupported()
    {
        var gateway = new TableGateway<NoIdEntity, string>(Context);
        var entity = new NoIdEntity { Key = "k1", Name = "n1" };

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => gateway.BuildUpdateAsync(entity, false, Context).AsTask());
        Assert.Contains("Id column", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildUpdateAsync_WithSql92Dialect_UsesPositionalParameters()
    {
        var unknownContext = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Unknown",
            new fakeDbFactory(SupportedDatabase.Unknown), TypeMap);
        await using var disposeUnknown = unknownContext;
        var gateway = new TableGateway<TestEntity, int>(unknownContext, AuditValueResolver);

        var entity = new TestEntity
        {
            Id = 1,
            Name = "updated",
            CreatedBy = "creator",
            CreatedOn = DateTime.UtcNow.AddDays(-1),
            LastUpdatedBy = "updater",
            LastUpdatedOn = DateTime.UtcNow,
            version = 3
        };

        var sql = await gateway.BuildUpdateAsync(entity, false, unknownContext);
        var text = sql.Query.ToString();

        Assert.Contains('?', text);
        Assert.Contains("Version", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalizeDateTimeOffset_ConvertibleObject_UsesDefaultConvertPath()
    {
        var source = new ConvertibleDateTime(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));

        var result = TableGateway<TestEntity, int>.NormalizeDateTimeOffset(source);

        Assert.Equal(TimeSpan.Zero, result.Offset);
        Assert.Equal(2024, result.Year);
    }

    [Fact]
    public void NormalizeDateTimeOffset_EmptyString_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => TableGateway<TestEntity, int>.NormalizeDateTimeOffset("   "));
    }

    [Fact]
    public void NormalizeDateTimeOffset_InvalidString_ReachesFinalParseAndThrows()
    {
        Assert.Throws<FormatException>(() => TableGateway<TestEntity, int>.NormalizeDateTimeOffset("not-a-date"));
    }

    [Fact]
    public void PrivateHasExplicitOffset_ExercisesSeparatorBranches()
    {
        var method = typeof(TableGateway<TestEntity, int>).GetMethod("HasExplicitOffset",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.True((bool)method.Invoke(null, new object[] { "2024-01-01Z" })!);
        Assert.True((bool)method.Invoke(null, new object[] { "2024-01-01t12:00:00+01:00" })!);
        Assert.True((bool)method.Invoke(null, new object[] { "2024-01-01 12:00:00-01:00" })!);
        Assert.False((bool)method.Invoke(null, new object[] { "2024-01-01" })!);
    }

    [Fact]
    public void PrivateConvertToGuid_ExercisesAllSwitchBranches()
    {
        var method = typeof(TableGateway<TestEntity, int>).GetMethod("ConvertToGuid",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var expected = Guid.NewGuid();

        var fromGuid = (Guid)method.Invoke(null, new object[] { expected })!;
        var fromString = (Guid)method.Invoke(null, new object[] { expected.ToString("D") })!;
        var fromOther = (Guid)method.Invoke(null, new object[] { new GuidStringWrapper(expected) })!;

        Assert.Equal(expected, fromGuid);
        Assert.Equal(expected, fromString);
        Assert.Equal(expected, fromOther);
    }

    [Fact]
    public void BuildUpdateByKey_Reflection_ExercisesStringSetClauseAndVersionIncrement()
    {
        var gateway = new TableGateway<TestEntity, int>(Context, AuditValueResolver);
        var tableInfoField = typeof(TableGateway<TestEntity, int>).GetField("_tableInfo",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var tableInfo = tableInfoField.GetValue(gateway)!;
        var keyColumns = (System.Collections.Generic.IReadOnlyList<IColumnInfo>)tableInfo.GetType()
            .GetProperty("PrimaryKeys")!
            .GetValue(tableInfo)!;

        var buildUpdateByKey = typeof(TableGateway<TestEntity, int>).GetMethod("BuildUpdateByKey",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        var entity = new TestEntity
        {
            Id = 7,
            Name = "pk-value",
            CreatedBy = "creator",
            CreatedOn = DateTime.UtcNow.AddDays(-2),
            LastUpdatedBy = "updater",
            LastUpdatedOn = DateTime.UtcNow,
            version = 5
        };

        var result = ((string sql, System.Collections.Generic.List<System.Data.Common.DbParameter> parameters))
            buildUpdateByKey.Invoke(gateway, new object[] { entity, keyColumns, Context.GetDialect() })!;

        Assert.Contains("SET", result.sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Version", result.sql, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(result.parameters);
    }

    [Fact]
    public void AppendVersionCondition_WithNullValue_AppendsIsNull()
    {
        var gateway = new TableGateway<TestEntity, int>(Context, AuditValueResolver);
        var method = typeof(TableGateway<TestEntity, int>).GetMethod("AppendVersionCondition",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        var container = Context.CreateSqlContainer("UPDATE Test SET Name = @p0 WHERE 1=1");
        var counters = new ClauseCounters();
        var args = new object?[] { container, null, Context.GetDialect(), counters };

        var result = method.Invoke(gateway, args);
        _ = (ClauseCounters)args[3]!;

        Assert.Null(result);
        Assert.Contains("IS NULL", container.Query.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AppendVersionCondition_WithValue_UsesPositionalParameterForSql92()
    {
        var unknownContext = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Unknown",
            new fakeDbFactory(SupportedDatabase.Unknown), TypeMap);
        await using var disposeUnknown = unknownContext;

        var gateway = new TableGateway<TestEntity, int>(unknownContext, AuditValueResolver);
        var method = typeof(TableGateway<TestEntity, int>).GetMethod("AppendVersionCondition",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        var container = unknownContext.CreateSqlContainer("UPDATE Test SET Name = ? WHERE 1=1");
        var counters = new ClauseCounters();
        var args = new object?[] { container, 42, unknownContext.GetDialect(), counters };

        var result = method.Invoke(gateway, args);

        Assert.NotNull(result);
        Assert.Contains('?', container.Query.ToString());
    }

    [Fact]
    public async Task LoadOriginalAsync_OverloadWithoutToken_ExecutesForwardingPath()
    {
        var gateway = new TableGateway<TestEntity, int>(Context, AuditValueResolver);
        var method = typeof(TableGateway<TestEntity, int>).GetMethod("LoadOriginalAsync",
            BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(TestEntity), typeof(IDatabaseContext) },
            null)!;

        var entity = new TestEntity
        {
            Id = 0,
            Name = "default-id"
        };

        var task = (Task<TestEntity?>)method.Invoke(gateway, new object?[] { entity, Context })!;
        var result = await task;

        Assert.Null(result);
    }

    [Table("NoIdEntities")]
    private sealed class NoIdEntity
    {
        [PrimaryKey(1)]
        [Column("key", DbType.String)]
        public string Key { get; set; } = string.Empty;

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class GuidStringWrapper
    {
        private readonly Guid _guid;

        public GuidStringWrapper(Guid guid)
        {
            _guid = guid;
        }

        public override string ToString()
        {
            return _guid.ToString("D", CultureInfo.InvariantCulture);
        }
    }

    private sealed class ConvertibleDateTime : IConvertible
    {
        private readonly DateTime _value;

        public ConvertibleDateTime(DateTime value)
        {
            _value = value;
        }

        public TypeCode GetTypeCode() => TypeCode.Object;
        public bool ToBoolean(IFormatProvider? provider) => throw new InvalidCastException();
        public byte ToByte(IFormatProvider? provider) => throw new InvalidCastException();
        public char ToChar(IFormatProvider? provider) => throw new InvalidCastException();
        public DateTime ToDateTime(IFormatProvider? provider) => _value;
        public decimal ToDecimal(IFormatProvider? provider) => throw new InvalidCastException();
        public double ToDouble(IFormatProvider? provider) => throw new InvalidCastException();
        public short ToInt16(IFormatProvider? provider) => throw new InvalidCastException();
        public int ToInt32(IFormatProvider? provider) => throw new InvalidCastException();
        public long ToInt64(IFormatProvider? provider) => throw new InvalidCastException();
        public sbyte ToSByte(IFormatProvider? provider) => throw new InvalidCastException();
        public float ToSingle(IFormatProvider? provider) => throw new InvalidCastException();
        public string ToString(IFormatProvider? provider) => _value.ToString("O", CultureInfo.InvariantCulture);
        public object ToType(Type conversionType, IFormatProvider? provider)
        {
            if (conversionType == typeof(DateTime))
            {
                return _value;
            }

            throw new InvalidCastException();
        }

        public ushort ToUInt16(IFormatProvider? provider) => throw new InvalidCastException();
        public uint ToUInt32(IFormatProvider? provider) => throw new InvalidCastException();
        public ulong ToUInt64(IFormatProvider? provider) => throw new InvalidCastException();
    }
}
