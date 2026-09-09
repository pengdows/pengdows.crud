using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Found while mapping pengdows.crud's layered parameter-type-coercion system (AdvancedTypeRegistry
/// -> CoercionRegistry -> ParameterBindingRules) during the architecture cleanup that added this
/// test: <c>ProviderParameterFactory.ApplyMySqlOptimizations</c> sets a bool parameter's
/// <c>DbType</c> to <c>DbType.Byte</c> (TINYINT(1) compatibility) but never converts
/// <c>parameter.Value</c> itself away from the raw C# <c>bool</c> — the one live code path that
/// actually runs for MySql/MariaDb bool parameters through
/// <see cref="SqlDialect.CreateDbParameter{T}"/>, the real production entry point (not the
/// coercion classes tested in isolation). <c>ParameterBindingRules.ApplyBooleanNormalization</c>
/// has the correct <c>(byte)1</c>/<c>(byte)0</c> conversion, but is unreachable dead code here:
/// <c>CoercionRegistry</c>'s <c>BooleanCoercion</c> always succeeds first, short-circuiting the
/// `||` chain in <c>AdvancedTypeRegistry.TryConfigureParameterForDialect</c> before
/// <c>ParameterBindingRules</c> ever runs.
/// </summary>
public class MySqlBooleanParameterValueTests
{
    [Theory]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    public void CreateDbParameter_BoolValue_ConvertsToByteValue_NotJustByteDbType(SupportedDatabase provider)
    {
        var factory = new fakeDbFactory(provider);
        SqlDialect dialect = provider == SupportedDatabase.MariaDb
            ? new MariaDbDialect(factory, NullLogger.Instance)
            : new MySqlDialect(factory, NullLogger.Instance);

        var trueParam = dialect.CreateDbParameter("flag_true", DbType.Boolean, true);
        var falseParam = dialect.CreateDbParameter("flag_false", DbType.Boolean, false);

        Assert.Equal(DbType.Byte, trueParam.DbType);
        Assert.Equal((byte)1, trueParam.Value);

        Assert.Equal(DbType.Byte, falseParam.DbType);
        Assert.Equal((byte)0, falseParam.Value);
    }
}
