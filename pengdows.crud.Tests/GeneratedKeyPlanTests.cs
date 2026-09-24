using System;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

public class GeneratedKeyPlanTests : SqlLiteContextTestBase
{
    private sealed class TestDialect : SqlDialect
    {
        public GeneratedKeyPlan Plan { get; set; } = GeneratedKeyPlan.None;

        public TestDialect(DbProviderFactory factory)
            : base(factory, NullLogger.Instance) { }

        public override SupportedDatabase DatabaseType => SupportedDatabase.Unknown;
        public override GeneratedKeyPlan GetGeneratedKeyPlan() => Plan;
        public override bool SupportsInsertReturning => false;
    }

    [Table("correlation_entity")]
    private sealed class CorrelationEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [CorrelationToken]
        [Column("token", DbType.String)]
        public string Token { get; set; } = string.Empty;

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public async Task CreateAsync_UsesCorrelationTokenPlan()
    {
        var factory = ((DatabaseContext)Context).Factory;
        var dialect = new TestDialect(factory);
        dialect.Plan = GeneratedKeyPlan.CorrelationToken;

        // We need a context that uses our TestDialect
        var customContext = new DatabaseContext(Context.ConnectionString, factory, TypeMap, dialect);

        TypeMap.Register<CorrelationEntity>();
        var gateway = new TableGateway<CorrelationEntity, int>(customContext);

        var entity = new CorrelationEntity { Name = "Test" };

        // Mock connection behavior
        var tracked = customContext.GetConnection(ExecutionType.Write, false);
        var conn = (fakeDbConnection)((IInternalConnectionWrapper)tracked).UnderlyingConnection;
        conn.EnableDataPersistence = false;
        conn.EmulatedProduct = SupportedDatabase.Unknown;

        // INSERT uses ExecuteNonQuery (no reader consumed).
        // The correlation token lookup uses ExecuteReaderAsync internally (via ExecuteScalarCore).
        conn.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["Value"] = 123 } });

        var result = await gateway.CreateAsync(entity);

        Assert.True(result);
        Assert.Equal(123, entity.Id);
        Assert.False(string.IsNullOrEmpty(entity.Token)); // Gateway should have generated a token
    }

    [Table("sequence_entity")]
    private sealed class SequenceEntity
    {
        [Id(false)] // Not client-writable: PrefetchSequence only overwrites the Id when it is not writable
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public async Task CreateAsync_UsesPrefetchSequencePlan()
    {
        var factory = ((DatabaseContext)Context).Factory;
        var dialect = new TestDialect(factory);
        dialect.Plan = GeneratedKeyPlan.PrefetchSequence;

        var customContext = new DatabaseContext(Context.ConnectionString, factory, TypeMap, dialect);

        TypeMap.Register<SequenceEntity>();
        var gateway = new TableGateway<SequenceEntity, int>(customContext);

        var entity = new SequenceEntity { Name = "Test" };

        // Mock connection behavior
        var tracked = customContext.GetConnection(ExecutionType.Write, false);
        var conn = (fakeDbConnection)((IInternalConnectionWrapper)tracked).UnderlyingConnection;
        conn.EnableDataPersistence = false;
        conn.EmulatedProduct = SupportedDatabase.Unknown;

        // 1. Mock sequence nextval result
        conn.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["Value"] = 456 } });
        // 2. Mock insert result (empty row list)
        conn.EnqueueReaderResult(new List<Dictionary<string, object?>>());

        var result = await gateway.CreateAsync(entity);

        Assert.True(result);
        Assert.Equal(456, entity.Id);
    }

    [Table("sequence_entity")]
    private sealed class WritableIdSequenceEntity
    {
        [Id(true)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    private (TableGateway<WritableIdSequenceEntity, int> Gateway, fakeDbConnection Conn) CreateWritableIdPrefetchGateway()
    {
        var factory = ((DatabaseContext)Context).Factory;
        var dialect = new TestDialect(factory) { Plan = GeneratedKeyPlan.PrefetchSequence };
        var customContext = new DatabaseContext(Context.ConnectionString, factory, TypeMap, dialect);

        TypeMap.Register<WritableIdSequenceEntity>();
        var gateway = new TableGateway<WritableIdSequenceEntity, int>(customContext);

        var tracked = customContext.GetConnection(ExecutionType.Write, false);
        var conn = (fakeDbConnection)((IInternalConnectionWrapper)tracked).UnderlyingConnection;
        conn.EnableDataPersistence = false;
        conn.EmulatedProduct = SupportedDatabase.Unknown;

        // If the gateway (wrongly) prefetched, this is the sequence value it would read.
        conn.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["Value"] = 999 } });
        conn.EnqueueNonQueryResult(1);
        return (gateway, conn);
    }

    /// <summary>
    /// A caller-supplied (writable) Id must survive CreateAsync under PrefetchSequence (InterBase's
    /// plan). Previously the sequence value silently replaced it, so the row was stored under an
    /// Id the caller never saw.
    /// </summary>
    [Fact]
    public async Task CreateAsync_PrefetchSequence_WritableId_KeepsCallerSuppliedId()
    {
        var (gateway, _) = CreateWritableIdPrefetchGateway();
        var entity = new WritableIdSequenceEntity { Id = 42, Name = "Test" };

        var result = await gateway.CreateAsync(entity);

        Assert.True(result);
        Assert.Equal(42, entity.Id);
    }

    [Fact]
    public async Task CreateAsync_WithCancellationToken_PrefetchSequence_WritableId_KeepsCallerSuppliedId()
    {
        var (gateway, _) = CreateWritableIdPrefetchGateway();
        var entity = new WritableIdSequenceEntity { Id = 42, Name = "Test" };

        var result = await gateway.CreateAsync(entity, null, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(42, entity.Id);
    }
}
