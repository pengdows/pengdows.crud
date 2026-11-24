using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

// DataReaderMapper maps a row with a plan resolved once per result-set shape. The plan maps the whole row in one
// compiled body (no delegate call per column); these tests pin the behavior that body must keep.
[Collection("TypeRegistry")]
public class DataReaderMapperRowBodyTests
{
    public sealed class Row
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
        public bool IsActive { get; set; }
        public string Tail { get; set; } = "unset";
    }

    private static fakeDbDataReader Reader(object age) => new(new List<Dictionary<string, object>>
    {
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = "Alice", ["Age"] = age, ["IsActive"] = true, ["Tail"] = "end"
        }
    });

    // Review policy Ring 1 P0: lenient mode no longer keeps going past a failed column with a default. The failure throws,
    // naming the column, as strict mode does.
    [Fact]
    public async Task LenientMode_AFailingColumn_ThrowsNamingIt()
    {
        var original = TypeCoercionHelper.Logger;
        try
        {
            TypeCoercionHelper.Logger = NullLogger.Instance;

            var error = await Assert.ThrowsAsync<DataMappingException>(
                () => DataReaderMapper.LoadAsync<Row>(Reader("NaN"), MapperOptions.Default).AsTask());

            Assert.Contains("'age'", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TypeCoercionHelper.Logger = original;
        }
    }

    [Fact]
    public async Task StrictAndLenientCallers_ShareOnePlan_AndBothFailTheSameWay()
    {
        var original = TypeCoercionHelper.Logger;
        try
        {
            TypeCoercionHelper.Logger = NullLogger.Instance;

            await Assert.ThrowsAsync<DataMappingException>(async () =>
                await DataReaderMapper.LoadAsync<Row>(Reader("NaN"), MapperOptions.Default));

            await Assert.ThrowsAsync<DataMappingException>(async () =>
                await DataReaderMapper.LoadAsync<Row>(Reader("NaN"), new MapperOptions { Strict = true }));
        }
        finally
        {
            TypeCoercionHelper.Logger = original;
        }
    }

    [Fact]
    public async Task StrictMode_NamesTheColumnAndTheProperty_AndKeepsTheCause()
    {
        var ex = await Assert.ThrowsAsync<DataMappingException>(async () =>
            await DataReaderMapper.LoadAsync<Row>(Reader("NaN"), new MapperOptions { Strict = true }));

        Assert.Contains("'Age'", ex.Message, StringComparison.Ordinal);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void ResolveRowMapper_IsOneCompiledBody_NotALoopOverPerColumnDelegates()
    {
        using var reader = Reader(30);

        var mapper = DataReaderMapper.ResolveRowMapper<Row>(reader, MapperOptions.Default);

        // A compiled expression's delegate closes over System.Runtime.CompilerServices.Closure; a loop closing
        // over the plan is a compiler-generated display class.
        Assert.Equal("System.Runtime.CompilerServices.Closure", mapper.Target?.GetType().FullName);
    }
}
