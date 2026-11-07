using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.@internal;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// The NULL-into-non-nullable decision and its error are defined once (NullabilityRules), so the mapper and the gateway raise the
/// same error for the same column.
/// </summary>
public class NullabilityRulesTests
{
    public class Poco
    {
        [Column("v", DbType.String)]
        public string Value { get; set; } = "";
    }

    [Fact]
    public void TheNullErrorNamesTheColumnAndTheType()
    {
        var reader = new fakeDbDataReader(new[] { new System.Collections.Generic.Dictionary<string, object> { ["v"] = System.DBNull.Value } });
        Assert.True(reader.Read());

        var ex = NullabilityRules.NullIntoNonNullableException(reader, 0, "Value", typeof(string));

        var dataMapping = Assert.IsType<DataMappingException>(ex);
        Assert.Contains("'v'", dataMapping.Message);
        Assert.Contains("Value", dataMapping.Message);
    }

    // A failed enum read is checked for NULL on the failure path. A NULL is the error; a value that is present keeps the lenient
    // mode's default; and when the NULL check itself cannot be answered, the original failure is what surfaces, not the check's.
    [Fact]
    public void RejectNullOnFailure_ANullColumn_Throws()
    {
        var reader = new fakeDbDataReader(new[] { new System.Collections.Generic.Dictionary<string, object> { ["v"] = System.DBNull.Value } });
        Assert.True(reader.Read());
        var failure = new System.InvalidCastException("typed read failed");

        Assert.IsType<DataMappingException>(Record.Exception(() =>
            NullabilityRules.RejectNullOnFailure(reader, 0, "Value", typeof(System.DayOfWeek), failure)));
    }

    [Fact]
    public void RejectNullOnFailure_APresentValue_ReturnsSoTheLenientDefaultApplies()
    {
        var reader = new fakeDbDataReader(new[] { new System.Collections.Generic.Dictionary<string, object> { ["v"] = "Nope" } });
        Assert.True(reader.Read());
        var failure = new System.InvalidCastException("not a member");

        Assert.Null(Record.Exception(() =>
            NullabilityRules.RejectNullOnFailure(reader, 0, "Value", typeof(System.DayOfWeek), failure)));
    }

    [Fact]
    public void RejectNullOnFailure_WhenTheNullCheckFails_SurfacesTheOriginalFailure()
    {
        // A provider whose NULL check throws for this column (a NUMERIC beyond decimal, emulated by fakeDb).
        var reader = new fakeDbDataReader(new[] { new System.Collections.Generic.Dictionary<string, object> { ["v"] = "Nope" } })
        {
            OutOfRangeReturnsNullColumns = new System.Collections.Generic.HashSet<string> { "v" },
        };
        Assert.True(reader.Read());
        var failure = new System.InvalidCastException("not a member");

        var thrown = Record.Exception(() =>
            NullabilityRules.RejectNullOnFailure(reader, 0, "Value", typeof(System.DayOfWeek), failure));

        Assert.Same(failure, thrown);
    }
}
