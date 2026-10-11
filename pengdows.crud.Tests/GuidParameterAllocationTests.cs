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
/// Pins what a parameter allocates, after the 2.0.6 A/B against 2.0.5 found every parameter allocating more: a 32 B closure in
/// <c>DbTypeValidator</c> and a value type boxed twice. These tests fail if those return (9 of them fail with the whole parameter
/// path reverted to before the fix). They do NOT see the CPU-only wins made alongside: the TYPE-002 test order (<c>type ==
/// DbType.Binary &amp;&amp; value is Guid</c>), Firebird's per-<c>T</c> zoned-value check, and the per-<c>T</c> gates; on the
/// optimized code (tier-1 JIT) the old forms allocate the same bytes. Those are guarded only by the A/B benchmark and the
/// behavior tests (<c>ParameterValueGatingTests</c>, the Firebird zoned-value test below).
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

    // Firebird's override looked at the value's runtime type for an FbZonedDateTime before doing anything else (a box and a
    // type-name string compare on every parameter, measured in time, not in bytes, on optimized code). This pins that a Guid
    // costs no more than 2.0.5's 128 B (binary) and 184 B (string).
    [Theory]
    [InlineData(true, 136)]
    [InlineData(false, 192)]
    public void AFirebirdGuidParameter_CostsNoMoreThanItDidInTheLastRelease(bool binary, long ceiling)
    {
        var mode = binary ? FirebirdGuidStorageMode.Binary : FirebirdGuidStorageMode.String;
        var dialect = new FirebirdDialect(new fakeDbFactory(SupportedDatabase.Firebird), NullLogger.Instance)
        {
            GuidStorageMode = mode
        };
        DbParameter Create() => dialect.CreateDbParameter("id", DbType.Guid, Sample);
        WarmUpToOptimizedCode(Create);

        var bytes = AllocationMeasurement.Lowest(() => Measure(Create));

        Assert.True(bytes <= ceiling, $"A Firebird {mode} Guid parameter allocated {bytes} B; expected at most {ceiling} B.");
    }

    // The zoned-value branch is the reason the check exists: a value that IS an FbZonedDateTime keeps its Object type.
    [Fact]
    public void AFirebirdZonedValue_IsStillRecognizedAndKeepsItsObjectType()
    {
        var dialect = new FirebirdDialect(new fakeDbFactory(SupportedDatabase.Firebird), NullLogger.Instance);

        var zoned = new FirebirdSql.Data.Types.FbZonedDateTime(new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc), "UTC");

        var parameter = dialect.CreateDbParameter("t", DbType.Object, zoned);

        Assert.Equal(DbType.Object, parameter.DbType);
        Assert.IsType<FirebirdSql.Data.Types.FbZonedDateTime>(parameter.Value);
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
