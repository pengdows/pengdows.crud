#region

using System;
using System.Reflection;
using pengdows.crud;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

/// <summary>
/// IDatabaseContext.DataSource hands out a raw DbDataSource, which bypasses connection governance.
/// Developers must never need it, so it is obsolete on 2.0.x and removed in 3.0.
/// </summary>
public class DataSourceObsolescenceTests
{
    [Fact]
    public void IDatabaseContext_DataSource_IsObsolete_RemovedIn3_0()
    {
        var property = typeof(IDatabaseContext).GetProperty(nameof(IDatabaseContext.DataSource),
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);

        var obsolete = property!.GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotNull(obsolete);
        Assert.False(obsolete!.IsError);
        Assert.Contains("3.0", obsolete.Message, StringComparison.Ordinal);
        Assert.Contains("removed", obsolete.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DatabaseContext_DataSource_IsObsolete_RemovedIn3_0()
    {
        var property = typeof(DatabaseContext).GetProperty(nameof(DatabaseContext.DataSource),
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);

        var obsolete = property!.GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotNull(obsolete);
        Assert.False(obsolete!.IsError);
        Assert.Contains("3.0", obsolete.Message, StringComparison.Ordinal);
        Assert.Contains("removed", obsolete.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TransactionContext_DataSource_IsObsolete_RemovedIn3_0()
    {
        var property = typeof(TransactionContext).GetProperty(nameof(TransactionContext.DataSource),
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);

        var obsolete = property!.GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotNull(obsolete);
        Assert.False(obsolete!.IsError);
        Assert.Contains("3.0", obsolete.Message, StringComparison.Ordinal);
        Assert.Contains("removed", obsolete.Message, StringComparison.OrdinalIgnoreCase);
    }
}
