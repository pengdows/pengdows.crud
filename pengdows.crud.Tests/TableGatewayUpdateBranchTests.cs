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
public class TableGatewayUpdateBranchTests : SqlLiteContextTestBase
{
    public TableGatewayUpdateBranchTests()
    {
        TypeMap.Register<TestEntity>();
        TypeMap.Register<NoIdEntity>();
        TypeMap.Register<OnlyIdEntity>();
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

    // The dirty check reads timestamp text as every read path does (TypeCoercionHelper.TryParseTimestampText):
    // a stated offset keeps its instant, text without one is UTC, and a time of day alone is no timestamp.
    [Theory]
    [InlineData("2024-01-01Z", "2024-01-01T00:00:00+00:00")]
    [InlineData("2024-01-01t12:00:00+01:00", "2024-01-01T12:00:00+01:00")]
    [InlineData("2024-01-01 12:00:00-01:00", "2024-01-01T12:00:00-01:00")]
    [InlineData("2024-01-01", "2024-01-01T00:00:00+00:00")]
    [InlineData(" 2024-01-01T12:00:00.1234567 ", "2024-01-01T12:00:00.1234567+00:00")]
    public void NormalizeDateTimeOffset_TimestampText_ReadsAsTheReadPathDoes(string text, string expected)
    {
        var result = TableGateway<TestEntity, int>.NormalizeDateTimeOffset(text);

        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), result);
        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture).Offset, result.Offset);
    }

    [Fact]
    public void NormalizeDateTimeOffset_TimeOfDayText_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => TableGateway<TestEntity, int>.NormalizeDateTimeOffset("13:45:30"));
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

        var result = await (dynamic)method.Invoke(gateway, new object?[] { entity, Context })!;

        Assert.Null(result);
    }

    // -------------------------------------------------------------------------
    // UpdateAsync CT overload — "No changes detected" catch (Core.cs lines 1133-1136)
    // An entity with only [Id] and no other updateable columns causes BuildSetClause
    // to return 0 columns → throws "No changes detected for update." → caught → returns 0
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_EntityWithNoUpdateableColumns_CatchesNoChanges_ReturnsZero()
    {
        TypeMap.Register<OnlyIdEntity>();
        var gateway = new TableGateway<OnlyIdEntity, int>(Context);
        var entity = new OnlyIdEntity { Id = 1 };

        // Passes cancellationToken explicitly to exercise the CT UpdateAsync overload (Core.cs line 1117)
        // BuildUpdateInternal finds 0 updateable columns → throws "No changes detected"
        // CT UpdateAsync catch block at lines 1133-1136 returns 0
        var result = await gateway.UpdateAsync(entity, false, null, System.Threading.CancellationToken.None);
        Assert.Equal(0, result);
    }

    // COR-012: "no changes" was an InvalidOperationException UpdateAsync threw and caught by matching
    // its message: an exception per no-op update, and fragile. UpdateAsync now returns 0 without one;
    // BuildUpdateAsync still throws, as documented.
    private static readonly System.Threading.AsyncLocal<bool> CountingExceptions = new();

    [Fact]
    public async Task UpdateAsync_NoChanges_ReturnsZeroWithoutThrowingInternally()
    {
        var gateway = new TableGateway<OnlyIdEntity, int>(Context);
        var entity = new OnlyIdEntity { Id = 1 };
        var thrown = 0;
        void Count(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
        {
            if (CountingExceptions.Value)
            {
                System.Threading.Interlocked.Increment(ref thrown);
            }
        }

        AppDomain.CurrentDomain.FirstChanceException += Count;
        try
        {
            CountingExceptions.Value = true;
            var result = await gateway.UpdateAsync(entity, false, null, System.Threading.CancellationToken.None);
            CountingExceptions.Value = false;

            Assert.Equal(0, result);
            Assert.Equal(0, thrown);
        }
        finally
        {
            CountingExceptions.Value = false;
            AppDomain.CurrentDomain.FirstChanceException -= Count;
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.BuildUpdateAsync(entity, false, Context).AsTask());
        Assert.Equal("No changes detected for update.", ex.Message);
    }

    [Table("OnlyIdEntities")]
    private sealed class OnlyIdEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }
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
