using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

// pengdows.crud maps rows by two separate paths, and each is specified here on its own:
//   1. DataReaderMapper (arbitrary POCOs): a plan resolved once per result-set shape, run per row.
//   2. CompiledMapperFactory (TableGateway entities): one compiled delegate per shape.
// ResolveRowMapper exposes path 1's resolved per-row mapper so it can be measured and tested without the
// per-call shape and plan lookup LoadAsync does.
public class DataReaderMapperRowMapperTests
{
    private static readonly IReadOnlyList<fakeDbColumn> Columns =
    [
        new("Id", typeof(int), "INTEGER"),
        new("Name", typeof(string), "TEXT"),
        new("Score", typeof(double), "DOUBLE"),
        new("Note", typeof(string), "TEXT")
    ];

    private static fakeDbDataReader Reader() => new(new List<Dictionary<string, object>>
    {
        new(StringComparer.OrdinalIgnoreCase) { ["Id"] = 1, ["Name"] = "one", ["Score"] = 1.5d, ["Note"] = DBNull.Value },
        new(StringComparer.OrdinalIgnoreCase) { ["Id"] = 2, ["Name"] = "two", ["Score"] = 2.5d, ["Note"] = "n2" }
    }) { Columns = Columns };

    // Counts how often the schema is inspected: shape building asks for every column's type.
    private sealed class CountingReader : fakeDbDataReader
    {
        public CountingReader(IEnumerable<Dictionary<string, object>> rows) : base(rows)
        {
        }

        public int FieldTypeCalls { get; private set; }

        public override Type GetFieldType(int ordinal)
        {
            FieldTypeCalls++;
            return base.GetFieldType(ordinal);
        }
    }

    public sealed class Plain
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public double Score { get; set; }
        public string? Note { get; set; } = "unset";
    }

    [Table("rowmapper_entity")]
    public sealed class Attributed
    {
        [Id(false)]
        [Column("Id", System.Data.DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", System.Data.DbType.String)]
        public string Name { get; set; } = string.Empty;

        [Column("Score", System.Data.DbType.Double)]
        public double Score { get; set; }

        [Column("Note", System.Data.DbType.String)]
        public string? Note { get; set; } = "unset";
    }

    [Fact]
    public async Task ReaderMapperPath_ResolveRowMapper_MapsEachRowLikeLoadAsync()
    {
        var expected = await DataReaderMapper.LoadAsync<Plain>(Reader(), MapperOptions.Default);

        using var reader = Reader();
        var mapper = DataReaderMapper.ResolveRowMapper<Plain>(reader, MapperOptions.Default);
        var actual = new List<Plain>();
        while (reader.Read())
        {
            actual.Add(mapper(reader));
        }

        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Id, actual[i].Id);
            Assert.Equal(expected[i].Name, actual[i].Name);
            Assert.Equal(expected[i].Score, actual[i].Score);
            Assert.Equal(expected[i].Note, actual[i].Note);
        }

        Assert.Equal("unset", actual[0].Note); // NULL leaves the property alone, as LoadAsync does
        Assert.Equal("n2", actual[1].Note);
    }

    [Fact]
    public void ReaderMapperPath_ResolveRowMapper_ResolvesTheMappingOnce_NotPerRow()
    {
        using var reader = new CountingReader(new List<Dictionary<string, object>>
        {
            new(StringComparer.OrdinalIgnoreCase) { ["Id"] = 1, ["Name"] = "one", ["Score"] = 1.5d, ["Note"] = "a" },
            new(StringComparer.OrdinalIgnoreCase) { ["Id"] = 2, ["Name"] = "two", ["Score"] = 2.5d, ["Note"] = "b" }
        }) { Columns = Columns };
        var mapper = DataReaderMapper.ResolveRowMapper<Plain>(reader, MapperOptions.Default);
        var before = reader.FieldTypeCalls;
        Assert.True(before > 0); // resolving did inspect the schema

        while (reader.Read())
        {
            mapper(reader);
        }

        Assert.Equal(before, reader.FieldTypeCalls);
    }

    [Fact]
    public void CompiledMapperPath_MapsTheSameRowsAsTheReaderMapperPath()
    {
        var columns = new TypeMapRegistry().GetTableInfo<Attributed>().Columns;
        string[] names = ["Id", "Name", "Score", "Note"];
        Type[] types = [typeof(int), typeof(string), typeof(double), typeof(string)];

        using var compiledReader = Reader();
        var compiled = CompiledMapperFactory<Attributed>.Create<DbDataReader>(
            compiledReader, columns, EnumParseFailureMode.Throw, names, types);
        using var planReader = Reader();
        var plan = DataReaderMapper.ResolveRowMapper<Attributed>(planReader, MapperOptions.Default);

        while (compiledReader.Read() && planReader.Read())
        {
            var a = compiled(compiledReader);
            var b = plan(planReader);
            Assert.Equal(a.Id, b.Id);
            Assert.Equal(a.Name, b.Name);
            Assert.Equal(a.Score, b.Score);
            Assert.Equal(a.Note, b.Note);
        }
    }
}
