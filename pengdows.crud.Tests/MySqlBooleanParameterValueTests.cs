using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// A MySQL/MariaDB bool parameter must carry the byte value (1/0), not only <c>DbType.Byte</c>
/// with the raw C# <c>bool</c>, through <see cref="SqlDialect.CreateDbParameter{T}"/>, the real
/// entry point. (The ProviderParameterFactory/ParameterBindingRules layer this was first traced
/// through was unreachable and has been removed, REV-039.)
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
