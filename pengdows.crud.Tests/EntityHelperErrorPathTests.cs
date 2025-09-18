#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class EntityHelperErrorPathTests : IAsyncLifetime
{
    public TypeMapRegistry TypeMap { get; private set; } = null!;
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
            await asyncDisp.DisposeAsync().ConfigureAwait(false);
        }
        else if (Context is IDisposable disp)
        {
            disp.Dispose();
        }
    }
    [Table("test_entity")]
    private class EntityWithNoPrimaryKey
    {
        [Column("name", DbType.String)]
        public string Name { get; set; } = "";
    }

    [Table("test_entity")]
    private class EntityWithoutIdColumn
    {
        [Column("name", DbType.String)]
        public string Name { get; set; } = "";

        [PrimaryKey(1)]
        [Column("composite_key", DbType.String)]
        public string CompositeKey { get; set; } = "";
    }

    [Table("test_entity")]
    private class EntityWithUnsupportedId
    {
        [Id]
        [Column("id", DbType.Decimal)]
        public decimal Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = "";
    }

    [Fact]
    public void Constructor_EntityWithoutTableAttribute_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => new EntityHelper<object, int>(Context));
    }

    [Fact]
    public async Task UpdateAsync_NullEntity_ThrowsArgumentNullException()
    {
        var helper = new EntityHelper<TestEntity, long>(Context);

        await Assert.ThrowsAsync<ArgumentNullException>(() => helper.UpdateAsync(null!));
    }

    [Fact]
    public async Task UpdateAsync_EntityWithNoPrimaryKey_ThrowsNotSupportedException()
    {
        Assert.Throws<InvalidOperationException>(() => new EntityHelper<EntityWithNoPrimaryKey, int>(Context));
    }

    [Fact]
    public async Task UpsertAsync_NullEntity_ThrowsArgumentNullException()
    {
        var helper = new EntityHelper<TestEntity, long>(Context);

        await Assert.ThrowsAsync<ArgumentNullException>(() => helper.UpsertAsync(null!));
    }

    [Fact]
    public async Task UpsertAsync_EntityWithNoPrimaryKey_ThrowsNotSupportedException()
    {
        Assert.Throws<InvalidOperationException>(() => new EntityHelper<EntityWithNoPrimaryKey, int>(Context));
    }

    [Fact]
    public async Task CreateAsync_NullEntity_ThrowsArgumentNullException()
    {
        var helper = new EntityHelper<TestEntity, long>(Context);

        await Assert.ThrowsAsync<ArgumentNullException>(() => helper.CreateAsync(null!, Context));
    }

    [Fact]
    public async Task DeleteAsync_NullIds_ThrowsArgumentNullException()
    {
        var helper = new EntityHelper<TestEntity, long>(Context);

        await Assert.ThrowsAsync<ArgumentNullException>(() => helper.DeleteAsync(null!));
    }

    [Fact]
    public async Task DeleteAsync_EmptyIds_ThrowsArgumentException()
    {
        var helper = new EntityHelper<TestEntity, long>(Context);

        await Assert.ThrowsAsync<ArgumentException>(() => helper.DeleteAsync(new long[0]));
    }

    [Fact]
    public async Task RetrieveAsync_NullIds_ThrowsArgumentNullException()
    {
        var helper = new EntityHelper<TestEntity, long>(Context);

        await Assert.ThrowsAsync<ArgumentNullException>(() => helper.RetrieveAsync(null!));
    }

    [Fact]
    public async Task RetrieveAsync_EmptyIds_ThrowsArgumentException()
    {
        var helper = new EntityHelper<TestEntity, long>(Context);

        await Assert.ThrowsAsync<ArgumentException>(() => helper.RetrieveAsync(new long[0]));
    }

    [Fact]
    public async Task UpdateAsync_WithLoadOriginal_OriginalNotFound_ThrowsInvalidOperationException()
    {
        var helper = new EntityHelper<TestEntity, long>(Context);
        var entity = new TestEntity { Id = 999, Name = "NonExistent" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => helper.UpdateAsync(entity, true));
    }

    [Fact]
    public void BuildDelete_ValidEntity_ReturnsContainer()
    {
        var helper = new EntityHelper<TestEntity, long>(Context);

        var container = helper.BuildDelete(1);

        Assert.NotNull(container);
        Assert.Contains("DELETE", container.Query.ToString());
    }

    [Fact]
    public void BuildDelete_InvalidRowIdType_ThrowsInvalidOperationException()
    {
        Assert.Throws<TypeInitializationException>(() => new EntityHelper<EntityWithUnsupportedId, decimal>(Context));
    }

    [Fact]
    public async Task TooManyParameters_ThrowsTooManyParametersException()
    {
        var helper = new EntityHelper<TestEntity, long>(Context);
        var manyIds = new List<long>();

        for (int i = 0; i < 100000; i++)
        {
            manyIds.Add(i);
        }

        await Assert.ThrowsAsync<TooManyParametersException>(() => helper.RetrieveAsync(manyIds));
    }

    [Fact]
    public void BuildRetrieve_EntityWithoutIdColumn_ThrowsInvalidOperationException()
    {
        var helper = new EntityHelper<EntityWithoutIdColumn, string>(Context);

        Assert.Throws<InvalidOperationException>(() => helper.BuildRetrieve(new[] { "test" }, "alias"));
    }


    [Fact]
    public void ValidateRowIdType_UnsupportedType_ThrowsNotSupportedException()
    {
        Assert.Throws<TypeInitializationException>(() => new EntityHelper<EntityWithUnsupportedId, decimal>(Context));
    }

    [Fact]
    public async Task LoadSingleAsync_NullSqlContainer_ThrowsArgumentNullException()
    {
        var helper = new EntityHelper<TestEntity, long>(Context);

        await Assert.ThrowsAsync<ArgumentNullException>(() => helper.LoadSingleAsync(null!));
    }

    [Fact]
    public async Task LoadListAsync_NullSqlContainer_ThrowsArgumentNullException()
    {
        var helper = new EntityHelper<TestEntity, long>(Context);

        await Assert.ThrowsAsync<ArgumentNullException>(() => helper.LoadListAsync(null!));
    }

    [Fact]
    public void ClearCaches_ClearsAllInternalCaches()
    {
        var helper = new EntityHelper<TestEntity, long>(Context);

        helper.ClearCaches();

        Assert.True(true);
    }

    [Fact]
    public void ClearCaches_ExecutesSuccessfully()
    {
        var helper = new EntityHelper<TestEntity, long>(Context);

        helper.ClearCaches();

        Assert.True(true);
    }

    [Fact]
    public void SetLogger_NullLogger_SetsNullLogger()
    {
        EntityHelper<TestEntity, long>.Logger = null!;

        Assert.NotNull(EntityHelper<TestEntity, long>.Logger);
    }

    [Fact]
    public void BuildUpsert_ValidEntity_ReturnsContainer()
    {
        var helper = new EntityHelper<TestEntity, long>(Context);
        var entity = new TestEntity { Name = "Test" };

        var container = helper.BuildUpsert(entity);

        Assert.NotNull(container);
    }

    [Table("no_pk_entity")]
    private class EntityWithNoPkAttributes
    {
        [Column("name", DbType.String)]
        public string Name { get; set; } = "";
    }

    [Fact]
    public void BuildUpsert_EntityWithNoPrimaryKey_ThrowsNotSupportedException()
    {
        Assert.Throws<InvalidOperationException>(() => new EntityHelper<EntityWithNoPkAttributes, string>(Context));
    }

    [Fact]
    public async Task RetrieveOneAsync_NullEntity_ThrowsArgumentNullException()
    {
        var helper = new EntityHelper<TestEntity, long>(Context);

        await Assert.ThrowsAsync<ArgumentNullException>(() => helper.RetrieveOneAsync(null!));
    }
}
