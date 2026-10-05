using System;
using System.Linq;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions.translators;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// REV-039: what code outside the dialects needs about a database comes from the
/// <see cref="DatabaseTraits"/> its dialect declares, looked up by value.
/// </summary>
public sealed class DatabaseTraitsTests
{
    [Fact]
    public void EveryDatabaseHasItsOwnTraits()
    {
        // A database missing from SqlDialectFactory.CreateTraits would silently get the fallback.
        foreach (var database in Enum.GetValues<SupportedDatabase>())
        {
            Assert.Equal(database, DatabaseTraits.For(database).Database);
        }
    }

    [Fact]
    public void AllHoldsOneEntryPerDatabase()
    {
        var databases = DatabaseTraits.All.Select(t => t.Database).ToList();

        Assert.Equal(databases.Distinct().Count(), databases.Count);
        Assert.Equal(Enum.GetValues<SupportedDatabase>().OrderBy(d => d), databases.OrderBy(d => d));
    }

    [Fact]
    public void CombinedFlagsGetTheFallbackTraits()
    {
        var traits = DatabaseTraits.For(SupportedDatabase.MySql | SupportedDatabase.PostgreSql);

        Assert.Equal(SupportedDatabase.Unknown, traits.Database);
        Assert.IsType<FallbackExceptionTranslator>(traits.ExceptionTranslator);
        Assert.Equal(SpatialWireFormat.Unspecified, traits.SpatialFormat);
        Assert.Equal(IntervalWireFormat.Unspecified, traits.IntervalFormat);
        Assert.False(traits.BindsNpgsqlValueTypes);
    }

    [Fact]
    public void ValueFormatsStayOnTheDatabasesThatHadThem()
    {
        var npgsql = Enum.GetValues<SupportedDatabase>().Where(d => DatabaseTraits.For(d).BindsNpgsqlValueTypes);
        Assert.Equal(
            new[] { SupportedDatabase.PostgreSql, SupportedDatabase.CockroachDb, SupportedDatabase.YugabyteDb }.OrderBy(d => d),
            npgsql.OrderBy(d => d));

        var iso = Enum.GetValues<SupportedDatabase>()
            .Where(d => DatabaseTraits.For(d).IntervalFormat == IntervalWireFormat.Iso8601);
        Assert.Equal(new[] { SupportedDatabase.PostgreSql, SupportedDatabase.CockroachDb }.OrderBy(d => d),
            iso.OrderBy(d => d));

        var mySqlInternal = Enum.GetValues<SupportedDatabase>()
            .Where(d => DatabaseTraits.For(d).SpatialFormat == SpatialWireFormat.LittleEndianSridPrefixedWkb);
        Assert.Equal(
            new[] { SupportedDatabase.MySql, SupportedDatabase.MariaDb, SupportedDatabase.AuroraMySql }.OrderBy(d => d),
            mySqlInternal.OrderBy(d => d));
    }
}
