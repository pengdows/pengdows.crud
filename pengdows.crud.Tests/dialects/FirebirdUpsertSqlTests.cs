#region

using System;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests.dialects;

public class FirebirdUpsertSqlTests
{
    [Fact]
    public void BuildUpsert_UsesFirebirdMergeSql()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        var typeMap = new TypeMapRegistry();
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Firebird", factory, typeMap);
        context.TypeMapRegistry.Register<FirebirdMergeEntity>();

        var helper = new TableGateway<FirebirdMergeEntity, int>(context);
        var entity = new FirebirdMergeEntity { Id = 1, Name = "sa", Counter = 5 };

        using var container = helper.BuildUpsert(entity);
        var sql = container.Query.ToString();

        Assert.Contains("UPDATE OR INSERT INTO", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERGE INTO", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MATCHING", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VALUES", sql, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain("USING (SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ON CONFLICT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DO UPDATE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Table("firebird_merge")]
    private class FirebirdMergeEntity
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;

        [Column("counter", DbType.Int32)] public int Counter { get; set; }
    }
}