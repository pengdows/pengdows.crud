using System;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public class ParameterCreationTests
{
    private static SqlDialect CreateDialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        return new SqlServerDialect(factory, NullLogger.Instance);
    }

    [Fact]
    public void CreateDbParameter_WithDecimal_SetsPrecisionToAtLeast18AndExactScale()
    {
        // Precision is set to max(inferred, 18) so SqlClient 6.x accepts any value
        // that fits a standard DECIMAL(18,x) column.  Scale is set to the value's
        // natural fractional digits to avoid silent rounding.
        var dialect = CreateDialect();
        var p = dialect.CreateDbParameter(null, DbType.Decimal, 123.45m);
        // 123.45m: inferred precision=5 (3 integer + 2 fractional), scale=2
        // Precision = max(5, 18) = 18; Scale = 2
        Assert.Equal((byte)18, p.Precision);
        Assert.Equal((byte)2, p.Scale);
    }

    [Fact]
    public void CreateDbParameter_WithNullDecimal_DoesNotSetPrecision()
    {
        // Null value: decimal branch is not entered; Precision/Scale stay at 0.
        var dialect = CreateDialect();
        var p = dialect.CreateDbParameter(null, DbType.Decimal, (decimal?)null);
        Assert.Equal((byte)0, p.Precision);
        Assert.Equal((byte)0, p.Scale);
    }

    [Fact]
    public void CreateDbParameter_NoName_GeneratesName()
    {
        var dialect = CreateDialect();
        var p = dialect.CreateDbParameter(DbType.Int32, 5);

        Assert.False(string.IsNullOrEmpty(p.ParameterName));
        Assert.Equal(DbType.Int32, p.DbType);
        Assert.Equal(5, p.Value);
    }

    [Fact]
    public void CreateDbParameter_NoName_NullValue_UsesDbNull()
    {
        var dialect = CreateDialect();
        var p = dialect.CreateDbParameter<string?>(DbType.String, null);

        Assert.False(string.IsNullOrEmpty(p.ParameterName));
        Assert.Equal(DbType.String, p.DbType);
        Assert.Equal(DBNull.Value, p.Value);
    }

    private sealed class PositionalDialect : Sql92Dialect
    {
        public PositionalDialect(DbProviderFactory factory) : base(factory, NullLogger.Instance)
        {
        }

        public override bool SupportsNamedParameters => false;
        public override string ParameterMarker => "?";
    }

    [Fact]
    public void CreateDbParameter_Positional_ClearsNameAndConverts()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Unknown);
        var dialect = new PositionalDialect(factory);
        var p = dialect.CreateDbParameter("flag", DbType.Boolean, true);

        Assert.Equal(string.Empty, p.ParameterName);
        Assert.Equal(DbType.Int16, p.DbType);
        Assert.Equal((short)1, p.Value);
    }

    [Fact]
    public void CreateDbParameter_NamedParameters_RetainsNameAndType()
    {
        var dialect = CreateDialect();
        var p = dialect.CreateDbParameter("flag", DbType.Boolean, true);

        Assert.Equal("flag", p.ParameterName);
        Assert.Equal(DbType.Boolean, p.DbType);
        Assert.Equal(true, p.Value);
    }
}