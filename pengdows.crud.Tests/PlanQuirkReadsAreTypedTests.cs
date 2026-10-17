#pragma warning disable CS0618 // TypeCoercionOptions is internal plumbing here
using System;
using System.Data.Common;
using System.Linq.Expressions;
using System.Numerics;
using pengdows.crud.enums;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// The provider-quirk reads (an offset timestamp, a wide integer, a Guid the provider can't return, inet and interval
/// without Npgsql) convert through one typed expression on the plan path, as on the gateway path, not a coercer
/// delegate called per value.
/// </summary>
public class PlanQuirkReadsAreTypedTests
{
    public sealed class Plain<T>
    {
        public T V { get; set; } = default!;
    }

    private sealed class CountDelegateCalls : ExpressionVisitor
    {
        public int Invocations;

        protected override Expression VisitInvocation(InvocationExpression node)
        {
            Invocations++;
            return base.VisitInvocation(node);
        }
    }

    private static int DelegateCalls<T>(Type fieldType, TypeCoercionOptions? options = null)
    {
        var obj = Expression.Parameter(typeof(Plain<T>), "target");
        var reader = Expression.Parameter(typeof(DbDataReader), "reader");
        var key = new DataReaderMapper.SetterCacheKey(typeof(Plain<T>), typeof(Plain<T>).GetProperty("V")!, fieldType,
            RequiresCoercion: true, EnumParseFailureMode.Throw, Ordinal: 0, options);
        var body = DataReaderMapper.BuildSetterBody<Plain<T>>(key, obj, reader, out _);
        var count = new CountDelegateCalls();
        count.Visit(body);
        return count.Invocations;
    }

    [Fact]
    public void LongIntoBigInteger() => Assert.Equal(0, DelegateCalls<BigInteger>(typeof(long)));

    [Fact]
    public void ObjectIntoGuid() => Assert.Equal(0, DelegateCalls<Guid>(typeof(object)));

    [Fact]
    public void DateTimeIntoOffset_WhenTheDriverReadsOffsetsFromValue() =>
        Assert.Equal(0, DelegateCalls<DateTimeOffset>(typeof(DateTime),
            TypeCoercionOptions.Default with { ReadsOffsetTimestampsFromValue = true }));

    [Fact]
    public void ObjectIntoInet_WithoutNpgsql() => Assert.Equal(0, DelegateCalls<Inet>(typeof(object)));

    [Fact]
    public void ObjectIntoPostgreSqlInterval_WithoutNpgsql() =>
        Assert.Equal(0, DelegateCalls<PostgreSqlInterval>(typeof(object)));
}
