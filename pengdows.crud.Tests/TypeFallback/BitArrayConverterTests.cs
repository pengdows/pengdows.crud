using System.Collections;
using System.Data;
using System.Linq;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.TypeFallback;

/// <summary>
/// A BitArray on SQLite (no bit type) is stored as its 0/1 text, which keeps its length exactly, and the BitArray
/// coercion reads it back.
/// </summary>
public class BitArraySqliteTests
{
    [Fact]
    public void OnSqliteTheBitsAreBoundAsTheirText()
    {
        var dialect = SqlDialectFactory.CreateDialectForType(SupportedDatabase.Sqlite,
            new fakeDbFactory(SupportedDatabase.Sqlite), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var parameter = dialect.CreateDbParameter("p", DbType.Object, new BitArray(new[] { true, false, true }));

        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal("101", parameter.Value);
    }

    [Fact]
    public void TheTextReadsBackToTheSameBits()
    {
        Assert.True(pengdows.crud.@internal.BitStringFormat.TryParse("101", out var bits));

        Assert.Equal(new[] { true, false, true }, bits.Cast<bool>().ToArray());
    }
}
