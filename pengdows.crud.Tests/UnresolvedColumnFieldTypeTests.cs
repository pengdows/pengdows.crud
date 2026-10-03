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

    [Fact]
    public async Task DataReaderMapper_CachedPlanCall_AsksTheRefusedColumnOnce()
    {
        using (var warm = new RefusingReader(Rows()))
        {
            await DataReaderMapper.LoadAsync<Row>(warm, MapperOptions.Default);
        }

        using var reader = new RefusingReader(Rows());
        var result = await DataReaderMapper.LoadAsync<Row>(reader, MapperOptions.Default);

        Assert.Single(result);
        Assert.Equal(1, reader.Refusals);
    }

    private static TrackedReader Track(RefusingReader reader) =>
        new(reader, new Mock<ITrackedConnection>().Object, Mock.Of<IAsyncDisposable>(), false);

    [Fact]
    public async Task DataReaderMapper_TrackedReader_CachedPlanCall_AsksTheRefusedColumnOnce()
    {
        var warmInner = new RefusingReader(Rows());
        await DataReaderMapper.LoadAsync<Row>(Track(warmInner), MapperOptions.Default);

        var inner = new RefusingReader(Rows());
        var result = await DataReaderMapper.LoadAsync<Row>(Track(inner), MapperOptions.Default);

        Assert.Single(result);
        Assert.Equal(1, inner.Refusals);
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
