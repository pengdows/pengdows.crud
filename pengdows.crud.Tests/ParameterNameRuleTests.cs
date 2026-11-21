using System;
using System.Collections.Generic;
using System.Data;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// A parameter name is written into SQL text, so it must be a plain identifier: start with an ASCII letter, be
/// at most 63 characters, and contain only ASCII letters, digits and underscores. Names that break the rule are
/// refused when they are added, before any SQL can carry them.
/// </summary>
public sealed class ParameterNameRuleTests
{
    private static SqlContainer NewContainer(SupportedDatabase database = SupportedDatabase.Sqlite)
    {
        var factory = new fakeDbFactory(database);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={database}", factory);
        return (SqlContainer)context.CreateSqlContainer("SELECT 1");
    }

    [Theory]
    [InlineData("p1")]
    [InlineData("A")]
    [InlineData("customer_id")]
    [InlineData("a_1")]
    [InlineData("Id")]
    [InlineData("@p1")]
    [InlineData(":p1")]
    public void ValidName_IsAccepted(string name)
    {
        using var sc = NewContainer();

        var parameter = sc.AddParameterWithValue(name, DbType.Int32, 1);

        Assert.NotNull(parameter);
    }

    // Db2 does not state a parameter-name limit, so the library's 63-character fallback applies.
    [Fact]
    public void NoProviderLimit_FallsBackTo63Characters_Accepted()
    {
        using var sc = NewContainer(SupportedDatabase.Db2);
        var name = "a" + new string('b', 62);

        var parameter = sc.AddParameterWithValue(name, DbType.Int32, 1);

        Assert.Equal(63, parameter.ParameterName.Length);
    }

    [Fact]
    public void NoProviderLimit_FallsBackTo63Characters_RejectsLonger()
    {
        using var sc = NewContainer(SupportedDatabase.Db2);
        var name = "a" + new string('b', 63);

        Assert.Throws<ArgumentException>(() => sc.AddParameterWithValue(name, DbType.Int32, 1));
    }

    [Fact]
    public void DialectWithoutAStatedLimit_ReportsTheFallbackOf63()
    {
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Db2", new fakeDbFactory(SupportedDatabase.Db2));

        Assert.Equal(63, context.DataSourceInfo.ParameterNameMaxLength);
    }

    // Oracle states 30, so the provider's limit replaces the fallback.
    [Fact]
    public void ProviderLimit_Oracle30_AcceptsThirtyAndRejectsThirtyOne()
    {
        using var accepted = NewContainer(SupportedDatabase.Oracle);
        Assert.NotNull(accepted.AddParameterWithValue("a" + new string('b', 29), DbType.Int32, 1));

        using var refused = NewContainer(SupportedDatabase.Oracle);
        Assert.Throws<ArgumentException>(() =>
            refused.AddParameterWithValue("a" + new string('b', 30), DbType.Int32, 1));
    }

    // SQLite states 255, so a name longer than the fallback is accepted when the provider allows it.
    [Fact]
    public void ProviderLimit_Sqlite255_AcceptsNamesLongerThan63()
    {
        using var sc = NewContainer(SupportedDatabase.Sqlite);
        var name = "a" + new string('b', 200);

        Assert.NotNull(sc.AddParameterWithValue(name, DbType.Int32, 1));
    }

    [Theory]
    [InlineData("1abc")]        // starts with a digit
    [InlineData("_leading")]    // starts with an underscore
    [InlineData("a b")]         // space
    [InlineData("a-b")]         // hyphen
    [InlineData("a;DROP")]      // statement separator
    [InlineData("a'")]          // quote
    [InlineData("a\"")]         // double quote
    [InlineData("a)")]          // closing parenthesis
    [InlineData("a--")]         // comment marker
    [InlineData("a.b")]         // dot
    [InlineData("café")]   // non-ASCII letter
    public void InvalidName_IsRejectedWithArgumentException(string name)
    {
        using var sc = NewContainer();

        Assert.Throws<ArgumentException>(() => sc.AddParameterWithValue(name, DbType.Int32, 1));
    }

