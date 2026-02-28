using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class TableGatewayRecordsetPlanTests : SqlLiteContextTestBase
{
    public TableGatewayRecordsetPlanTests()
    {
        TypeMap.Register<NameEntity>();
    }

    [Fact]
    public void MapReaderToObject_DifferentFieldTypes_BuildsSeparatePlans()
    {
        var helper = new TableGateway<NameEntity, int>(Context);

        var rows1 = new[]
        {
            new Dictionary<string, object>
            {
                ["Id"] = 1,
                ["Name"] = "Alice"
            }
        };
        using var reader1 = new FakeTrackedReader(rows1);
        reader1.Read();
        var e1 = helper.MapReaderToObject(reader1);
        Assert.Equal("Alice", e1.Name);

        var rows2 = new[]
        {
            new Dictionary<string, object>
            {
                ["Id"] = 2,
                ["Name"] = 123
            }
        };
        using var reader2 = new FakeTrackedReader(rows2);
        reader2.Read();
        var e2 = helper.MapReaderToObject(reader2);
        Assert.Equal("123", e2.Name);
    }

    [Table("NameEntity")]
    private class NameEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", DbType.String)] public string? Name { get; set; }
    }

    private sealed class FakeTrackedReader : fakeDbDataReader, ITrackedReader
    {
        public FakeTrackedReader(IEnumerable<Dictionary<string, object>> rows) : base(rows)
        {
        }

        public new Task<bool> ReadAsync()
        {
            return base.ReadAsync(CancellationToken.None);
        }

        public override ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public override Type GetFieldType(int ordinal)
        {
            var value = GetValue(ordinal);
            return value?.GetType() ?? typeof(object);
        }
    }
}