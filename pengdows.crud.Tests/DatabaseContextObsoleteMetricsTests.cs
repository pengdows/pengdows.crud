#region

using System;
using System.Reflection;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

/// <summary>
/// TotalConnectionsReused is never incremented by the library (provider pool reuse is invisible
/// to it), so it always reads 0. It is marked obsolete on 2.0.x and removed in 3.0.
/// </summary>
public class DatabaseContextObsoleteMetricsTests
{
    [Fact]
    public void TotalConnectionsReused_IsObsolete_NotTracked_RemovedIn3_0()
    {
        var property = typeof(DatabaseContext).GetProperty(nameof(DatabaseContext.TotalConnectionsReused),
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);

        var obsolete = property!.GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotNull(obsolete);
        Assert.False(obsolete!.IsError);
        Assert.Contains("not tracked", obsolete.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3.0", obsolete.Message, StringComparison.Ordinal);
    }
}
