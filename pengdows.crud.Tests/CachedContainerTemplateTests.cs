#region

using System;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class CachedContainerTemplateTests : SqlLiteContextTestBase
{
    [Fact]
    public void BuildCreate_ProducesSameSql_AsDirectBuild()
    {
        TypeMap.Register<TemplateEntity>();
        var helper = new TableGateway<TemplateEntity, int>(Context);

        var entity = new TemplateEntity { Name = "Test1", Value = 42 };

        // First call triggers template init; second call uses clone path
        var sc1 = helper.BuildCreate(entity);
        var sql1 = sc1.Query.ToString();

        var sc2 = helper.BuildCreate(entity);
        var sql2 = sc2.Query.ToString();

        Assert.Equal(sql1, sql2);
        Assert.Contains("INSERT INTO", sql1);
        Assert.Contains("\"Name\"", sql1);
        Assert.Contains("\"Value\"", sql1);
    }

    [Fact]
    public void BuildCreate_SetsCorrectParameterValues()
    {
        TypeMap.Register<TemplateEntity>();
        var helper = new TableGateway<TemplateEntity, int>(Context);

        var entity = new TemplateEntity { Name = "Hello", Value = 99 };
        var sc = helper.BuildCreate(entity);

        // Id is writable, so i0=Id, i1=Name, i2=Value
        var nameValue = sc.GetParameterValue("i1");
        var valueValue = sc.GetParameterValue("i2");

        Assert.Equal("Hello", nameValue);
        Assert.Equal(99, valueValue);
    }

    [Fact]
    public void BuildCreate_ClonesAreIndependent()
    {
        TypeMap.Register<TemplateEntity>();
        var helper = new TableGateway<TemplateEntity, int>(Context);

        var entity1 = new TemplateEntity { Name = "First", Value = 1 };
        var entity2 = new TemplateEntity { Name = "Second", Value = 2 };

        var sc1 = helper.BuildCreate(entity1);
        var sc2 = helper.BuildCreate(entity2);

        // Each clone should have its own parameter values (i1=Name, i2=Value)
        Assert.Equal("First", sc1.GetParameterValue("i1"));
        Assert.Equal("Second", sc2.GetParameterValue("i1"));
        Assert.Equal(1, sc1.GetParameterValue("i2"));
        Assert.Equal(2, sc2.GetParameterValue("i2"));
    }

    [Fact]
    public void BuildDelete_ProducesSameSql_AsDirectBuild()
    {
        TypeMap.Register<TemplateEntity>();
        var helper = new TableGateway<TemplateEntity, int>(Context);

        var sc1 = helper.BuildDelete(1);
        var sql1 = sc1.Query.ToString();

        var sc2 = helper.BuildDelete(2);
        var sql2 = sc2.Query.ToString();

        // SQL should be identical (only parameter value differs)
        Assert.Equal(sql1, sql2);
        Assert.Contains("DELETE FROM", sql1);
    }

    [Fact]
    public void BuildDelete_SetsCorrectId()
    {
        TypeMap.Register<TemplateEntity>();
        var helper = new TableGateway<TemplateEntity, int>(Context);

        var sc = helper.BuildDelete(42);
        var idValue = sc.GetParameterValue("k0");

        Assert.Equal(42, idValue);
    }

    [Fact]
    public void BuildBaseRetrieve_ProducesSameSql_AsDirectBuild()
    {
        TypeMap.Register<TemplateEntity>();
        var helper = new TableGateway<TemplateEntity, int>(Context);

        // Alias "a" uses the clone path
        var sc1 = helper.BuildBaseRetrieve("a");
        var sql1 = sc1.Query.ToString();

        var sc2 = helper.BuildBaseRetrieve("a");
        var sql2 = sc2.Query.ToString();

        Assert.Equal(sql1, sql2);
        Assert.Contains("SELECT", sql1);
        Assert.Contains("FROM", sql1);

        // Non-"a" alias uses the string-cached path — should produce equivalent SQL
        var sc3 = helper.BuildBaseRetrieve("b");
        var sql3 = sc3.Query.ToString();
        Assert.Contains("SELECT", sql3);
        Assert.Contains("FROM", sql3);
    }

    [Fact]
    public void BuildCreate_WithAuditFields_SetsCorrectValues()
    {
        TypeMap.Register<AuditTemplateEntity>();
        var helper = new TableGateway<AuditTemplateEntity, int>(Context, AuditValueResolver);

        var entity = new AuditTemplateEntity { Name = "Audited" };
        var sc = helper.BuildCreate(entity);

        // Audit fields should have been set by MutateEntityForInsert
        Assert.NotEqual(default, entity.CreatedOn);
        Assert.NotEmpty(entity.CreatedBy);

        // The SQL should include audit columns
        var sql = sc.Query.ToString();
        Assert.Contains("\"CreatedOn\"", sql);
        Assert.Contains("\"CreatedBy\"", sql);
    }

    [Fact]
    public void BuildCreate_WithVersionColumn_InitializesVersion()
    {
        TypeMap.Register<VersionTemplateEntity>();
        var helper = new TableGateway<VersionTemplateEntity, int>(Context);

        var entity = new VersionTemplateEntity { Name = "Versioned" };
        Assert.Equal(0, entity.Version);

        helper.BuildCreate(entity);

        // Version should be initialized to 1
        Assert.Equal(1, entity.Version);
    }

    [Table("Template")]
    private class TemplateEntity
    {
        [Id] [Column("Id", DbType.Int32)] public int Id { get; set; }

        [Column("Name", DbType.String)] public string Name { get; set; } = string.Empty;

        [Column("Value", DbType.Int32)] public int Value { get; set; }
    }

    [Table("AuditTemplate")]
    private class AuditTemplateEntity
    {
        [Id] [Column("Id", DbType.Int32)] public int Id { get; set; }

        [Column("Name", DbType.String)] public string Name { get; set; } = string.Empty;

        [CreatedOn]
        [Column("CreatedOn", DbType.DateTime)]
        public DateTime CreatedOn { get; set; }

        [CreatedBy]
        [Column("CreatedBy", DbType.String)]
        public string CreatedBy { get; set; } = string.Empty;

        [LastUpdatedOn]
        [Column("LastUpdatedOn", DbType.DateTime)]
        public DateTime LastUpdatedOn { get; set; }

        [LastUpdatedBy]
        [Column("LastUpdatedBy", DbType.String)]
        public string LastUpdatedBy { get; set; } = string.Empty;
    }

    [Table("VersionTemplate")]
    private class VersionTemplateEntity
    {
        [Id] [Column("Id", DbType.Int32)] public int Id { get; set; }

        [Column("Name", DbType.String)] public string Name { get; set; } = string.Empty;

        [Version]
        [Column("Version", DbType.Int32)]
        public int Version { get; set; }
    }
}
