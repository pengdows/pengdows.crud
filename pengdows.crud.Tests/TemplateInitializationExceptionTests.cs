#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for TemplateInitializationException and the RetrieveAsync fallback that catches it.
/// </summary>
public class TemplateInitializationExceptionTests
{
    [Table("TplEntity")]
    private class TplEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    // ── Exception type invariants ────────────────────────────────────────────

    [Fact]
    public void TemplateInitializationException_MessageConstructor_PreservesMessage()
    {
        var ex = new TemplateInitializationException("build failed");
        Assert.Equal("build failed", ex.Message);
    }

    [Fact]
    public void TemplateInitializationException_InnerExceptionConstructor_PreservesInner()
    {
        var inner = new InvalidOperationException("original");
        var ex = new TemplateInitializationException("wrap", inner);
        Assert.Equal("wrap", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void TemplateInitializationException_IsException()
    {
        var ex = new TemplateInitializationException("test");
        Assert.IsAssignableFrom<Exception>(ex);
    }

    // ── RetrieveAsync fallback covers list.Count > 2 ────────────────────────

    [Fact]
    public async Task RetrieveAsync_ListCountGreaterThanTwo_ReturnsAllRows()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<TplEntity>();

        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite });

        var execConn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite };
        execConn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { ["Id"] = 1, ["Name"] = "alpha" },
            new Dictionary<string, object?> { ["Id"] = 2, ["Name"] = "beta" },
            new Dictionary<string, object?> { ["Id"] = 3, ["Name"] = "gamma" },
        });
        factory.Connections.Add(execConn);

        var ctx = new DatabaseContext(
            "Data Source=sqlite;EmulatedProduct=Sqlite",
            factory,
            typeMap);

        var gateway = new TableGateway<TplEntity, int>(ctx);
        var results = await gateway.RetrieveAsync(new[] { 1, 2, 3 });

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task BuildRetrieve_ListCountGreaterThanTwo_ReturnsSqlContainer()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<TplEntity>();

        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite });
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite });

        var ctx = new DatabaseContext(
            "Data Source=:memory:;EmulatedProduct=Sqlite",
            factory,
            typeMap);

        var gateway = new TableGateway<TplEntity, int>(ctx);
        var sc = gateway.BuildRetrieve(new[] { 1, 2, 3 });

        Assert.NotNull(sc);
        var sql = sc.Query.ToString();
        Assert.Contains("IN", sql);
    }
}
