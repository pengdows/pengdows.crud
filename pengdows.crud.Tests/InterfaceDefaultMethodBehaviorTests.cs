using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class InterfaceDefaultMethodBehaviorTests
{
    [Fact]
    public void IAuditValues_As_WhenUserIdPresent_CastsValue()
    {
        IAuditValues values = new ScriptedAuditValues { UserId = 42 };

        var result = values.As<int>();

        Assert.Equal(42, result);
    }

    [Fact]
    public void IAuditValues_As_WhenUserIdNull_ThrowsInvalidOperationException()
    {
        IAuditValues values = new ScriptedAuditValues { UserId = null! };

        var ex = Assert.Throws<InvalidOperationException>(() => values.As<int>());
        Assert.Contains("UserId is null", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ITableGateway_DefaultWrapperMethods_DelegateToBatchMethods()
    {
        var scripted = new ScriptedTableGateway<DefaultEntity, int>();
        ITableGateway<DefaultEntity, int> gateway = scripted;
        var entities = (IReadOnlyList<DefaultEntity>)new List<DefaultEntity>
        {
            new() { Id = 1 },
            new() { Id = 2 }
        };
        var entityCollection = (IReadOnlyCollection<DefaultEntity>)new List<DefaultEntity>(entities);
        var ids = (IEnumerable<int>)new List<int> { 1, 2 };
        var cancellation = new CancellationTokenSource().Token;

        scripted.Results["BatchCreate"] = 11;
        scripted.Results["BatchDeleteIds"] = 12;
        scripted.Results["BatchDeleteEntities"] = 13;
        scripted.Results["BatchUpdate"] = 14;
        scripted.Results["BatchUpsert"] = 15;

        Assert.Equal(11, await gateway.CreateAsync(entities, null, cancellation));
        Assert.Equal(12, await gateway.DeleteAsync(ids, null, cancellation));
        Assert.Equal(13, await gateway.DeleteAsync(entityCollection, null, cancellation));
        Assert.Equal(14, await gateway.UpdateAsync(entities, null, cancellation));
        Assert.Equal(15, await gateway.UpsertAsync(entities, null, cancellation));

        // Each default wrapper delegated to its batch member, passing the same collection, context and token through.
        Assert.Equal(new[] { "BatchCreate", "BatchDeleteIds", "BatchDeleteEntities", "BatchUpdate", "BatchUpsert" },
            scripted.Calls.ConvertAll(c => c.Member));
        Assert.Same(entities, scripted.Calls[0].Entities);
        Assert.Same(ids, scripted.Calls[1].Entities);
        Assert.Same(entityCollection, scripted.Calls[2].Entities);
        Assert.Same(entities, scripted.Calls[3].Entities);
        Assert.Same(entities, scripted.Calls[4].Entities);
        Assert.All(scripted.Calls, c =>
        {
            Assert.Null(c.Context);
            Assert.Equal(cancellation, c.Token);
        });
    }

    [Fact]
    public void ISqlDialect_DefaultMembers_ExecuteBranches()
    {
        // RenderJsonArgument and RenderMergeSource live on IInternalSqlDialect; script only the members they read.
        IInternalSqlDialect dialect = new ScriptedInternalSqlDialect
        {
            QuotePrefix = "[",
            QuoteSuffix = "]",
            ParameterMarker = "@",
            MakeParameterNameFunc = p => "@" + p
        };

        Assert.False(dialect.IsPrepareExhausted);
        Assert.Equal("[col]", dialect.WrapSimpleName("col"));

        var col1 = new ScriptedColumnInfo { Name = "id", IsJsonType = false };
        var col2 = new ScriptedColumnInfo { Name = "name", IsJsonType = false };

        // Call through the extension method (ISqlDialect overload) — exercises GetInternal() cast path.
        ISqlDialect dialectAsPublic = dialect;
        var merge = dialectAsPublic.RenderMergeSource(
            new IColumnInfo[] { col1, col2 },
            new[] { "p0", "p1" });

        Assert.Contains("@p0", merge, StringComparison.Ordinal);
        Assert.Contains("@p1", merge, StringComparison.Ordinal);
        Assert.Contains("[id]", merge, StringComparison.Ordinal);
        Assert.Contains("[name]", merge, StringComparison.Ordinal);

        Assert.Equal("t.id = s.id", dialectAsPublic.RenderMergeOnClause("t.id = s.id"));

        Assert.Throws<ArgumentNullException>(() => dialectAsPublic.RenderMergeSource(null!, new[] { "p0" }));
        Assert.Throws<ArgumentNullException>(() => dialectAsPublic.RenderMergeSource(new IColumnInfo[] { col1 }, null!));
        Assert.Throws<ArgumentException>(() => dialectAsPublic.RenderMergeSource(
            new IColumnInfo[] { col1, col2 },
            new[] { "p0" }));
        Assert.Throws<ArgumentNullException>(() => dialectAsPublic.RenderMergeOnClause(null!));
    }

    [Fact]
    public void IInternalSqlDialect_RenderMergeSource_JsonColumn_CallsRenderJsonArgument()
    {
        // Verify the JSON-column branch of the RenderMergeSource default implementation.
        // Uses a fakeDb context to get a real dialect instance (avoids Moq extension-method limitation).
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory);
        ISqlDialect dialect = context.GetDialect();

        var jsonColumn = new ScriptedColumnInfo { Name = "payload", IsJsonType = true };
        var plainColumn = new ScriptedColumnInfo { Name = "id", IsJsonType = false };

        // The base SqlDialect.RenderJsonArgument returns the parameterMarker unchanged,
        // so we just verify both placeholders appear in the output.
        var merge = dialect.RenderMergeSource(
            new IColumnInfo[] { plainColumn, jsonColumn },
            new[] { "p0", "p1" });

        Assert.Contains("p0", merge, StringComparison.Ordinal);
        Assert.Contains("p1", merge, StringComparison.Ordinal);
        Assert.Contains("id", merge, StringComparison.Ordinal);
        Assert.Contains("payload", merge, StringComparison.Ordinal);
    }

    public sealed class DefaultEntity
    {
        public int Id { get; set; }
    }
}
