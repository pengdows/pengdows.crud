using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// 2.0.6 regression found by the A/B against 2.0.5: every Guid parameter allocated 32 B more (one extra boxed Guid) and cost
/// 13 to 32 ns more. The TYPE-002 check <c>value is Guid g &amp;&amp; type == DbType.Binary</c> evaluated the type test first; for a
/// generic T that is Guid, <c>box; isinst; unbox</c> cannot be folded away, so it boxed the value on every call. Testing the
/// DbType first skips it for every Guid that is not declared Binary. A Guid parameter may cost only what an int of the same shape
/// does plus the difference between their boxes (8 B).
/// </summary>
[Collection("AllocationSerial")]
public sealed class GuidParameterAllocationTests
{
    private static readonly Guid Sample = new("550e8400-e29b-41d4-a716-446655440000");

    private static SqlServerDialect NewDialect() =>
        new(new fakeDbFactory(SupportedDatabase.SqlServer), NullLogger.Instance);

    // Tiered JIT starts the dialect's code unoptimized, where a generic type test boxes every time whatever its type; wait
    // for the optimized code, which is what runs in production and in the benchmarks, before measuring.
    private static void WarmUpToOptimizedCode(Func<DbParameter> create)
    {
        for (var round = 0; round < 4; round++)
        {
            for (var i = 0; i < 3_000; i++)
            {
                create();
            }

            Thread.Sleep(250);
        }
    }

    private static long Measure(Func<DbParameter> create)
    {
        const int runs = 500;
        create();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < runs; i++)
        {
            create();
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / runs;
    }

    // What a parameter must cost: the parameter object plus ONE box of its value. Before the fix a string parameter allocated 88 B,
    // an int 112 B, a DateTime 136 B and a Guid 152 B on the optimized code: a 32 B closure in DbTypeValidator on every
    // parameter, and a value type boxed once for the mapped-type call and again for PrepareParameterValue. 2.0.5 allocated 120 B
    // for the Guid. Figures are exact bytes on the fakeDb parameter class, with a few bytes of slack.
    [Theory]
    [InlineData("string", 64)]
    [InlineData("int", 88)]
    [InlineData("datetime", 88)]
    [InlineData("guid", 96)]
    public void ACreatedParameter_AllocatesItsParameterObjectAndOneBoxOfItsValue(string kind, long ceiling)
    {
        var dialect = NewDialect();
        Func<DbParameter> create = kind switch
        {
            "string" => () => dialect.CreateDbParameter("id", DbType.String, "x"),
            "int" => () => dialect.CreateDbParameter("id", DbType.Int32, 7),
            "datetime" => () => dialect.CreateDbParameter("id", DbType.DateTime, new DateTime(2026, 10, 11)),
            _ => () => dialect.CreateDbParameter("id", DbType.Guid, Sample)
        };
        WarmUpToOptimizedCode(create);

        var bytes = AllocationMeasurement.Lowest(() => Measure(create));

        Assert.True(bytes <= ceiling, $"A {kind} parameter allocated {bytes} B; expected at most {ceiling} B.");
    }

    [Fact]
    public void ABinaryDeclaredGuid_StillBindsItsBytes()
    {
        var dialect = NewDialect();

        var parameter = dialect.CreateDbParameter("id", DbType.Binary, Sample);

        Assert.IsType<byte[]>(parameter.Value);
        Assert.Equal(16, ((byte[])parameter.Value!).Length);
    }
}
