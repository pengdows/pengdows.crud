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
    public void IDatabaseContext_PrepareStatements_DefaultsFromDataSourceInfo()
    {
        var dataSourceInfo = new Mock<IDataSourceInformation>();
        dataSourceInfo.SetupGet(d => d.PrepareStatements).Returns(true);

        var context = new Mock<IDatabaseContext> { CallBase = true };
        context.SetupGet(c => c.DataSourceInfo).Returns(dataSourceInfo.Object);

        Assert.True(context.Object.PrepareStatements);
    }

    [Fact]
    public void ISqlDialect_DefaultMembers_ExecuteBranches()
    {
        var dialect = new Mock<ISqlDialect> { CallBase = true };
        dialect.SetupGet(d => d.QuotePrefix).Returns("[");
        dialect.SetupGet(d => d.QuoteSuffix).Returns("]");
        dialect.SetupGet(d => d.ParameterMarker).Returns("@");
        dialect.Setup(d => d.MakeParameterName(It.IsAny<string>())).Returns((string p) => "@" + p);
        dialect.Setup(d => d.RenderJsonArgument(It.IsAny<string>(), It.IsAny<IColumnInfo>()))
            .Returns((string p, IColumnInfo _) => "JSON(" + p + ")");

        Assert.False(dialect.Object.IsPrepareExhausted);
        Assert.Equal("[col]", dialect.Object.WrapSimpleName("col"));

        var plainColumn = new Mock<IColumnInfo>();
        plainColumn.SetupGet(c => c.Name).Returns("id");
        plainColumn.SetupGet(c => c.IsJsonType).Returns(false);

        var jsonColumn = new Mock<IColumnInfo>();
        jsonColumn.SetupGet(c => c.Name).Returns("payload");
        jsonColumn.SetupGet(c => c.IsJsonType).Returns(true);

        var merge = dialect.Object.RenderMergeSource(
            new[] { plainColumn.Object, jsonColumn.Object },
            new[] { "p0", "p1" });

        Assert.Contains("@p0", merge, StringComparison.Ordinal);
        Assert.Contains("JSON(@p1)", merge, StringComparison.Ordinal);
        Assert.Contains("[id]", merge, StringComparison.Ordinal);
        Assert.Contains("[payload]", merge, StringComparison.Ordinal);

        Assert.Equal("t.id = s.id", dialect.Object.RenderMergeOnClause("t.id = s.id"));

        Assert.Throws<ArgumentNullException>(() => dialect.Object.RenderMergeSource(null!, new[] { "p0" }));
        Assert.Throws<ArgumentNullException>(() => dialect.Object.RenderMergeSource(new[] { plainColumn.Object }, null!));
        Assert.Throws<ArgumentException>(() => dialect.Object.RenderMergeSource(
            new[] { plainColumn.Object, jsonColumn.Object },
            new[] { "p0" }));
        Assert.Throws<ArgumentNullException>(() => dialect.Object.RenderMergeOnClause(null!));
    }

    public sealed class DefaultEntity
    {
        public int Id { get; set; }
    }
}
