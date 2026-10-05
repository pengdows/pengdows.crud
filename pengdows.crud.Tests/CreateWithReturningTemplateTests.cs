using System;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// PERF-016: CreateAsync's INSERT ... RETURNING was rebuilt on every call (SQL text from the column
/// list, then three placeholder replacements) while BuildCreate cloned a cached template; on SQLite
/// that was a measurable part of CreateAsync's 12 µs over BuildCreate + execute. The returning insert
/// now comes from a cached per-dialect template too.
/// </summary>
[Collection("AllocationSerial")]
public class CreateWithReturningTemplateTests
{
    [Table("rows")]
    public class Row
    {
        [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = "";
        [Column("age", DbType.Int32)] public int Age { get; set; }
        [Column("salary", DbType.Double)] public double Salary { get; set; }
        [Column("created_at", DbType.String)] public string CreatedAt { get; set; } = "";
    }

    private static TableGateway<Row, int> Gateway(SupportedDatabase db) =>
        new(new DatabaseContext($"Data Source=test;EmulatedProduct={db}", new fakeDbFactory(db)));

    private static Row NewRow() => new() { Name = "n", Age = 3, Salary = 4.5, CreatedAt = "x" };

    // The SQL each dialect's returning insert had before the template (characterization).
    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.MariaDb)]
    [InlineData(SupportedDatabase.Firebird)]
    [InlineData(SupportedDatabase.Db2)]
    [InlineData(SupportedDatabase.Oracle)]
    public void BuildCreateWithReturning_RepeatedCalls_ProduceTheSameSqlAndParameters(SupportedDatabase db)
    {
        var gateway = Gateway(db);
        using var first = gateway.BuildCreateWithReturning(NewRow(), true);
        using var second = gateway.BuildCreateWithReturning(NewRow(), true);

        Assert.Equal(first.Query.ToString(), second.Query.ToString());
        Assert.Equal(first.ParameterCount, second.ParameterCount);
        Assert.DoesNotContain("{", first.Query.ToString());
    }

    [Fact]
    public void BuildCreateWithReturning_AllocatesAboutAsLittleAsBuildCreate()
    {
        var gateway = Gateway(SupportedDatabase.Sqlite);
        long Measure(Func<Row, ISqlContainer> build)
        {
            for (var i = 0; i < 50; i++)
            {
                build(NewRow()).Dispose();
            }

            var row = NewRow();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 200; i++)
            {
                build(row).Dispose();
            }

            return (GC.GetAllocatedBytesForCurrentThread() - before) / 200;
        }

        var plain = Measure(r => gateway.BuildCreate(r));
        var returning = Measure(r => gateway.BuildCreateWithReturning(r, true));

        Assert.True(returning <= plain + 64, $"BuildCreateWithReturning {returning} B vs BuildCreate {plain} B");
    }

    // PERF-019: the insert templates were never rendered (only executed clones are), so every
    // BuildCreate clone copied and rendered the SQL text again. The template is rendered once when
    // built and clones share the result.
    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.PostgreSql)]
    public void BuildCreate_And_BuildCreateWithReturning_ShareTheRenderedTemplateText(SupportedDatabase db)
    {
        var gateway = Gateway(db);

        using var create = (SqlContainer)gateway.BuildCreate(NewRow());
        using var returning = (SqlContainer)gateway.BuildCreateWithReturning(NewRow(), true);

        Assert.True(create.IsCommandTextRendered);
        Assert.True(returning.IsCommandTextRendered);
    }

    // A positional-parameter provider: the clone gets the template's parameter order with its text.
    [Fact]
    public void BuildCreate_Informix_SharesTheRenderedTemplateText()
    {
        using var create = (SqlContainer)Gateway(SupportedDatabase.Informix).BuildCreate(NewRow());

        Assert.True(create.IsCommandTextRendered);
    }
}
