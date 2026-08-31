using System;
using System.Data.Common;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

// Found during a fakeDb documentation/behavior audit: SetParameter(string, DbParameter) threw
// NotImplementedException (reachable through DbParameterCollection's own base indexer setter),
// SyncRoot allocated a fresh object on every access instead of a stable one, and every
// name-based lookup was case-sensitive — unlike real ADO.NET provider parameter collections
// (e.g. SqlParameterCollection), which resolve parameter names case-insensitively.
public class FakeParameterCollectionTests
{
    private static DbParameter MakeParameter(string name, object? value)
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        using var cmd = conn.CreateCommand();
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        return p;
    }

    [Fact]
    public void BaseIndexerSetByName_ReplacesExistingParameter()
    {
        var collection = new FakeParameterCollection();
        var original = MakeParameter("@id", 1);
        collection.Add(original);

        DbParameterCollection baseCollection = collection;
        var replacement = MakeParameter("@id", 2);
        baseCollection["@id"] = replacement;

        Assert.Same(replacement, collection["@id"]);
        Assert.Equal(1, collection.Count);
    }

    [Fact]
    public void BaseIndexerSetByName_UnknownName_Throws()
    {
        var collection = new FakeParameterCollection();
        DbParameterCollection baseCollection = collection;

        Assert.Throws<IndexOutOfRangeException>(() => baseCollection["@missing"] = MakeParameter("@missing", 1));
    }

    [Fact]
    public void SyncRoot_ReturnsSameInstance_AcrossMultipleCalls()
    {
        var collection = new FakeParameterCollection();

        Assert.Same(collection.SyncRoot, collection.SyncRoot);
    }

    [Fact]
    public void Lookup_IsCaseInsensitive()
    {
        var collection = new FakeParameterCollection();
        collection.Add(MakeParameter("@Id", 1));

        Assert.True(collection.Contains("@id"));
        Assert.Equal(0, collection.IndexOf("@ID"));
        Assert.NotNull(collection["@id"]);
    }
}
