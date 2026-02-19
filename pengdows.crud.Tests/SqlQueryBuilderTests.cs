using System;
using System.Globalization;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlQueryBuilderTests
{
    [Fact]
    public void Append_BuildsExpectedSql()
    {
        var builder = new SqlQueryBuilder();

        builder.Append("SELECT ").Append('1');

        Assert.Equal("SELECT 1", builder.ToString());
        Assert.Equal(8, builder.Length);
    }

    [Fact]
    public void Clear_ReturnsBuilderAndResetsState()
    {
        var builder = new SqlQueryBuilder("SELECT 1");

        var returned = builder.Clear();

        Assert.Same(builder, returned);
        Assert.Equal(string.Empty, builder.ToString());
        Assert.Equal(0, builder.Length);
    }

    [Fact]
    public void Version_IncrementsOnlyOnMutation()
    {
        var builder = new SqlQueryBuilder();
        var v0 = builder.Version;

        builder.Append("A");
        var v1 = builder.Version;

        builder.Append(string.Empty);
        var v2 = builder.Version;

        builder.Clear();
        var v3 = builder.Version;

        Assert.NotEqual(v0, v1);
        Assert.Equal(v1, v2);
        Assert.NotEqual(v2, v3);
    }

    [Fact]
    public void AppendFormat_FormatsContent()
    {
        var builder = new SqlQueryBuilder();

        builder.AppendFormat("SELECT {0} FROM {1}", 1, "table");

        Assert.Equal("SELECT 1 FROM table", builder.ToString());
    }

    [Fact]
    public void CopyFrom_CopiesTextAndVersion()
    {
        var source = new SqlQueryBuilder("SELECT 42");
        var version = source.Version;

        var target = new SqlQueryBuilder();
        target.CopyFrom(source);

        Assert.Equal(source.ToString(), target.ToString());
        Assert.Equal(version, target.Version);
    }

    [Fact]
    public void Replace_ReplacesAllOccurrences()
    {
        var builder = new SqlQueryBuilder("SELECT {0} FROM {0}");
        var version = builder.Version;

        builder.Replace("{0}", "table");

        Assert.Equal("SELECT table FROM table", builder.ToString());
        Assert.NotEqual(version, builder.Version);
    }

    [Fact]
    public void Replace_NullNewValueRemovesMatches()
    {
        var builder = new SqlQueryBuilder("A|B|C");

        builder.Replace("|", null);

        Assert.Equal("ABC", builder.ToString());
    }

    [Fact]
    public void Replace_NoMatchDoesNotChangeVersion()
    {
        var builder = new SqlQueryBuilder("SELECT");
        var version = builder.Version;

        builder.Replace("WHERE", "X");

        Assert.Equal("SELECT", builder.ToString());
        Assert.Equal(version, builder.Version);
    }

    [Fact]
    public void Replace_ThrowsOnNullOrEmptyOldValue()
    {
        var builder = new SqlQueryBuilder("SELECT");

        Assert.Throws<ArgumentNullException>(() => builder.Replace(null!, "X"));
        Assert.Throws<ArgumentException>(() => builder.Replace(string.Empty, "X"));
    }

    [Fact]
    public void Append_Int_ConvertsCorrectly()
    {
        var builder = new SqlQueryBuilder();
        builder.Append(42);
        Assert.Equal("42", builder.ToString());
    }

    [Fact]
    public void Append_NegativeInt_ConvertsCorrectly()
    {
        var builder = new SqlQueryBuilder();
        builder.Append(-1);
        Assert.Equal("-1", builder.ToString());
    }

    [Fact]
    public void Append_Long_ConvertsCorrectly()
    {
        var builder = new SqlQueryBuilder();
        builder.Append(9876543210L);
        Assert.Equal("9876543210", builder.ToString());
    }

    [Fact]
    public void Append_Double_ConvertsCorrectly()
    {
        var builder = new SqlQueryBuilder();
        builder.Append(3.14);
        var result = builder.ToString();
        Assert.Contains("3", result);
        Assert.Contains("14", result);
    }

    [Fact]
    public void Append_Decimal_ConvertsCorrectly()
    {
        var builder = new SqlQueryBuilder();
        builder.Append(1.5m);
        var result = builder.ToString();
        Assert.Contains("1", result);
        Assert.Contains("5", result);
    }

    [Fact]
    public void Append_NullObject_IsNoOp()
    {
        var builder = new SqlQueryBuilder();
        builder.Append((object?)null);
        Assert.Equal(string.Empty, builder.ToString());
        Assert.Equal(0, builder.Length);
    }

    [Fact]
    public void Append_ObjectWithValue_UsesToString()
    {
        var builder = new SqlQueryBuilder();
        builder.Append((object?)123);
        Assert.Equal("123", builder.ToString());
    }

    [Fact]
    public void Append_NullISqlQueryBuilder_IsNoOp()
    {
        var builder = new SqlQueryBuilder("SELECT");
        builder.Append((ISqlQueryBuilder?)null!);
        Assert.Equal("SELECT", builder.ToString());
    }

    [Fact]
    public void Append_EmptySqlQueryBuilder_IsNoOp()
    {
        var builder = new SqlQueryBuilder("SELECT");
        var other = new SqlQueryBuilder(); // empty
        builder.Append((ISqlQueryBuilder)other);
        Assert.Equal("SELECT", builder.ToString());
    }

    [Fact]
    public void Append_AnotherSqlQueryBuilder_CopiesContent()
    {
        var other = new SqlQueryBuilder(" 1");
        var builder = new SqlQueryBuilder("SELECT");
        builder.Append((ISqlQueryBuilder)other);
        Assert.Equal("SELECT 1", builder.ToString());
    }

    [Fact]
    public void AppendLine_NoArgs_AppendsNewline()
    {
        var builder = new SqlQueryBuilder("SELECT");
        builder.AppendLine();
        Assert.Equal("SELECT\n", builder.ToString());
    }

    [Fact]
    public void AppendLine_WithValue_AppendsValueAndNewline()
    {
        var builder = new SqlQueryBuilder();
        builder.AppendLine("SELECT 1");
        Assert.Equal("SELECT 1\n", builder.ToString());
    }

    [Fact]
    public void AppendLine_WithNullValue_AppendsOnlyNewline()
    {
        var builder = new SqlQueryBuilder();
        builder.AppendLine((string?)null);
        Assert.Equal("\n", builder.ToString());
    }

    [Fact]
    public void AppendFormat_WithProvider_FormatsContent()
    {
        var builder = new SqlQueryBuilder();
        builder.AppendFormat(CultureInfo.InvariantCulture, "{0:F2}", 3.14159);
        Assert.Equal("3.14", builder.ToString());
    }

    [Fact]
    public void AppendFormat_NullFormat_Throws()
    {
        var builder = new SqlQueryBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.AppendFormat((string)null!, "arg"));
        Assert.Throws<ArgumentNullException>(() => builder.AppendFormat(CultureInfo.InvariantCulture, (string)null!, "arg"));
    }

    [Fact]
    public void Dispose_TwiceIsNoOp()
    {
        var builder = new SqlQueryBuilder("SELECT 1");
        builder.Dispose();
        // After dispose the buffer is null; ToString should return empty
        Assert.Equal(string.Empty, builder.ToString());
        builder.Dispose(); // second dispose — must not throw
    }

    [Fact]
    public void Clear_OnEmptyBuilder_VersionUnchanged()
    {
        var builder = new SqlQueryBuilder();
        var v = builder.Version;
        builder.Clear();
        Assert.Equal(v, builder.Version);
        Assert.Equal(string.Empty, builder.ToString());
    }

    [Fact]
    public void Replace_OnEmptyBuilder_IsNoOp()
    {
        var builder = new SqlQueryBuilder();
        var v = builder.Version;
        builder.Replace("X", "Y");
        Assert.Equal(v, builder.Version);
        Assert.Equal(string.Empty, builder.ToString());
    }

    [Fact]
    public void Append_LargeString_GrowsBuffer()
    {
        var builder = new SqlQueryBuilder(4); // tiny initial capacity forces Grow()
        var large = new string('A', 1024);
        builder.Append(large);
        Assert.Equal(large, builder.ToString());
        Assert.Equal(1024, builder.Length);
    }

    [Fact]
    public void Constructor_WithZeroCapacity_UsesDefault()
    {
        var builder = new SqlQueryBuilder(0);
        builder.Append("test");
        Assert.Equal("test", builder.ToString());
    }

    [Fact]
    public void Constructor_WithNegativeCapacity_UsesDefault()
    {
        var builder = new SqlQueryBuilder(-5);
        builder.Append("test");
        Assert.Equal("test", builder.ToString());
    }

    [Fact]
    public void Replace_SingleOccurrence_Replaces()
    {
        var builder = new SqlQueryBuilder("Hello World");
        builder.Replace("World", "SQL");
        Assert.Equal("Hello SQL", builder.ToString());
    }

    [Fact]
    public void Replace_WithLongerValue_Expands()
    {
        var builder = new SqlQueryBuilder("A B C");
        builder.Replace("B", "HELLO");
        Assert.Equal("A HELLO C", builder.ToString());
    }

    [Fact]
    public void Replace_WithShorterValue_Contracts()
    {
        var builder = new SqlQueryBuilder("AABAA");
        builder.Replace("AA", "X");
        Assert.Equal("XBX", builder.ToString());
    }

    [Fact]
    public void CopyFrom_ThrowsOnNull()
    {
        var builder = new SqlQueryBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.CopyFrom(null!));
    }

    [Fact]
    public void CopyFrom_EmptySource_ResultsInEmptyTarget()
    {
        var source = new SqlQueryBuilder();
        var target = new SqlQueryBuilder("existing");
        target.CopyFrom(source);
        Assert.Equal(string.Empty, target.ToString());
        Assert.Equal(0, target.Length);
    }

    [Fact]
    public void Constructor_WithInitialString_HasContent()
    {
        var builder = new SqlQueryBuilder("SELECT 1");
        Assert.Equal("SELECT 1", builder.ToString());
        Assert.Equal(8, builder.Length);
    }

    [Fact]
    public void ToString_OnEmpty_ReturnsEmptyString()
    {
        var builder = new SqlQueryBuilder();
        Assert.Equal(string.Empty, builder.ToString());
    }

    [Fact]
    public void Replace_WithEmptyNewValue_RemovesOldValue()
    {
        var builder = new SqlQueryBuilder("SELECT X FROM X");
        builder.Replace("X", string.Empty);
        Assert.Equal("SELECT  FROM ", builder.ToString());
    }
}
