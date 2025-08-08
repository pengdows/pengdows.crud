#region

using System.Data;
using System.Reflection;
using pengdows.crud.attributes;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class AttributeTests
{
    [Fact]
    public void ColumnAttribute_SetsName()
    {
        var attr = new ColumnAttribute("Name", DbType.Int32);
        Assert.Equal("Name", attr.Name);
    }

    [Fact]
    public void TableAttribute_SetsName()
    {
        var attr = new TableAttribute("TableName");
        Assert.Equal("TableName", attr.Name);
    }

    [Fact]
    public void IdAttribute_CanBeApplied()
    {
        var prop = typeof(Dummy).GetProperty(nameof(Dummy.Id));
        var attr = prop.GetCustomAttribute<IdAttribute>();
        Assert.NotNull(attr);
    }

    private class Dummy
    {
        [Id] public int Id { get; set; }
    }
}