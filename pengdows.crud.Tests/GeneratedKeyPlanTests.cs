using System;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
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
        [Id(true)] // Must be writable for prefetch
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
}
