using System;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

public class CompiledBinderFactoryTests
{
    [Table("BinderTests")]
    public class BinderEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = "";

        [Column("age", DbType.Int32)]
        public int Age { get; set; }

        [Json]
        [Column("data", DbType.String)]
        public Dictionary<string, string> Data { get; set; } = new();

        [Column("status", DbType.String)]
        public Status Status { get; set; }
    }

    public enum Status { Active, Inactive }

    [Table("BinderTestsNumericEnum")]
    public class BinderEntityNumericEnum
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("status", DbType.Int32)]
        public Status Status { get; set; }
    }

    private readonly IReadOnlyList<IColumnInfo> _columns;
    private readonly ISqlDialect _dialect;

    public CompiledBinderFactoryTests()
    {
        var typeMap = new TypeMapRegistry();
        var tableInfo = typeMap.GetTableInfo<BinderEntity>();
        _columns = tableInfo.OrderedColumns;
        _dialect = new SqliteDialect(fakeDbFactory.Instance, NullLogger.Instance);
    }

    [Fact]
    public void CreateInsertBinder_PopulatesParametersCorrectly()
    {
        var entity = new BinderEntity
        {
            Id = 1,
            Name = "Test",
            Age = 30,
            Data = new Dictionary<string, string> { ["key"] = "value" },
            Status = Status.Active
        };

        var paramNames = new[] { "p0", "p1", "p2", "p3", "p4" };
        var binder = CompiledBinderFactory<BinderEntity>.CreateInsertBinder(_columns, paramNames, _dialect);

        var parameters = new List<DbParameter>();
        var count = binder(entity, parameters);

        Assert.Equal(5, count);
        Assert.Equal(5, parameters.Count);
        Assert.Equal("Test", parameters[1].Value);
        Assert.Equal(30, parameters[2].Value);
        Assert.Contains("{\"key\":\"value\"}", (string)parameters[3].Value!);
        Assert.Equal("Active", parameters[4].Value!.ToString());
    }

    [Fact]
    public void CreateUpdateBinder_PopulatesChangedParametersCorrectly()
    {
        var data = new Dictionary<string, string>();
        var updated = new BinderEntity
        {
            Id = 1,
            Name = "New Name",
            Age = 30, // Unchanged
            Data = data, // Unchanged instance
            Status = Status.Inactive // Changed
        };

        var original = new BinderEntity
        {
            Id = 1,
            Name = "Old Name",
            Age = 30,
            Data = data,
            Status = Status.Active
        };

        // Update columns are usually Name, Age, Data, Status (excluding Id)
        var updateColumns = _columns.Where(c => !c.IsId).ToList();
        var paramNames = new[] { "p0", "p1", "p2", "p3" };
        
        var binder = CompiledBinderFactory<BinderEntity>.CreateUpdateBinder(updateColumns, paramNames, _dialect);

        var parameters = new List<DbParameter>();
        var columnsAdded = binder(updated, original, parameters);

        Assert.Equal(2, columnsAdded);
        Assert.Equal(2, parameters.Count);
        Assert.Equal("New Name", parameters[0].Value);
        Assert.Equal("Inactive", parameters[1].Value!.ToString());
    }

    [Fact]
    public void CreateUpsertBinder_PopulatesAllParametersCorrectly()
    {
        var entity = new BinderEntity
        {
            Id = 42,
            Name = "Upsert",
            Age = 25,
            Data = new Dictionary<string, string>(),
            Status = Status.Active
        };

        // For upsert, we usually use ALL ordered columns
        var paramNames = _columns.Select((_, i) => $"u{i}").ToList();
        var binder = CompiledBinderFactory<BinderEntity>.CreateInsertBinder(_columns, paramNames, _dialect);

        var parameters = new List<DbParameter>();
        var count = binder(entity, parameters);

        Assert.Equal(_columns.Count, count);
        Assert.Equal(_columns.Count, parameters.Count);
        Assert.Equal(42, parameters[0].Value);
        Assert.Equal("Upsert", parameters[1].Value);
    }

    [Fact]
    public void CreateInsertBinder_EnumStoredAsNumeric_UsesUnderlyingValue()
    {
        var typeMap = new TypeMapRegistry();
        var tableInfo = typeMap.GetTableInfo<BinderEntityNumericEnum>();
        var columns = tableInfo.OrderedColumns;
        var paramNames = new[] { "p0", "p1" };

        var binder = CompiledBinderFactory<BinderEntityNumericEnum>.CreateInsertBinder(columns, paramNames, _dialect);
        var parameters = new List<DbParameter>();

        var count = binder(new BinderEntityNumericEnum { Id = 7, Status = Status.Inactive }, parameters);

        Assert.Equal(2, count);
        Assert.Equal(2, parameters.Count);
        Assert.Equal(7, parameters[0].Value);
        Assert.Equal((int)Status.Inactive, Convert.ToInt32(parameters[1].Value));
    }

    [Fact]
    public void CreateUpdateBinder_EnumStoredAsNumeric_UsesUnderlyingValue()
    {
        var typeMap = new TypeMapRegistry();
        var tableInfo = typeMap.GetTableInfo<BinderEntityNumericEnum>();
        var updateColumns = tableInfo.OrderedColumns.Where(c => !c.IsId).ToList();
        var paramNames = new[] { "p0" };

        var binder = CompiledBinderFactory<BinderEntityNumericEnum>.CreateUpdateBinder(updateColumns, paramNames,
            _dialect);
        var parameters = new List<DbParameter>();

        var updated = new BinderEntityNumericEnum { Id = 1, Status = Status.Inactive };
        var original = new BinderEntityNumericEnum { Id = 1, Status = Status.Active };
        var count = binder(updated, original, parameters);

        Assert.Equal(1, count);
        Assert.Single(parameters);
        Assert.Equal((int)Status.Inactive, Convert.ToInt32(parameters[0].Value));
    }
}
