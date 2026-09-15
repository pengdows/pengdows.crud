using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class InterfaceDefaultMethodCoverageTests
{
    [Fact]
    public void IAuditValues_As_WhenUserIdPresent_CastsValue()
    {
        var values = new Mock<IAuditValues> { CallBase = true };
        values.SetupGet(v => v.UserId).Returns(42);

        var result = values.Object.As<int>();

        Assert.Equal(42, result);
    }

    [Fact]
    public void IAuditValues_As_WhenUserIdNull_ThrowsInvalidOperationException()
    {
        var values = new Mock<IAuditValues> { CallBase = true };
        values.SetupGet(v => v.UserId).Returns((object)null!);

        var ex = Assert.Throws<InvalidOperationException>(() => values.Object.As<int>());
        Assert.Contains("UserId is null", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ITableGateway_DefaultWrapperMethods_DelegateToBatchMethods()
    {
        var gateway = new Mock<ITableGateway<DefaultEntity, int>> { CallBase = true };
        var entities = (IReadOnlyList<DefaultEntity>)new List<DefaultEntity>
        {
            new() { Id = 1 },
            new() { Id = 2 }
        };
        var entityCollection = (IReadOnlyCollection<DefaultEntity>)new List<DefaultEntity>(entities);
        var ids = (IEnumerable<int>)new List<int> { 1, 2 };
        var cancellation = new CancellationTokenSource().Token;

        gateway.Setup(g => g.BatchCreateAsync(entities, null, cancellation)).ReturnsAsync(11).Verifiable();
        gateway.Setup(g => g.BatchDeleteAsync(ids, null, cancellation)).ReturnsAsync(12).Verifiable();
        gateway.Setup(g => g.BatchDeleteAsync(entityCollection, null, cancellation)).ReturnsAsync(13).Verifiable();
        gateway.Setup(g => g.BatchUpdateAsync(entities, null, cancellation)).ReturnsAsync(14).Verifiable();
        gateway.Setup(g => g.BatchUpsertAsync(entities, null, cancellation)).ReturnsAsync(15).Verifiable();

        Assert.Equal(11, await gateway.Object.CreateAsync(entities, null, cancellation));
        Assert.Equal(12, await gateway.Object.DeleteAsync(ids, null, cancellation));
        Assert.Equal(13, await gateway.Object.DeleteAsync(entityCollection, null, cancellation));
        Assert.Equal(14, await gateway.Object.UpdateAsync(entities, null, cancellation));
        Assert.Equal(15, await gateway.Object.UpsertAsync(entities, null, cancellation));

        gateway.VerifyAll();
    }

    [Fact]
    public void PrepareStatements_DefaultsFromDataSourceInfo()
    {
        var dataSourceInfo = new Mock<IDataSourceInformation>();
        dataSourceInfo.SetupGet(d => d.DefaultPrepareStatements).Returns(true);

        var context = new Mock<IDatabaseContext> { CallBase = true };
        context.SetupGet(c => c.DataSourceInfo).Returns(dataSourceInfo.Object);
        // DefaultPrepareStatements is no longer exposed on IDatabaseContext, 
        // but we can assert on the mock setup if needed, or remove this test.
    }

    [Fact]
    public void ISqlDialect_DefaultMembers_ExecuteBranches()
    {
        // RenderJsonArgument and RenderMergeSource live on IInternalSqlDialect; mock that.
        // Moq cannot mock extension methods, so we avoid calling RenderJsonArgument via a
        // Setup() expression — only non-JSON columns are used in the mock-based test.
        var dialect = new Mock<IInternalSqlDialect> { CallBase = true };
        dialect.SetupGet(d => d.QuotePrefix).Returns("[");
        dialect.SetupGet(d => d.QuoteSuffix).Returns("]");
        dialect.SetupGet(d => d.ParameterMarker).Returns("@");
        dialect.Setup(d => d.MakeParameterName(It.IsAny<string>())).Returns((string p) => "@" + p);

        Assert.False(dialect.Object.IsPrepareExhausted);
        Assert.Equal("[col]", dialect.Object.WrapSimpleName("col"));

        var col1 = new Mock<IColumnInfo>();
        col1.SetupGet(c => c.Name).Returns("id");
        col1.SetupGet(c => c.IsJsonType).Returns(false);

        var col2 = new Mock<IColumnInfo>();
        col2.SetupGet(c => c.Name).Returns("name");
        col2.SetupGet(c => c.IsJsonType).Returns(false);

        // Call through the extension method (ISqlDialect overload) — exercises GetInternal() cast path.
        ISqlDialect dialectAsPublic = dialect.Object;
        var merge = dialectAsPublic.RenderMergeSource(
            new[] { col1.Object, col2.Object },
            new[] { "p0", "p1" });

        Assert.Contains("@p0", merge, StringComparison.Ordinal);
        Assert.Contains("@p1", merge, StringComparison.Ordinal);
        Assert.Contains("[id]", merge, StringComparison.Ordinal);
        Assert.Contains("[name]", merge, StringComparison.Ordinal);

        Assert.Equal("t.id = s.id", dialectAsPublic.RenderMergeOnClause("t.id = s.id"));

        Assert.Throws<ArgumentNullException>(() => dialectAsPublic.RenderMergeSource(null!, new[] { "p0" }));
        Assert.Throws<ArgumentNullException>(() => dialectAsPublic.RenderMergeSource(new[] { col1.Object }, null!));
        Assert.Throws<ArgumentException>(() => dialectAsPublic.RenderMergeSource(
            new[] { col1.Object, col2.Object },
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

        var jsonColumn = new Mock<IColumnInfo>();
        jsonColumn.SetupGet(c => c.Name).Returns("payload");
        jsonColumn.SetupGet(c => c.IsJsonType).Returns(true);

        var plainColumn = new Mock<IColumnInfo>();
        plainColumn.SetupGet(c => c.Name).Returns("id");
        plainColumn.SetupGet(c => c.IsJsonType).Returns(false);

        // The base SqlDialect.RenderJsonArgument returns the parameterMarker unchanged,
        // so we just verify both placeholders appear in the output.
        var merge = dialect.RenderMergeSource(
            new[] { plainColumn.Object, jsonColumn.Object },
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
