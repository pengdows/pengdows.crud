using System;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.exceptions;

/// <summary>
/// DRY-018: Db2 and Informix report negative SQLCODEs and compared their magnitude with
/// Math.Abs(code), which throws OverflowException for int.MinValue: classifying such an error threw
/// from inside the error path instead of reporting it. One helper gives the magnitude, none for a
/// code without one.
/// </summary>
public class ProviderErrorCodeMagnitudeTests
{
    private sealed class CodedException : DbException
    {
        public CodedException(int number) : base("provider error") => Number = number;

        public int Number { get; }
    }

    [Theory]
    [InlineData(SupportedDatabase.Db2)]
    [InlineData(SupportedDatabase.Informix)]
    public void SmallestCode_IsClassifiedWithoutOverflow(SupportedDatabase product)
    {
        var dialect = (SqlDialect)SqlDialectFactory.CreateDialectForType(product, new fakeDbFactory(product), NullLogger.Instance);
        var ex = new CodedException(int.MinValue);

        Assert.False(dialect.IsUniqueViolation(ex));
        Assert.False(dialect.IsForeignKeyViolation(ex));
        Assert.False(dialect.IsNotNullViolation(ex));
        Assert.False(dialect.IsCheckConstraintViolation(ex));
        Assert.Equal(int.MinValue, dialect.AnalyzeException(ex).ProviderErrorCode);
    }

    [Theory]
    [InlineData(SupportedDatabase.Db2, -803)]
    [InlineData(SupportedDatabase.Informix, -268)]
    public void NegativeCodes_StillClassify(SupportedDatabase product, int uniqueCode)
    {
        var dialect = (SqlDialect)SqlDialectFactory.CreateDialectForType(product, new fakeDbFactory(product), NullLogger.Instance);

        Assert.True(dialect.IsUniqueViolation(new CodedException(uniqueCode)));
        Assert.True(dialect.IsForeignKeyViolation(new CodedException(product == SupportedDatabase.Db2 ? -530 : -691)));
    }
}
