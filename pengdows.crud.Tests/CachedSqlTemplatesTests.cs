using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public class CachedSqlTemplatesTests : IAsyncLifetime
{
    internal ITypeMapRegistry TypeMap { get; private set; } = null!;
    public IDatabaseContext Context { get; private set; } = null!;
    public IAuditValueResolver AuditValueResolver { get; private set; } = null!;

    public Task InitializeAsync()
    {
        TypeMap = new TypeMapRegistry();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        Context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory, TypeMap);
        AuditValueResolver = new StubAuditValueResolver("test-user");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (Context is IAsyncDisposable asyncDisp)
        {
            await asyncDisp.DisposeAsync();
        }
        else if (Context is IDisposable disp)
        {
            disp.Dispose();
        }
    }

    [Fact]
    public void BuildCreate_ReusesCachedTemplates()
    {
        TypeMap.Register<TestEntity>();
        var helper1 = new TableGateway<TestEntity, int>(Context, AuditValueResolver);
        var entity1 = new TestEntity { Name = "one" };
        var entity2 = new TestEntity { Name = "two" };

        // Build create twice with same helper to verify template reuse within instance
        var sc1 = helper1.BuildCreate(entity1);
        var field = typeof(TableGateway<TestEntity, int>).GetField("_templatesByDialect",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var initialCacheCount = CountConditionalWeakTableEntries(field.GetValue(helper1)!);

        var sc2 = helper1.BuildCreate(entity2);
        var finalCacheCount = CountConditionalWeakTableEntries(field.GetValue(helper1)!);

        // Verify cache was reused (same count means no new templates were created)
        Assert.Equal(initialCacheCount, finalCacheCount);
        Assert.True(finalCacheCount > 0, "Templates should be cached");
    }

    [Fact]
    public void BuildCreate_UsesPredictableParameterNames()
    {
        TypeMap.Register<TestEntity>();
        var helper = new TableGateway<TestEntity, int>(Context, AuditValueResolver);
        var entity = new TestEntity { Name = "foo" };

        var sc = helper.BuildCreate(entity);

        var sql = sc.Query.ToString();
        Assert.Contains("@i0", sql);
        Assert.Contains("@i1", sql);

        var field = typeof(SqlContainer).GetField("_parameters", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var parameters = (IDictionary<string, DbParameter>)field.GetValue(sc)!;
        Assert.Contains("i0", parameters.Keys);
        Assert.Contains("i1", parameters.Keys);
    }

    [Fact]
    public async Task BuildUpdateAsync_ReusesCachedTemplates()
    {
        TypeMap.Register<TestEntity>();
        var helper1 = new TableGateway<TestEntity, int>(Context, AuditValueResolver);

        var entity1 = new TestEntity { Id = 1, Name = "one" };
        var entity2 = new TestEntity { Id = 1, Name = "two" };

        // Build update twice with same helper to verify template reuse within instance
        await helper1.BuildUpdateAsync(entity1, false);

        var field = typeof(TableGateway<TestEntity, int>).GetField("_templatesByDialect",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var initialCacheCount = CountConditionalWeakTableEntries(field.GetValue(helper1)!);

        await helper1.BuildUpdateAsync(entity2, false);
        var finalCacheCount = CountConditionalWeakTableEntries(field.GetValue(helper1)!);

        // Verify cache was reused (same count means no new templates were created)
        Assert.Equal(initialCacheCount, finalCacheCount);
        Assert.True(finalCacheCount > 0, "Templates should be cached");
    }


    /// <summary>
    /// _templatesByDialect is a ConditionalWeakTable (keyed by dialect instance, not
    /// SupportedDatabase enum — see the field's own comment), which has no .Count. It does
    /// implement IEnumerable, so counting entries via enumeration is the only reflection-free
    /// option available without referencing the internal CachedSqlTemplates type argument.
    /// </summary>
    private static int CountConditionalWeakTableEntries(object conditionalWeakTable)
    {
        return ((IEnumerable)conditionalWeakTable).Cast<object>().Count();
    }

    [Fact]
    public async Task BuildUpdateAsync_WhenLoadOriginalTrue_ThrowsIfTableMissing()
    {
        // TestEntity has a [Version] column, so "original not found" (table missing resolves the
        // same way as row missing here) is a concurrency conflict per the documented contract,
        // not a generic InvalidOperationException.
        TypeMap.Register<TestEntity>();
        var helper = new TableGateway<TestEntity, int>(Context, AuditValueResolver);
        var entity = new TestEntity { Id = 1, Name = "one" };

        await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
            await helper.BuildUpdateAsync(entity, true));
    }
}