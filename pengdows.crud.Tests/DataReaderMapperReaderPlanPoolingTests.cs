#region

using System.Collections.Generic;
using System.Threading.Tasks;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

// Code-review finding: DataReaderMapper.BuildSchemaShape allocated two fresh arrays
// (names/types) on EVERY call — including cache-hit calls, the hot path — unlike
// BaseTableGateway.Reader.cs's sibling plan cache, which rents lookup arrays from an
// ArrayPool and only allocates real copies on an actual cache miss (see RecordsetShape.Persist).
// These tests exercise the pooled rent/return path (<=64 fields), the ArrayPool.Shared fallback
// for wider schemas (>64 fields), and repeated calls with the same shape, to catch any
// rent/return lifecycle bug (e.g. a returned buffer's contents corrupting a later lookup)
// introduced by bringing DataReaderMapper in line with that pattern.
[Collection("MapperCacheSerial")]
public class DataReaderMapperReaderPlanPoolingTests
{
    [Fact]
    public async Task LoadAsync_WithSchemaWiderThanPoolMaxLength_MapsCorrectlyViaSharedArrayPoolFallback()
    {
        var row = new Dictionary<string, object>();
        for (var i = 0; i < 70; i++)
        {
            row[$"Column{i}"] = $"Value{i}";
        }

        var reader = new fakeDbDataReader(new[] { row });

        var results = await DataReaderMapper.LoadAsync<WideEntity>(reader, MapperOptions.Default);

        var entity = Assert.Single(results);
        Assert.Equal("Value0", entity.Column0);
        Assert.Equal("Value35", entity.Column35);
        Assert.Equal("Value69", entity.Column69);
    }

    [Fact]
    public async Task LoadAsync_CalledRepeatedlyWithSameNarrowShape_ReusesCachedPlanAndMapsCorrectlyEveryTime()
    {
        for (var iteration = 0; iteration < 25; iteration++)
        {
            var reader = new fakeDbDataReader(new[]
            {
                new Dictionary<string, object>
                {
                    ["Column0"] = $"iteration-{iteration}"
                }
            });

            var results = await DataReaderMapper.LoadAsync<NarrowEntity>(reader, MapperOptions.Default);

            var entity = Assert.Single(results);
            Assert.Equal($"iteration-{iteration}", entity.Column0);
        }
    }

    private sealed class NarrowEntity
    {
        public string? Column0 { get; set; }
    }

    private sealed class WideEntity
    {
        public string? Column0 { get; set; }
        public string? Column1 { get; set; }
        public string? Column2 { get; set; }
        public string? Column3 { get; set; }
        public string? Column4 { get; set; }
        public string? Column5 { get; set; }
        public string? Column6 { get; set; }
        public string? Column7 { get; set; }
        public string? Column8 { get; set; }
        public string? Column9 { get; set; }
        public string? Column10 { get; set; }
        public string? Column11 { get; set; }
        public string? Column12 { get; set; }
        public string? Column13 { get; set; }
        public string? Column14 { get; set; }
        public string? Column15 { get; set; }
        public string? Column16 { get; set; }
        public string? Column17 { get; set; }
        public string? Column18 { get; set; }
        public string? Column19 { get; set; }
        public string? Column20 { get; set; }
        public string? Column21 { get; set; }
        public string? Column22 { get; set; }
        public string? Column23 { get; set; }
        public string? Column24 { get; set; }
        public string? Column25 { get; set; }
        public string? Column26 { get; set; }
        public string? Column27 { get; set; }
        public string? Column28 { get; set; }
        public string? Column29 { get; set; }
        public string? Column30 { get; set; }
        public string? Column31 { get; set; }
        public string? Column32 { get; set; }
        public string? Column33 { get; set; }
        public string? Column34 { get; set; }
        public string? Column35 { get; set; }
        public string? Column36 { get; set; }
        public string? Column37 { get; set; }
        public string? Column38 { get; set; }
        public string? Column39 { get; set; }
        public string? Column40 { get; set; }
        public string? Column41 { get; set; }
        public string? Column42 { get; set; }
        public string? Column43 { get; set; }
        public string? Column44 { get; set; }
        public string? Column45 { get; set; }
        public string? Column46 { get; set; }
        public string? Column47 { get; set; }
        public string? Column48 { get; set; }
        public string? Column49 { get; set; }
        public string? Column50 { get; set; }
        public string? Column51 { get; set; }
        public string? Column52 { get; set; }
        public string? Column53 { get; set; }
        public string? Column54 { get; set; }
        public string? Column55 { get; set; }
        public string? Column56 { get; set; }
        public string? Column57 { get; set; }
        public string? Column58 { get; set; }
        public string? Column59 { get; set; }
        public string? Column60 { get; set; }
        public string? Column61 { get; set; }
        public string? Column62 { get; set; }
        public string? Column63 { get; set; }
        public string? Column64 { get; set; }
        public string? Column65 { get; set; }
        public string? Column66 { get; set; }
        public string? Column67 { get; set; }
        public string? Column68 { get; set; }
        public string? Column69 { get; set; }
    }
}
