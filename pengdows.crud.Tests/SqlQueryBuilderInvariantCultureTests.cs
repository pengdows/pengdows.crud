#region

using System;
using System.Globalization;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

/// <summary>
/// SQL text must not depend on the thread's culture: numbers are always written with '.' as the
/// decimal separator and '-' as the minus sign.
/// </summary>
public class SqlQueryBuilderInvariantCultureTests
{
    // Decimal comma and U+2212 minus, like de-DE / sv-SE — built explicitly so the test does not
    // depend on the machine's ICU data.
    private static CultureInfo HostileCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        culture.NumberFormat.NumberGroupSeparator = ".";
        culture.NumberFormat.NegativeSign = "−";
        return culture;
    }

    private static string Render(Action<SqlQueryBuilder> build)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = HostileCulture();
            var builder = new SqlQueryBuilder();
            build(builder);
            return builder.ToString();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Append_Int_UsesInvariantMinus()
    {
        Assert.Equal("-5", Render(b => b.Append(-5)));
    }

    [Fact]
    public void Append_Long_UsesInvariantMinus()
    {
        Assert.Equal("-5000000000", Render(b => b.Append(-5_000_000_000L)));
    }

    [Fact]
    public void Append_Double_UsesInvariantSeparator()
    {
        Assert.Equal("-1.5", Render(b => b.Append(-1.5d)));
    }

    [Fact]
    public void Append_Decimal_UsesInvariantSeparator()
    {
        Assert.Equal("1.5", Render(b => b.Append(1.5m)));
    }

    [Fact]
    public void Append_BoxedNumber_UsesInvariantFormatting()
    {
        Assert.Equal("-1.5", Render(b => b.Append((object)(-1.5m))));
    }

    [Fact]
    public void AppendFormat_UsesInvariantFormatting()
    {
        Assert.Equal("LIMIT 1.5 OFFSET -2", Render(b => b.AppendFormat("LIMIT {0} OFFSET {1}", 1.5m, -2)));
    }

    [Fact]
    public void AppendFormat_NullProvider_UsesInvariantFormatting()
    {
        Assert.Equal("1.5", Render(b => b.AppendFormat(null, "{0}", 1.5m)));
    }

    [Fact]
    public void AppendFormat_ExplicitProvider_IsHonored()
    {
        Assert.Equal("1,5", Render(b => b.AppendFormat(HostileCulture(), "{0}", 1.5m)));
    }
}
