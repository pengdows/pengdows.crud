#region

using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.Tests.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class EntityHelperAdditionalBranchTests : SqlLiteContextTestBase
{
    [Table("BranchTest")] private sealed class BranchEntity
    {
        [Id(writable: false)] [Column("Id", DbType.Int32)] public int Id { get; set; }
        [Column("Name", DbType.String)] public string Name { get; set; } = string.Empty;
        [Version] [Column("Version", DbType.Int32)] public int Version { get; set; }
    }

    public EntityHelperAdditionalBranchTests()
    {
        TypeMap.Register<BranchEntity>();
        var qp = Context.QuotePrefix; var qs = Context.QuoteSuffix;
        var create = $@"CREATE TABLE IF NOT EXISTS {qp}BranchTest{qs}(
            {qp}Id{qs} INTEGER PRIMARY KEY AUTOINCREMENT,
            {qp}Name{qs} TEXT NOT NULL,
            {qp}Version{qs} INTEGER NOT NULL DEFAULT 0
        )";
        Context.CreateSqlContainer(create).ExecuteNonQueryAsync().GetAwaiter().GetResult();

        // Seed a row
        var insert = Context.CreateSqlContainer($"INSERT INTO {qp}BranchTest{qs}({qp}Name{qs}) VALUES('n')");
        insert.ExecuteNonQueryAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task BuildUpdateAsync_NoChanges_Throws()
    {
        var helper = new EntityHelper<BranchEntity, int>(Context, AuditValueResolver);
        var loaded = await helper.RetrieveOneAsync(1);
        Assert.NotNull(loaded);

        // No changes to Name/Version
        await Assert.ThrowsAsync<InvalidOperationException>(() => helper.BuildUpdateAsync(loaded!, loadOriginal: true));
    }

    [Fact]
    public void BuildUpdateAsync_LoadOriginal_IdConversionFail_Throws()
    {
        var ex = Assert.Throws<TypeInitializationException>(() => new EntityHelper<BranchEntity, DateTime>(Context, AuditValueResolver));
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
        var helper = new EntityHelper<BranchEntity, int>((IDatabaseContext)failing, AuditValueResolver);
        var e = new BranchEntity { Id = 1, Name = "x" };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => helper.BuildUpdateAsync(e, loadOriginal: true));
        Assert.Contains("Original record not found", ex.Message);
    }
}
