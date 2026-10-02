using System;
using System.Data;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Found live on SAP HANA (Sap.Data.Hana.Net 2.29): a decimal whose own scale carries trailing zeros
/// beyond the parameter's Scale is mis-sent. 12345678901234567.00m with Precision 18 / Scale 0 was
/// stored as 2345678901234567 (no error), and 1234567890123456789012345.000m failed ("Unable to convert
/// from .NET Decimal value"). The parameter's value is sent without trailing zeros, so its digits always
/// fit the declared Precision/Scale; the number is unchanged.
/// </summary>
public sealed class DecimalParameterNormalizationTests
{
    public static TheoryData<SupportedDatabase> Databases()
    {
        var data = new TheoryData<SupportedDatabase>();
        foreach (var db in Enum.GetValues<SupportedDatabase>())
        {
            if (db != SupportedDatabase.Unknown)
            {
                data.Add(db);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Databases))]
    public void CreateDbParameter_DecimalWithTrailingZeros_SendsValueWithinDeclaredScale(SupportedDatabase db)
    {
        var dialect = new DatabaseContext($"Data Source=x;EmulatedProduct={db}", new fakeDbFactory(db)).Dialect;

        foreach (var value in new[] { 12345678901234567.00m, 1234567890123456789012345.000m, 1.50m, -99.000m })
        {
            var p = dialect.CreateDbParameter("p", DbType.Decimal, value);

            if (p.Value is not decimal sent)
            {
                continue; // a dialect that binds decimals as text (e.g. SQLite) has nothing to scale
            }

            Assert.Equal(value, sent);
            var sentScale = (decimal.GetBits(sent)[3] >> 16) & 0x7F;
            Assert.True(sentScale <= p.Scale, $"{db}: {value} sent with scale {sentScale}, parameter Scale {p.Scale}");
        }
    }

    // A container reused through SetParameterValue (the documented re-binding pattern) kept the
    // first value's Precision/Scale, so a larger value was refused ("Unable to convert from .NET
    // Decimal value") or, with trailing zeros, silently cut short on SAP HANA (confirmed live).
    [Fact]
    public void SetParameterValue_LargerDecimal_RaisesPrecisionAndScaleToFit()
    {
        var context = new DatabaseContext("Server=x;EmulatedProduct=SapHana", new fakeDbFactory(SupportedDatabase.SapHana));
        var sc = context.CreateSqlContainer();
        var p = sc.AddParameterWithValue("p", DbType.Decimal, 1.5m);

        sc.SetParameterValue("p", 1234567890123456789.25m);
        Assert.True(p.Precision >= 21, $"Precision {p.Precision}");
        Assert.True(p.Scale >= 2, $"Scale {p.Scale}");

        sc.SetParameterValue("p", 12345678901234567.00m);
        var sent = Assert.IsType<decimal>(p.Value);
        Assert.Equal(12345678901234567m, sent);
        Assert.True(((decimal.GetBits(sent)[3] >> 16) & 0x7F) <= p.Scale);
    }

    [Fact]
    public void SetParameterValue_SmallerDecimal_KeepsTheCallersPrecisionAndScale()
    {
        var context = new DatabaseContext("Server=x;EmulatedProduct=SapHana", new fakeDbFactory(SupportedDatabase.SapHana));
        var sc = context.CreateSqlContainer();
        var p = sc.AddParameterWithValue("p", DbType.Decimal, 1.5m);
        p.Precision = 30;
        p.Scale = 6;

        sc.SetParameterValue("p", 2m);

        Assert.Equal((byte)30, p.Precision);
        Assert.Equal((byte)6, p.Scale);
    }
}
