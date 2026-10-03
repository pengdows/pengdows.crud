using System;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// PERF-013: BuildCreate's fast path cloned the cached INSERT template, parameters included, then
/// cleared the clone and bound the entity's values into new parameters, so each call rented and
/// returned every parameter twice and built the parameter dictionaries twice. It now copies only
/// the template's text, so it costs no more than building the same INSERT by hand.
/// </summary>
public sealed class BuildCreateAllocationTests
{
    [Table("people")]
    public sealed class Person
    {
        [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = "";
        [Column("age", DbType.Int32)] public int Age { get; set; }
        [Column("salary", DbType.Double)] public double Salary { get; set; }
        [Column("is_active", DbType.Boolean)] public bool IsActive { get; set; }
        [Column("created_at", DbType.String)] public string CreatedAt { get; set; } = "";
    }

    private static readonly Person Sample = new()
    {
        Name = "c", Age = 25, Salary = 5, IsActive = true, CreatedAt = "x"
    };

    private static long Measure(Action build)
    {
        build();
        build();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            build();
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / 100;
    }

    [Fact]
    public void BuildCreate_AllocatesNoMoreThanTheSameInsertBuiltByHand()
    {
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite",
            new fakeDbFactory(SupportedDatabase.Sqlite));
        var gateway = new TableGateway<Person, int>(context);
        string sql;
        using (var first = gateway.BuildCreate(Sample))
        {
            sql = first.Query.ToString();
        }

        var viaGateway = AllocationMeasurement.Lowest(() => Measure(() =>
        {
            using var sc = gateway.BuildCreate(Sample);
        }));
        var byHand = AllocationMeasurement.Lowest(() => Measure(() =>
        {
            using var sc = context.CreateSqlContainer(sql);
            sc.AddParameterWithValue("i0", DbType.String, Sample.Name);
            sc.AddParameterWithValue("i1", DbType.Int32, Sample.Age);
            sc.AddParameterWithValue("i2", DbType.Double, Sample.Salary);
            sc.AddParameterWithValue("i3", DbType.Boolean, Sample.IsActive);
            sc.AddParameterWithValue("i4", DbType.String, Sample.CreatedAt);
        }));

        Assert.True(viaGateway <= byHand, $"BuildCreate {viaGateway} B, by hand {byHand} B");
    }
}
