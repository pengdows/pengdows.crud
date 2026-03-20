using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.Tests.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

[Collection("SqliteSerial")]
public class ExecutionTranslationTests : SqlLiteContextTestBase
{
    [Table("versioned_jobs")]
    private sealed class VersionedJob
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [Version]
        [Column("version", DbType.Int32)]
        public int Version { get; set; }
    }

    public ExecutionTranslationTests()
    {
        TypeMap.Register<VersionedJob>();
        Context.CreateSqlContainer("""
            CREATE TABLE IF NOT EXISTS "versioned_jobs"(
                "id" INTEGER PRIMARY KEY AUTOINCREMENT,
                "name" TEXT NOT NULL,
                "version" INTEGER NOT NULL DEFAULT 0
            )
            """).ExecuteNonQueryAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_WrapsProviderException_IntoDatabaseException()
    {
        await using var failing = ConnectionFailureHelper.CreateFailOnCommandContext(
            SupportedDatabase.SqlServer,
            new NumberedDbException(2627, "Violation of PRIMARY KEY constraint"));
        await using var sc = failing.CreateSqlContainer("INSERT INTO jobs(id) VALUES (1)");

        var ex = await Assert.ThrowsAsync<UniqueConstraintViolationException>(async () =>
            await sc.ExecuteNonQueryAsync(CommandType.Text));

        Assert.Equal(SupportedDatabase.SqlServer, ex.Database);
        Assert.Equal(2627, ex.ErrorCode);
        Assert.IsType<NumberedDbException>(ex.InnerException);
    }

    [Fact]
    public async Task UpdateAsync_WithStaleVersion_ThrowsConcurrencyConflictException()
    {
        var gateway = new TableGateway<VersionedJob, int>(Context, AuditValueResolver);
        var row = new VersionedJob { Name = "job-a" };
        Assert.True(await gateway.CreateAsync(row, Context));

        var loaded = await gateway.RetrieveOneAsync(row.Id, Context);
        Assert.NotNull(loaded);

        await using (var bump = Context.CreateSqlContainer(
                         $"UPDATE {Context.WrapObjectName("versioned_jobs")} SET {Context.WrapObjectName("version")} = 2 WHERE {Context.WrapObjectName("id")} = 1"))
        {
            Assert.Equal(1, await bump.ExecuteNonQueryAsync());
        }

        loaded!.Name = "job-b";
        loaded.Version = 1;

        var ex = await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
            await gateway.UpdateAsync(loaded, false, Context));

        Assert.Equal(SupportedDatabase.Sqlite, ex.Database);
        Assert.Null(ex.ConstraintName);
    }
}
