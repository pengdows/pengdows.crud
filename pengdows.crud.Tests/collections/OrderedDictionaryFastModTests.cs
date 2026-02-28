using System;
using System.Collections.Generic;
using pengdows.crud.collections;
using Xunit;

namespace pengdows.crud.Tests.collections;

public class OrderedDictionaryFastModTests
{
    private const int SampleSize = 256;

    [Theory]
    [MemberData(nameof(GetDivisors))]
    public void FastMod_MatchesRemainder_ForRandomizedValues(int divisor)
    {
        var multiplier = OrderedDictionary<int, int>.GetFastModMultiplier((uint)divisor);
        var rng = new Random(divisor);

        var samplingRange = Math.Max((long)divisor * 256, 1024L);

        for (var i = 0; i < SampleSize; i++)
        {
            var value = (uint)rng.NextInt64(0, samplingRange);
            var expected = value % (uint)divisor;
            var actual = OrderedDictionary<int, int>.FastMod(value, (uint)divisor, multiplier);

            Assert.Equal(expected, actual);
        }
    }

    [Theory]
    [MemberData(nameof(GetBoundaryValues))]
    public void FastMod_BoundaryValues_StayWithinBounds(int divisor, uint value)
    {
        var multiplier = OrderedDictionary<int, int>.GetFastModMultiplier((uint)divisor);
        var actual = OrderedDictionary<int, int>.FastMod(value, (uint)divisor, multiplier);

        Assert.InRange(actual, 0u, (uint)divisor - 1);
        Assert.Equal(value % (uint)divisor, actual);
    }

    public static IEnumerable<object[]> GetDivisors()
    {
        foreach (var divisor in new[] { 1, 17, 79, 163, 1024, 8192, 65535, 1000003 })
        {
            yield return new object[] { divisor };
        }
    }

    public static IEnumerable<object[]> GetBoundaryValues()
    {
        var divisors = new[] { 1, 163, 65535 };
        var values = new[] { 0u, 1u, uint.MaxValue };

        foreach (var divisor in divisors)
        {
            foreach (var value in values)
            {
                yield return new object[] { divisor, value };
            }
        }
    }
}
