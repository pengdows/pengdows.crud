using System.Data;
using System.Reflection;
using pengdows.crud.attributes;
using Xunit;

namespace pengdows.crud.Tests;

public class TestTableTests
{
    [Fact]
    public void Class_ShouldHave_TableAttribute()
    {
        var attr = typeof(TestTable).GetCustomAttribute<TableAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("test_table", attr.Name);
    }
    
    [Fact]
    public void IdProperty_ShouldHave_IdAndColumnAttributes()
    {
        var prop = typeof(TestTable).GetProperty("Id");
        Assert.NotNull(prop);

        Assert.NotNull(prop.GetCustomAttribute<IdAttribute>());
    
        var columnAttr = prop.GetCustomAttribute<ColumnAttribute>();
        Assert.NotNull(columnAttr);
        Assert.Equal("id", columnAttr.Name);
        Assert.Equal(DbType.Int64, columnAttr.Type);
    }

    [Fact]
    public void NameProperty_ShouldHave_EnumColumnAttribute()
    {
        var prop = typeof(TestTable).GetProperty("Name");
        Assert.NotNull(prop);

        var enumAttr = prop.GetCustomAttribute<EnumColumnAttribute>();
        Assert.NotNull(enumAttr);
        Assert.Equal(typeof(NameEnum), enumAttr.EnumType);
    }

    [Fact]
    public void CreatedAt_ShouldHave_CreatedOnAttribute()
    {
        var prop = typeof(TestTable).GetProperty("CreatedAt");
        Assert.NotNull(prop);
        Assert.NotNull(prop.GetCustomAttribute<CreatedOnAttribute>());
    }

    [Fact]
    public void JsonProperty_ShouldHave_JsonAttribute()
    {
        var prop = typeof(TestTable).GetProperty("JsonProperty");
        Assert.NotNull(prop);
        Assert.NotNull(prop.GetCustomAttribute<JsonAttribute>());
    }

    [Fact]
    public void NonUpdateableColumn_ShouldHave_NonUpdateableAttribute()
    {
        var prop = typeof(TestTable).GetProperty("NonUpdateableColumn");
        Assert.NotNull(prop);
        Assert.NotNull(prop.GetCustomAttribute<NonUpdateableAttribute>());
    }


}
