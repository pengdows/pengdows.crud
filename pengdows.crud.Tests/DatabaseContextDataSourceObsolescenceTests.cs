using System;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using pengdows.crud.configuration;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Nothing public may let a caller hold or hand in something that can open connections: a DbDataSource passed to the
/// context stays in the caller's hands, so it can open connections outside the pool governor, and who disposes it is
/// unclear. 3.0 removes the data-source constructor; on 2.0.x it is obsolete.
/// </summary>
public class DatabaseContextDataSourceObsolescenceTests
{
    [Fact]
    public void EveryPublicConstructorTakingADbDataSource_IsObsolete_RemovedIn3_0()
    {
        var constructors = typeof(DatabaseContext)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Where(c => c.GetParameters().Any(p => typeof(DbDataSource).IsAssignableFrom(p.ParameterType)))
            .ToList();

        Assert.NotEmpty(constructors);
        Assert.All(constructors, constructor =>
        {
            var obsolete = constructor.GetCustomAttribute<ObsoleteAttribute>();
            Assert.NotNull(obsolete);
            Assert.False(obsolete!.IsError);
            Assert.Contains("3.0", obsolete.Message, StringComparison.Ordinal);
            Assert.Contains("DbDataSource", obsolete.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void NoPublicStaticFactoryTakesADbDataSource()
    {
        var factories = typeof(DatabaseContext)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.GetParameters().Any(p => typeof(DbDataSource).IsAssignableFrom(p.ParameterType)));

        Assert.Empty(factories);
    }
}
