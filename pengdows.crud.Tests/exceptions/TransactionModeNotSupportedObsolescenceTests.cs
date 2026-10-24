using System;
using System.Reflection;
using pengdows.crud.exceptions;
using Xunit;

namespace pengdows.crud.Tests.exceptions;

/// <summary>
/// 3.0 removes TransactionModeNotSupportedException: an isolation profile the database cannot honor throws
/// NotSupportedException unless the caller passes IsolationResolutionPolicy.AllowLower. On 2.0.x it is still thrown
/// (and derives from NotSupportedException), so it is marked obsolete with the 3.0 replacement spelled out.
/// </summary>
public class TransactionModeNotSupportedObsolescenceTests
{
    [Fact]
    public void TheException_IsObsolete_PointsAtNotSupportedException_RemovedIn3_0()
    {
        var obsolete = typeof(TransactionModeNotSupportedException).GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotNull(obsolete);
        Assert.False(obsolete!.IsError);
        Assert.Contains("3.0", obsolete.Message, StringComparison.Ordinal);
        Assert.Contains("NotSupportedException", obsolete.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheException_StillDerivesFromNotSupportedException_SoCatchingTheBaseTypeKeepsWorking()
    {
        Assert.True(typeof(NotSupportedException).IsAssignableFrom(typeof(TransactionModeNotSupportedException)));
    }
}
