using System.Data;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests.TypeFallback;

/// <summary>
/// TiDB's VECTOR takes its bracketed text, "[1,2.5,3]", and returns the same text: a float vector is bound as that text and
/// read back from it.
/// </summary>
public class TiDbVectorTests
{
    [Fact]
    public void AVectorIsBoundAsItsBracketedText()
    {
        var dialect = SqlDialectFactory.CreateDialectForType(SupportedDatabase.TiDb,
            new fakeDbFactory(SupportedDatabase.TiDb), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var parameter = dialect.CreateDbParameter("p", DbType.Object, new float[] { 1f, 2.5f, 3f });

        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal("[1,2.5,3]", parameter.Value);
    }

    [Fact]
    public void TheBracketedTextReadsBackAsTheVector()
    {
        Assert.True(new BracketedVectorCoercion().TryRead(new DbValue("[1,2.5,3]"), out var vector));

        Assert.Equal(new float[] { 1f, 2.5f, 3f }, vector);
    }

    [Fact]
    public void MalformedVectorTextDeclines()
    {
        Assert.False(new BracketedVectorCoercion().TryRead(new DbValue("[1,x]"), out _));
    }
}
