#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

/// <summary>
/// Exhaustive proof, per database write path, that a context configured
/// <see cref="ReadWriteMode.ReadOnly"/> rejects the write before any command reaches the
/// provider — the P0 acceptance criterion from
/// docs/planning/3.0-architectural-review-backlog.md ("Exhaustively prove ReadWriteMode.ReadOnly
/// really means incapable of writing"). CMS forcibly applies ReadWriteMode.ReadOnly to every
/// configured tenant as part of its security boundary; every one of these paths must reject via
/// one of the three documented <see cref="IReadOnlyViolation"/>-marked exception types (or the
/// pool-layer <see cref="PoolForbiddenException"/> backstop), never silently succeed.
/// </summary>
#pragma warning disable CS0618 // TableGateway is obsolete
public class ReadOnlyModeExhaustiveWritePathTests
{
    [Table("ro_entity")]
    private class RoEntity
    {
        // Writable [Id] (not [Id(false)]) so UpsertAsync/BatchUpsertAsync have a valid conflict
        // key — an [Id(false)]-only entity throws NotSupportedException("Upsert requires
        // client-assigned Id or [PrimaryKey] attributes.") before ReadWriteMode is ever
        // consulted, which would test entity-metadata validation, not read-only enforcement.
        [Id(true)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
    }

    [Table("ro_pk_entity")]
    private class RoPrimaryKeyEntity
    {
        [PrimaryKey(1)]
        [Column("code", DbType.String)]
        public string Code { get; set; } = string.Empty;

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
    }

    private static (IDatabaseContext context, ITypeMapRegistry typeMap) CreateReadOnlyContext()
    {
        var typeMap = new TypeMapRegistry();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadOnly
        };
        var context = new DatabaseContext(config, factory, null, typeMap);
        return (context, typeMap);
    }

    private static TableGateway<RoEntity, int> CreateGateway(IDatabaseContext context, ITypeMapRegistry typeMap)
    {
        typeMap.Register<RoEntity>();
        return new TableGateway<RoEntity, int>(context);
    }

    private static PrimaryKeyTableGateway<RoPrimaryKeyEntity> CreatePrimaryKeyGateway(
        IDatabaseContext context, ITypeMapRegistry typeMap)
    {
        typeMap.Register<RoPrimaryKeyEntity>();
        return new PrimaryKeyTableGateway<RoPrimaryKeyEntity>(context);
    }

    [Fact]
    public async Task CreateAsync_RejectsBeforeReachingProvider()
    {
        var (context, typeMap) = CreateReadOnlyContext();
        await using var ctx = (DatabaseContext)context;
        var gateway = CreateGateway(context, typeMap);

        await Assert.ThrowsAsync<ReadOnlyContextException>(async () =>
            await gateway.CreateAsync(new RoEntity { Name = "x" }, context));
    }

    [Fact]
    public async Task UpdateAsync_RejectsBeforeReachingProvider()
    {
        var (context, typeMap) = CreateReadOnlyContext();
        await using var ctx = (DatabaseContext)context;
        var gateway = CreateGateway(context, typeMap);

        await Assert.ThrowsAsync<ReadOnlyContextException>(async () =>
            await gateway.UpdateAsync(new RoEntity { Id = 1, Name = "x" }, context));
    }

    [Fact]
    public async Task DeleteAsync_ById_RejectsBeforeReachingProvider()
    {
        var (context, typeMap) = CreateReadOnlyContext();
        await using var ctx = (DatabaseContext)context;
        var gateway = CreateGateway(context, typeMap);

        await Assert.ThrowsAsync<ReadOnlyContextException>(async () =>
            await gateway.DeleteAsync(1, context));
    }

    [Fact]
    public async Task UpsertAsync_RejectsBeforeReachingProvider()
    {
        var (context, typeMap) = CreateReadOnlyContext();
        await using var ctx = (DatabaseContext)context;
        var gateway = CreateGateway(context, typeMap);

        await Assert.ThrowsAsync<ReadOnlyContextException>(async () =>
            await gateway.UpsertAsync(new RoEntity { Id = 1, Name = "x" }, context));
    }

    [Fact]
    public async Task BatchCreateAsync_RejectsBeforeReachingProvider()
    {
        var (context, typeMap) = CreateReadOnlyContext();
        await using var ctx = (DatabaseContext)context;
        var gateway = CreateGateway(context, typeMap);

        await Assert.ThrowsAsync<ReadOnlyContextException>(async () =>
            await gateway.BatchCreateAsync(
                new List<RoEntity> { new() { Name = "a" }, new() { Name = "b" } }, context));
    }

    [Fact]
    public async Task BatchUpdateAsync_RejectsBeforeReachingProvider()
    {
        var (context, typeMap) = CreateReadOnlyContext();
        await using var ctx = (DatabaseContext)context;
        var gateway = CreateGateway(context, typeMap);

        await Assert.ThrowsAsync<ReadOnlyContextException>(async () =>
            await gateway.BatchUpdateAsync(
                new List<RoEntity> { new() { Id = 1, Name = "a" }, new() { Id = 2, Name = "b" } }, context));
    }

