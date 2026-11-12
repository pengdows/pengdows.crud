using System;
using System.Reflection;
using pengdows.crud.exceptions;
using Xunit;

namespace pengdows.crud.Tests.exceptions;

/// <summary>
/// TransactionModeNotSupportedException is still thrown on 2.0.6 by BeginTransaction(IsolationProfile) and is
/// documented as the type to catch, so it is NOT obsolete on 2.0.x. Marking it obsolete made every catch warn while
/// the docs still told callers to catch it. (3.0 removes it; that deprecation belongs in 3.0.)
/// </summary>
public class TransactionModeNotSupportedObsolescenceTests
{
    [Fact]
    public void TheException_IsNotObsolete_BecauseTheLibraryStillThrowsIt()
    {
        var obsolete = typeof(TransactionModeNotSupportedException).GetCustomAttribute<ObsoleteAttribute>();

        Assert.Null(obsolete);
    }

    [Fact]
    public void TheException_StillDerivesFromNotSupportedException_SoCatchingTheBaseTypeKeepsWorking()
    {
        Assert.True(typeof(NotSupportedException).IsAssignableFrom(typeof(TransactionModeNotSupportedException)));
    }
}
