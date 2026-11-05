using System.Collections.Generic;
using System.Data;
using System.Linq;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.coercion;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.TypeFallback;

/// <summary>
/// A list, a string-keyed map and a range array are stored on a database with no such type as their text: a list as its
/// comma-separated values, a map as a JSON object, a multirange as its ranges separated by semicolons. Each is read back by
/// its coercion to the same value.
/// </summary>
public class TextCollectionTests
{
    private static SqlDialect Dialect(SupportedDatabase product) =>
        (SqlDialect)SqlDialectFactory.CreateDialectForType(product, new fakeDbFactory(product),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

    [Fact]
    public void AListIsItsCommaSeparatedValues()
    {
        var parameter = Dialect(SupportedDatabase.Sqlite).CreateDbParameter("p", DbType.Object, new List<int> { 1, 2, 3 });

        Assert.Equal("1,2,3", parameter.Value);
        Assert.True(new ListOfIntCoercion().TryRead(new DbValue("1,2,3"), out var back));
        Assert.Equal(new List<int> { 1, 2, 3 }, back);
    }

    [Fact]
    public void AMapIsItsJsonObjectAndReadsBack()
    {
        var map = new Dictionary<string, object> { ["a"] = "1" };

        var parameter = Dialect(SupportedDatabase.Sqlite).CreateDbParameter("p", DbType.Object, map);

        var text = Assert.IsType<string>(parameter.Value);
        Assert.Equal("{\"a\":\"1\"}", text);
        Assert.True(new JsonObjectCoercion().TryRead(new DbValue(text), out var back));
        Assert.Equal(map, back);
    }

    [Fact]
    public void AMultirangeIsItsRangesSeparatedBySemicolons()
    {
        var ranges = new[] { new Range<int>(1, 5, true, false), new Range<int>(7, 9, true, false) };

        var parameter = Dialect(SupportedDatabase.Sqlite).CreateDbParameter("p", DbType.Object, ranges);

        var text = Assert.IsType<string>(parameter.Value);
        Assert.True(new RangeArrayCoercion().TryRead(new DbValue(text), out var back));
        Assert.Equal(ranges, back);
    }
}
