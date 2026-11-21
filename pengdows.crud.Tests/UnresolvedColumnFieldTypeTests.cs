using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using System.Data;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// PERF-014: a column whose provider throws from GetFieldType (Npgsql for a type it has no handler
/// for) should be asked once per call, not twice: each refusal is a thrown exception.
/// </summary>
public sealed class UnresolvedColumnFieldTypeTests
{
    public sealed class Row
    {
        public int Id { get; set; }
        public object? Shape { get; set; }
    }

    private sealed class RefusingReader : fakeDbDataReader
    {
        public int Refusals;

        public RefusingReader(IEnumerable<Dictionary<string, object>> rows) : base(rows)
        {
        }

        public override Type GetFieldType(int ordinal)
        {
            if (GetName(ordinal) == "Shape")
            {
                Refusals++;
                throw new InvalidCastException("no handler");
            }

            return base.GetFieldType(ordinal);
        }
    }

    private static List<Dictionary<string, object>> Rows() => new()
    {
        new() { ["Id"] = 1, ["Shape"] = new byte[] { 1 } }
    };

    // The plan cache is a process-wide LRU that parallel tests also fill, so a "warm" call may find its plan evicted and
    // rebuild it. The invariant is per call, not per cache state: a refused column is asked exactly once whether the plan
    // was just built or reused (PERF-014 is a second ask on either path). Each test uses its own row type so its first call
    // is a genuine cold build.
    public sealed class ColdPlanRow
    {
        public int Id { get; set; }
        public object? Shape { get; set; }
    }

    public sealed class TrackedColdPlanRow
    {
        public int Id { get; set; }
        public object? Shape { get; set; }
    }

    [Fact]
    public async Task DataReaderMapper_EachCall_AsksTheRefusedColumnOnce()
    {
        using (var cold = new RefusingReader(Rows()))
        {
            await DataReaderMapper.LoadAsync<ColdPlanRow>(cold, MapperOptions.Default);
            Assert.Equal(1, cold.Refusals);
        }

        using var warm = new RefusingReader(Rows());
        var result = await DataReaderMapper.LoadAsync<ColdPlanRow>(warm, MapperOptions.Default);

        Assert.Single(result);
        Assert.Equal(1, warm.Refusals);
    }

    private static TrackedReader Track(RefusingReader reader) =>
        new(reader, new Mock<ITrackedConnection>().Object, Mock.Of<IAsyncDisposable>(), false);

    [Fact]
    public async Task DataReaderMapper_TrackedReader_EachCall_AsksTheRefusedColumnOnce()
    {
        var cold = new RefusingReader(Rows());
        await DataReaderMapper.LoadAsync<TrackedColdPlanRow>(Track(cold), MapperOptions.Default);
        Assert.Equal(1, cold.Refusals);

        var warm = new RefusingReader(Rows());
        var result = await DataReaderMapper.LoadAsync<TrackedColdPlanRow>(Track(warm), MapperOptions.Default);

        Assert.Single(result);
        Assert.Equal(1, warm.Refusals);
    }

    [Table("t")]
    public sealed class Entity
    {
        [Id] [Column("Id", DbType.Int32)] public int Id { get; set; }
        [Column("Shape", DbType.Binary)] public byte[]? Shape { get; set; }
    }

    [Fact]
    public void Gateway_CachedPlan_AsksTheRefusedColumnOncePerRead()
    {
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", new fakeDbFactory(SupportedDatabase.Sqlite));
        var gateway = new TableGateway<Entity, int>(context);

        var warm = Track(new RefusingReader(Rows()));
        warm.Read();
        gateway.MapReaderToObject(warm);

        var inner = new RefusingReader(Rows());
        var reader = Track(inner);
        reader.Read();
        gateway.MapReaderToObject(reader);

        Assert.Equal(1, inner.Refusals);
    }
}
