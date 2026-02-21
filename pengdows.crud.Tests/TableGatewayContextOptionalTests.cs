using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace pengdows.crud.Tests;

public class TableGatewayContextOptionalTests
{
    [Fact]
    public void TableGateway_PublicMethods_WithContextParameters_AreOptional()
    {
        var methods = typeof(TableGateway<TestTable, long>)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => !method.IsSpecialName);

        foreach (var method in methods)
        {
            foreach (var parameter in method.GetParameters())
            {
                if (typeof(IDatabaseContext).IsAssignableFrom(parameter.ParameterType))
                {
                    Assert.True(parameter.IsOptional,
                        $"Expected optional context parameter on {method.Name} but found required.");
                    Assert.True(parameter.HasDefaultValue,
                        $"Expected default value on {method.Name} for parameter {parameter.Name}.");
                    Assert.Null(parameter.DefaultValue);
                }
            }
        }
    }

    [Fact]
    public void ITableGateway_Methods_WithContextParameters_AreOptional()
    {
        var methods = typeof(ITableGateway<TestTable, long>)
            .GetMethods()
            .Where(method => !method.IsSpecialName);

        foreach (var method in methods)
        {
            foreach (var parameter in method.GetParameters())
            {
                if (typeof(IDatabaseContext).IsAssignableFrom(parameter.ParameterType))
                {
                    Assert.True(parameter.IsOptional,
                        $"Expected optional context parameter on {method.Name} but found required.");
                    Assert.True(parameter.HasDefaultValue,
                        $"Expected default value on {method.Name} for parameter {parameter.Name}.");
                    Assert.Null(parameter.DefaultValue);
                }
            }
        }
    }
}
