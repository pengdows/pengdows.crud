using System;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests.dialects;

public class SqlDialectWrapSafetyTests
{
    private sealed class TestDialect : SqlDialect
    {
        public TestDialect()
            : base(new fakeDbFactory(SupportedDatabase.Sqlite), NullLogger.Instance)
        {
        }

        public override SupportedDatabase DatabaseType => SupportedDatabase.Unknown;
    }

    [Fact]
    public void WrapSimpleName_EscapesInternalQuotes()
    {
        ISqlDialect dialect = new TestDialect();
        
        // This is the safety gap: default interface implementation does QuotePrefix + name + QuoteSuffix.
        // It should double-up the internal double-quote if QuotePrefix/Suffix is double-quote.
        var input = "my\"column";
        var actual = dialect.WrapSimpleName(input);
        
        // Expected behavior for SQL standard quoting (double the quote char):
        // QuotePrefix (") + my + DoubleInternalQuote ("") + column + QuoteSuffix (")
        Assert.Equal("\"my\"\"column\"", actual);
    }

    [Fact]
    public void WrapSimpleName_IsConsistentWithWrapObjectName()
    {
        ISqlDialect dialect = new TestDialect();
        var input = "my\"column";
        
        var wrappedSimple = dialect.WrapSimpleName(input);
        var wrappedObject = dialect.WrapObjectName(input);
        
        Assert.Equal(wrappedObject, wrappedSimple);
    }
}
