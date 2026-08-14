using System.Data;
using BenchmarkDotNet.Attributes;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;

namespace CrudBenchmarks.Internal;

/// <summary>
/// Isolates the CPU-only cost of TableGateway.CreateAsync's audit-field snapshot/restore
/// bookkeeping (SnapshotAuditFields / RestoreAuditFieldsIfFailed), which runs on every
/// CreateAsync call — success or failure — regardless of whether the entity has audit columns.
/// Uses fakeDb (in-memory, no real I/O) so timings reflect framework overhead, not database
/// or disk latency. Compares an entity with no audit columns (snapshot is a true no-op) against
/// an otherwise-identical entity with all four audit columns (snapshot does real reflection
/// GetValue calls) to bound the added cost per Create.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 20, invocationCount: 8192)]
public class CreateAsyncAuditOverheadBenchmarks
{
    private TableGateway<PlainEntity, int> _plainGateway = null!;
    private TableGateway<AuditedEntity, int> _auditedGateway = null!;
    private int _counter;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "fake",
            ProviderName = SupportedDatabase.PostgreSql.ToString(),
            DbMode = DbMode.Standard
        };

        var plainCtx = new DatabaseContext(config, factory);
        _plainGateway = new TableGateway<PlainEntity, int>(plainCtx);

        var auditedCtx = new DatabaseContext(config, factory);
        _auditedGateway = new TableGateway<AuditedEntity, int>(auditedCtx, new BenchAuditValueResolver());
    }

    [Benchmark(Baseline = true)]
    public async Task<bool> CreateAsync_NoAuditColumns()
    {
        var entity = new PlainEntity { Name = "row-" + _counter++ };
        return await _plainGateway.CreateAsync(entity);
    }

    [Benchmark]
    public async Task<bool> CreateAsync_WithAuditColumns()
    {
        var entity = new AuditedEntity { Name = "row-" + _counter++ };
        return await _auditedGateway.CreateAsync(entity);
    }

    [Table("plain_entity")]
    public sealed class PlainEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
    }

    [Table("audited_entity")]
    public sealed class AuditedEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;

        [CreatedOn]
        [Column("created_on", DbType.DateTime)]
        public DateTime CreatedOn { get; set; }

        [CreatedBy]
        [Column("created_by", DbType.String)]
        public string CreatedBy { get; set; } = string.Empty;

        [LastUpdatedOn]
        [Column("last_updated_on", DbType.DateTime)]
        public DateTime LastUpdatedOn { get; set; }

        [LastUpdatedBy]
        [Column("last_updated_by", DbType.String)]
        public string LastUpdatedBy { get; set; } = string.Empty;
    }

    private sealed class BenchAuditValueResolver : IAuditValueResolver
    {
        public IAuditValues Resolve() => new AuditValues { UserId = "bench-user", UtcNow = DateTime.UtcNow };
    }
}
