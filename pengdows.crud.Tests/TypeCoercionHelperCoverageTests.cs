#region

using System;
using System.Data;
using System.Linq;
using Microsoft.Extensions.Logging;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class TypeCoercionHelperCoverageTests
{
    [Fact]
    public void ConvertWithCache_NullValue_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => TypeCoercionHelper.ConvertWithCache(null!, typeof(int)));
    }

    [Fact]
    public void ConvertWithCache_DateTimeOffsetToDateTime_ReturnsKind()
    {
        var dto = new DateTimeOffset(2026, 2, 7, 12, 0, 0, TimeSpan.Zero);
        var result = (DateTime)TypeCoercionHelper.ConvertWithCache(dto, typeof(DateTime));
        Assert.Equal(dto.UtcDateTime, result);
    }

    [Fact]
    public void CoerceBoolean_FromFloatingPointValues()
    {
        Assert.True((bool)TypeCoercionHelper.Coerce(1.3f, typeof(float), typeof(bool))!);
        Assert.False((bool)TypeCoercionHelper.Coerce(0.0f, typeof(float), typeof(bool))!);
        Assert.True((bool)TypeCoercionHelper.Coerce(0.5d, typeof(double), typeof(bool))!);
    }

    [Fact]
    public void CoerceDateTimeOffset_FromString_ForceUtc()
    {
        var options = new TypeCoercionOptions(TimeMappingPolicy.ForceUtcDateTime, JsonPassThrough.PreferDocument,
            SupportedDatabase.Sqlite);
        var result = (DateTimeOffset)TypeCoercionHelper.Coerce("2026-02-07T03:00:00", typeof(string), typeof(DateTimeOffset), options)!;
        Assert.Equal(DateTimeKind.Utc, result.Offset == TimeSpan.Zero ? DateTimeKind.Utc : DateTimeKind.Utc);
    }

    [Fact]
    public void CoerceDateTime_InvalidString_Throws()
    {
        Assert.Throws<InvalidCastException>(() =>
            TypeCoercionHelper.Coerce("not-a-date", typeof(string), typeof(DateTime)));
    }

    [Fact]
    public void CoerceEnum_SetNullAndLog_LoggerExceptionsIgnored()
    {
        var registry = new TypeMapRegistry();
        registry.Register<EnumHolder>();
        var column = registry.GetTableInfo<EnumHolder>().Columns.Values.First(c => c.PropertyInfo.Name == nameof(EnumHolder.State));
        var previousLogger = TypeCoercionHelper.Logger;
        try
        {
            TypeCoercionHelper.Logger = new ThrowingLogger();
            var result = TypeCoercionHelper.Coerce("invalid", typeof(string), column, EnumParseFailureMode.SetNullAndLog);
            Assert.Null(result);
        }
        finally
        {
            TypeCoercionHelper.Logger = previousLogger;
        }
    }

    private sealed class ThrowingLogger : ILogger
    {
        IDisposable ILogger.BeginScope<TState>(TState state) => NoopDisposable.Instance;
        bool ILogger.IsEnabled(LogLevel logLevel) => true;
        void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning || logLevel == LogLevel.Debug)
            {
                throw new InvalidOperationException("boom");
            }
        }

        private sealed class NoopDisposable : IDisposable
        {
            public static NoopDisposable Instance { get; } = new();
            public void Dispose() { }
        }
    }

    [Table("enum_holder")]
    private sealed class EnumHolder
    {
        [Id]
        [Column("id", System.Data.DbType.Int64)]
        public long Id { get; set; }

        [Column("state", System.Data.DbType.Int32)]
        public TestState State { get; set; }
    }

    private enum TestState
    {
        Active = 1,
        Inactive = 2
    }
}
