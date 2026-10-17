using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;

namespace pengdows.crud.Tests;

/// <summary>
/// Real objects for the places a test needs an argument but not behaviour from it. Moq is never used in this repository:
/// fakeDb is the test provider, and anything it cannot do is added to fakeDb, not mocked.
/// </summary>
internal static class TestDoubles
{
    /// <summary>A real tracked connection over a fakeDb connection.</summary>
    public static TrackedConnection Connection() => new(new fakeDbConnection());

    /// <summary>A real tracked reader over a one-row fakeDb reader.</summary>
    public static TrackedReader Reader() => new(
        new fakeDbDataReader(new List<Dictionary<string, object>> { new() { ["Value"] = 1 } }),
        Connection(), Disposable(), false);

    /// <summary>An <see cref="IAsyncDisposable"/> that does nothing and counts being disposed.</summary>
    public static CountingAsyncDisposable Disposable() => new();

    /// <summary>A real column description (not JSON, not an enum) named <c>col1</c>, from a registered entity.</summary>
    public static IColumnInfo Column() =>
        new TypeMapRegistry().GetTableInfo<OneColumn>().Columns["col1"];

    [attributes.Table("test_doubles_one_column")]
    private sealed class OneColumn
    {
        [attributes.Id] [attributes.Column("id", System.Data.DbType.Int32)] public int Id { get; set; }
        [attributes.Column("col1", System.Data.DbType.String)] public string Col1 { get; set; } = "";
    }
}

/// <summary>An <see cref="IAsyncDisposable"/> that records how many times it was disposed.</summary>
internal sealed class CountingAsyncDisposable : IAsyncDisposable
{
    private int _disposed;

    public int DisposeCount => _disposed;

    public ValueTask DisposeAsync()
    {
        _disposed++;
        return ValueTask.CompletedTask;
    }
}
