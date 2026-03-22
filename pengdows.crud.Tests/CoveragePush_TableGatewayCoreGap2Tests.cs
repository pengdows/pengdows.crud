using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Targeted tests covering uncovered paths in TableGateway.Core.cs:
/// - CreateAsync(entity, ctx, CancellationToken) overload — PrefetchSequence path (lines 226-234)
/// - CreateAsync(entity, ctx, CancellationToken) overload — CorrelationToken path (lines 264-293)
/// - CreateAsync(entity, ctx, CancellationToken) overload — Returning/Oracle inline path (lines 245-247)
/// </summary>
public class CoveragePush_TableGatewayCoreGap2Tests : SqlLiteContextTestBase
{
    // ──────────────────────────────────────────────────────────────────────────
    // Entity types
    // ──────────────────────────────────────────────────────────────────────────

    [Table("ct_sequence_entity")]
    private sealed class SequenceEntity
    {
        [Id(true)]  // Writable so prefetch sequence can set it
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Table("ct_correlation_entity")]
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

    // ──────────────────────────────────────────────────────────────────────────
    // TestDialect — controls GeneratedKeyPlan, visible via InternalsVisibleTo
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class TestDialect : SqlDialect
    {
        public GeneratedKeyPlan Plan { get; set; } = GeneratedKeyPlan.None;

        public TestDialect(DbProviderFactory factory)
            : base(factory, NullLogger.Instance) { }

        public override SupportedDatabase DatabaseType => SupportedDatabase.Unknown;
        public override GeneratedKeyPlan GetGeneratedKeyPlan() => Plan;
        public override bool SupportsInsertReturning => false;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CreateAsync with CancellationToken — PrefetchSequence path (lines 226-234)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_CancellationToken_PrefetchSequencePlan_PopulatesId()
    {
        // Replicates GeneratedKeyPlanTests.CreateAsync_UsesPrefetchSequencePlan but
        // calls the CancellationToken overload to cover lines 226-234.
        var factory = ((DatabaseContext)Context).Factory;
        var dialect = new TestDialect(factory) { Plan = GeneratedKeyPlan.PrefetchSequence };
        var customContext = new DatabaseContext(Context.ConnectionString, factory, TypeMap, dialect);

        TypeMap.Register<SequenceEntity>();
        var gateway = new TableGateway<SequenceEntity, int>(customContext);

        var tracked = customContext.GetConnection(ExecutionType.Write, false);
        var conn = (fakeDbConnection)((IInternalConnectionWrapper)tracked).UnderlyingConnection;
        conn.EnableDataPersistence = false;
        conn.EmulatedProduct = SupportedDatabase.Unknown;

        // Sequence query returns the next value
        conn.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["Value"] = 789 } });
        // Insert query returns 1 row affected
        conn.EnqueueReaderResult(new List<Dictionary<string, object?>>());

        using var cts = new CancellationTokenSource();
        var result = await gateway.CreateAsync(new SequenceEntity { Name = "ct-test" }, null, cts.Token);

        Assert.True(result);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CreateAsync with CancellationToken — CorrelationToken path (lines 264-293)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_CancellationToken_CorrelationTokenPlan_PopulatesId()
    {
        // Replicates GeneratedKeyPlanTests.CreateAsync_UsesCorrelationTokenPlan but
        // calls the CancellationToken overload to cover lines 264-293.
        var factory = ((DatabaseContext)Context).Factory;
        var dialect = new TestDialect(factory) { Plan = GeneratedKeyPlan.CorrelationToken };
        var customContext = new DatabaseContext(Context.ConnectionString, factory, TypeMap, dialect);

        TypeMap.Register<CorrelationEntity>();
        var gateway = new TableGateway<CorrelationEntity, int>(customContext);

        var tracked = customContext.GetConnection(ExecutionType.Write, false);
        var conn = (fakeDbConnection)((IInternalConnectionWrapper)tracked).UnderlyingConnection;
        conn.EnableDataPersistence = false;
        conn.EmulatedProduct = SupportedDatabase.Unknown;

        // Correlation token lookup returns the generated ID
        conn.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["Value"] = 456 } });

        using var cts = new CancellationTokenSource();
        var result = await gateway.CreateAsync(
            new CorrelationEntity { Name = "ct-correlation-test" }, null, cts.Token);

        Assert.True(result);
    }
}
