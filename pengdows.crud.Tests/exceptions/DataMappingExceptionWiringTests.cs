using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.exceptions;

/// <summary>
/// Verifies that DataReaderMapper throws DataMappingException (not InvalidOperationException)
/// in strict mode when a column value cannot be mapped to a property.
/// </summary>
public class DataMappingExceptionWiringTests
{
    private class SampleEntity
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public bool IsActive { get; set; }
    }

    private static readonly IEnumerable<Dictionary<string, object>> RowsWithBadAge =
    [
        new Dictionary<string, object>
        {
            ["Name"] = "Alice",
            ["Age"] = "NaN",   // Cannot convert "NaN" to int
            ["IsActive"] = true
        }
    ];

    [Fact]
    public async Task LoadAsync_Strict_ThrowsDataMappingException()
    {
        var reader = new fakeDbDataReader(RowsWithBadAge);
        var options = new MapperOptions(true);

        var ex = await Assert.ThrowsAsync<DataMappingException>(() =>
            DataReaderMapper.LoadAsync<SampleEntity>(reader, options).AsTask());

        Assert.NotNull(ex.InnerException);
        Assert.Equal(SupportedDatabase.Unknown, ex.Database);
        Assert.Contains("Age", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StreamAsync_Strict_ThrowsDataMappingException()
    {
        var reader = new fakeDbDataReader(RowsWithBadAge);
        var options = new MapperOptions(true);
        var stream = DataReaderMapper.StreamAsync<SampleEntity>(reader, options);

        var ex = await Assert.ThrowsAsync<DataMappingException>(async () =>
        {
            await foreach (var _ in stream) { }
        });

        Assert.NotNull(ex.InnerException);
        Assert.Equal(SupportedDatabase.Unknown, ex.Database);
    }
}