    [Fact]
    public async Task BatchDeleteAsync_RejectsBeforeReachingProvider()
    {
        var (context, typeMap) = CreateReadOnlyContext();
        await using var ctx = (DatabaseContext)context;
        var gateway = CreateGateway(context, typeMap);

        await Assert.ThrowsAsync<ReadOnlyContextException>(async () =>
            await gateway.DeleteAsync(new List<int> { 1, 2 }, context));
    }

    [Fact]
    public async Task BatchUpsertAsync_RejectsBeforeReachingProvider()
    {
        var (context, typeMap) = CreateReadOnlyContext();
        await using var ctx = (DatabaseContext)context;
        var gateway = CreateGateway(context, typeMap);

        await Assert.ThrowsAsync<ReadOnlyContextException>(async () =>
            await gateway.BatchUpsertAsync(
                new List<RoEntity> { new() { Id = 1, Name = "a" }, new() { Id = 2, Name = "b" } }, context));
    }

    [Fact]
    public async Task PrimaryKeyTableGateway_CreateAsync_RejectsBeforeReachingProvider()
    {
        var (context, typeMap) = CreateReadOnlyContext();
        await using var ctx = (DatabaseContext)context;
        var gateway = CreatePrimaryKeyGateway(context, typeMap);

        await Assert.ThrowsAsync<ReadOnlyContextException>(async () =>
            await gateway.CreateAsync(new RoPrimaryKeyEntity { Code = "a", Name = "x" }, context));
    }

    [Fact]
    public async Task PrimaryKeyTableGateway_BatchCreateAsync_RejectsBeforeReachingProvider()
    {
        var (context, typeMap) = CreateReadOnlyContext();
        await using var ctx = (DatabaseContext)context;
        var gateway = CreatePrimaryKeyGateway(context, typeMap);

        await Assert.ThrowsAsync<ReadOnlyContextException>(async () =>
            await gateway.BatchCreateAsync(
                new List<RoPrimaryKeyEntity> { new() { Code = "a", Name = "x" }, new() { Code = "b", Name = "y" } },
                context));
    }

    [Fact]
    public async Task RawSqlContainer_ExecuteNonQueryAsync_Write_RejectsBeforeReachingProvider()
    {
        var (context, _) = CreateReadOnlyContext();
        await using var ctx = (DatabaseContext)context;
        using var sc = context.CreateSqlContainer("DELETE FROM ro_entity WHERE id = 1");

        await Assert.ThrowsAsync<ReadOnlyContextException>(async () =>
            await sc.ExecuteNonQueryAsync());
    }

    [Fact]
    public void BeginTransaction_DefaultWriteIntent_RejectsBeforeAnyConnectionWork()
    {
        // Regression: BeginTransaction()'s default (write-intent) request on a ReadOnly context
        // rejected via a bare `NotSupportedException("Context is read-only.")`
        // (DatabaseContext.ResolveTransactionParameters) — none of the three documented
        // IReadOnlyViolation-marked types, unlike every other rejection path in this file. A
        // caller following the documented `catch (IReadOnlyViolation)` pattern would not catch
        // this one. This is actually the EARLIEST of all the rejection points (no connection is
        // ever touched), but the wrong exception type broke the uniform-catch contract.
        var (context, _) = CreateReadOnlyContext();
        using var ctx = (DatabaseContext)context;

        var ex = Assert.Throws<ReadOnlyContextException>(() => context.BeginTransaction());
        Assert.IsAssignableFrom<IReadOnlyViolation>(ex);
    }

    [Fact]
    public void BeginTransaction_ExplicitReadIntent_Succeeds()
    {
        // A ReadOnly context legitimately supports read-intent transactions (see
        // ISqlDialect.SupportsReadOnlyTransactions) — only the default/write-intent request is
        // rejected. Confirms the fix above doesn't overreach into rejecting reads too.
        var (context, _) = CreateReadOnlyContext();
        using var ctx = (DatabaseContext)context;

        using var txn = context.BeginTransaction(executionType: ExecutionType.Read);
        Assert.NotNull(txn);
    }

    [Fact]
    public async Task RetryContextScopedWrite_RejectsBeforeReachingProvider()
    {
        var (context, _) = CreateReadOnlyContext();
        await using var ctx = (DatabaseContext)context;

        await using var retry = new RetryContext(context, RetryContextType.Sequential);
        var sc = retry.CreateSqlContainer("DELETE FROM ro_entity WHERE id = 1");

        await Assert.ThrowsAsync<ReadOnlyContextException>(async () =>
            await sc.ExecuteNonQueryAsync());
    }
}
#pragma warning restore CS0618