    [Fact]
    public void GeneratedName_IsEightCharacters()
    {
        using var sc = NewContainer(SupportedDatabase.Sqlite);

        var parameter = sc.AddParameterWithValue(null, DbType.Int32, 1);

        Assert.Equal(8, parameter.ParameterName.Length);
    }

    [Fact]
    public void GeneratedName_NeverExceedsAShorterDialectLimit()
    {
        using var sc = NewContainer(SupportedDatabase.Oracle); // Oracle states 30, so 8 still fits
        Assert.Equal(8, sc.AddParameterWithValue(null, DbType.Int32, 1).ParameterName.Length);
    }

    // An injection-shaped name is refused at the point it is added, through every entry point, and nothing is stored.
    [Fact]
    public void InjectionShapedName_ViaAddParameterWithValue_IsRefusedAndNothingIsStored()
    {
        using var sc = NewContainer();

        Assert.Throws<ArgumentException>(() => sc.AddParameterWithValue("fred; --", DbType.Int32, 1));

        Assert.Equal(0, sc.ParameterCount);
    }

    // The dialect refuses "fred; --" when the DbParameter is created, before it can reach a container.
    [Fact]
    public void InjectionShapedName_ViaCreateDbParameter_IsRefused()
    {
        using var sc = NewContainer();

        Assert.Throws<ArgumentException>(() => sc.CreateDbParameter("fred; --", DbType.Int32, 1));
    }

    // The dialect checks characters but no length. A 256-character name is well-formed to the dialect and is
    // refused only by the container's rule (SQLite states 255), at AddParameter.
    [Fact]
    public void NameTheDialectAllows_ButTheRuleRefuses_IsRefusedAtAddParameter()
    {
        using var sc = NewContainer();
        var parameter = sc.CreateDbParameter("a" + new string('b', 255), DbType.Int32, 1);

        Assert.Throws<ArgumentException>(() => sc.AddParameter(parameter));

        Assert.Equal(0, sc.ParameterCount);
    }

    // A refused name never reaches the parameter collection, so no placeholder can resolve to it.
    [Fact]
    public void InjectionShapedName_IsNotResolvableAfterRefusal()
    {
        using var sc = NewContainer();
        Assert.Throws<ArgumentException>(() => sc.AddParameterWithValue("fred; --", DbType.Int32, 1));

        Assert.Throws<KeyNotFoundException>(() => sc.GetParameterValue("fred; --"));
    }

    // MakeParameterName writes the name into SQL text, so it applies the same rule as AddParameter.
    [Theory]
    [InlineData("fred; --")]
    [InlineData("1abc")]
    [InlineData("a b")]
    public void MakeParameterName_RefusesABadName_BeforeWritingItIntoSql(string name)
    {
        using var sc = NewContainer();

        Assert.Throws<ArgumentException>(() => sc.MakeParameterName(name));
    }

    [Fact]
    public void MakeParameterName_RefusesANameOverTheDialectLimit()
    {
        using var sc = NewContainer(SupportedDatabase.Oracle); // Oracle states 30

        Assert.Throws<ArgumentException>(() => sc.MakeParameterName("a" + new string('b', 30)));
    }

    [Theory]
    [InlineData("p1", "@p1")]        // SQLite's marker is @
    [InlineData("@p1", "@p1")]
    [InlineData("customer_id", "@customer_id")]
    public void MakeParameterName_ValidName_IsRenderedWithTheMarker(string name, string expected)
    {
        using var sc = NewContainer();

        Assert.Equal(expected, sc.MakeParameterName(name));
    }

    // A positional provider writes "?" and never the name, so a bad name cannot reach the SQL text.
    [Fact]
    public void MakeParameterName_PositionalProvider_WritesQuestionMark_WhateverTheName()
    {
        using var sc = NewContainer(SupportedDatabase.Access);

        Assert.Equal("?", sc.MakeParameterName("fred; --"));
    }

    [Fact]
    public void NullName_IsStillGeneratedNotRejected()
    {
        using var sc = NewContainer();

        var parameter = sc.AddParameterWithValue(null, DbType.Int32, 1);

        Assert.False(string.IsNullOrEmpty(parameter.ParameterName));
    }
}
