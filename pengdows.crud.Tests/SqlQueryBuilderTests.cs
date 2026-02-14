using System;
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
}
