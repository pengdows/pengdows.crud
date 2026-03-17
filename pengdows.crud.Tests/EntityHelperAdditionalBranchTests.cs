#region

using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.Tests.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class TableGatewayAdditionalBranchTests : RealSqliteContextTestBase
{
    [Table("BranchTest")]
    private sealed class BranchEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", DbType.String)] public string Name { get; set; } = string.Empty;

        [Version]
        [Column("Version", DbType.Int32)]
        public int Version { get; set; }
    }

    [Table("GuidBranchTest")]
    private sealed class GuidBranchEntity
    {
        [Id] [Column("Id", DbType.Guid)] public Guid Id { get; set; }

        [Column("Name", DbType.String)] public string Name { get; set; } = string.Empty;
    }

    [Table("AuditBranchTest")]
    private sealed class AuditBranchEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", DbType.String)] public string Name { get; set; } = string.Empty;

        [LastUpdatedOn]
        [Column("LastUpdatedOn", DbType.DateTime)]
        public DateTime LastUpdatedOn { get; set; }
    }

    public TableGatewayAdditionalBranchTests()
    {
        TypeMap.Register<BranchEntity>();
        TypeMap.Register<GuidBranchEntity>();
        TypeMap.Register<AuditBranchEntity>();
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var create = $@"CREATE TABLE IF NOT EXISTS {qp}BranchTest{qs}(
            {qp}Id{qs} INTEGER PRIMARY KEY AUTOINCREMENT,
            {qp}Name{qs} TEXT NOT NULL,
            {qp}Version{qs} INTEGER NOT NULL DEFAULT 0
        )";
        Context.CreateSqlContainer(create).ExecuteNonQueryAsync().GetAwaiter().GetResult();

        // Seed a row
        var insert = Context.CreateSqlContainer($"INSERT INTO {qp}BranchTest{qs}({qp}Name{qs}) VALUES('n')");
        insert.ExecuteNonQueryAsync().GetAwaiter().GetResult();

        var guidCreate = $@"CREATE TABLE IF NOT EXISTS {qp}GuidBranchTest{qs}(
            {qp}Id{qs} TEXT PRIMARY KEY NOT NULL,
            {qp}Name{qs} TEXT NOT NULL
        )";
        Context.CreateSqlContainer(guidCreate).ExecuteNonQueryAsync().GetAwaiter().GetResult();

        var auditCreate = $@"CREATE TABLE IF NOT EXISTS {qp}AuditBranchTest{qs}(
            {qp}Id{qs} INTEGER PRIMARY KEY AUTOINCREMENT,
            {qp}Name{qs} TEXT NOT NULL,
            {qp}LastUpdatedOn{qs} TIMESTAMP NOT NULL
        )";
        Context.CreateSqlContainer(auditCreate).ExecuteNonQueryAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task BuildUpdateAsync_NoChanges_Throws()
    {
        var helper = new TableGateway<BranchEntity, int>(Context, AuditValueResolver);
        var loaded = await helper.RetrieveOneAsync(1);
        Assert.NotNull(loaded);

        // No changes to Name/Version
        await Assert.ThrowsAsync<InvalidOperationException>(() => helper.BuildUpdateAsync(loaded!, true).AsTask());
    }

    [Fact]
    public void BuildUpdateAsync_LoadOriginal_IdConversionFail_Throws()
    {
        var ex = Assert.Throws<TypeInitializationException>(() =>
            new TableGateway<BranchEntity, DateTime>(Context, AuditValueResolver));
        Assert.NotNull(ex.InnerException);
        Assert.IsType<NotSupportedException>(ex.InnerException);
        Assert.Contains("TRowID type 'System.DateTime' is not supported", ex.InnerException.Message);
    }

    [Fact]
    public async Task BuildUpdateAsync_LoadOriginal_DbException_ReturnsNotFound()
    {
        // Create a context whose commands fail (DbException) to trigger the null path in LoadOriginalAsync
        var dbException = ConnectionFailureHelper.CommonExceptions.CreateDbException("Simulated database error");
        await using var failing = ConnectionFailureHelper.CreateFailOnCommandContext(customException: dbException);
        var helper = new TableGateway<BranchEntity, int>((IDatabaseContext)failing, AuditValueResolver);
        var e = new BranchEntity { Id = 1, Name = "x" };
        var ex = await Record.ExceptionAsync(() => helper.BuildUpdateAsync(e, true).AsTask());
        Assert.NotNull(ex);
        Assert.IsAssignableFrom<DbException>(ex);
        Assert.Contains("Simulated database error", ex!.Message);
    }

    [Fact]
    public async Task BuildUpdateAsync_LoadOriginal_GuidId_UsesGuidValue()
    {
        var helper = new TableGateway<GuidBranchEntity, Guid>(Context, AuditValueResolver);
        var id = Guid.NewGuid();
        await helper.CreateAsync(new GuidBranchEntity { Id = id, Name = "n" });

        var entity = new GuidBranchEntity { Id = id, Name = "updated" };
        var container = await helper.BuildUpdateAsync(entity, true);

        Assert.NotNull(container);
    }

    [Fact]
    public async Task BuildUpdateAsync_AuditOnlyChange_IncludesAuditColumns()
    {
        var helper = new TableGateway<AuditBranchEntity, int>(Context, AuditValueResolver);
        var entity = new AuditBranchEntity { Name = "n" };
        await helper.CreateAsync(entity);

        var loaded = await helper.RetrieveOneAsync(entity.Id);
        Assert.NotNull(loaded);

        var sc = await helper.BuildUpdateAsync(loaded!, true);
        var sql = sc.Query.ToString();
        Assert.Contains(Context.WrapObjectName("LastUpdatedOn"), sql);
    }

    [Fact]
    public async Task BuildUpdateAsync_AuditWithBusinessChange_Succeeds()
    {
        var helper = new TableGateway<AuditBranchEntity, int>(Context, AuditValueResolver);
        var entity = new AuditBranchEntity { Name = "n2" };
        await helper.CreateAsync(entity);

        var loaded = await helper.RetrieveOneAsync(entity.Id);
        Assert.NotNull(loaded);
        loaded!.Name = "updated";

        var container = await helper.BuildUpdateAsync(loaded, true);

        Assert.NotNull(container);
    }
}
