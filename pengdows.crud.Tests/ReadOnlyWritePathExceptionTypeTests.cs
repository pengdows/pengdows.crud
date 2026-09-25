using System;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// BP-208 (3.0 7db5f4c): every write attempted on a ReadWriteMode.ReadOnly context must be rejected
/// with the same exception type before any provider command runs. The reader write path (used by
/// generated-key retrieval) threw InvalidOperationException from the connection check while every
/// other write path threw NotSupportedException.
/// </summary>
public class ReadOnlyWritePathExceptionTypeTests
{
    private static DatabaseContext CreateReadOnlyContext() =>
        new(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadOnly
        }, new fakeDbFactory(SupportedDatabase.Sqlite));

    [Fact]
    public async Task ExecuteNonQueryAsync_OnReadOnlyContext_ThrowsNotSupported()
    {
        using var context = CreateReadOnlyContext();
        await using var sc = context.CreateSqlContainer("DELETE FROM t");

        await Assert.ThrowsAsync<NotSupportedException>(async () => await sc.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task ExecuteScalarWriteAsync_OnReadOnlyContext_ThrowsNotSupported()
    {
        using var context = CreateReadOnlyContext();
        await using var sc = context.CreateSqlContainer("INSERT INTO t VALUES (1) RETURNING id");

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await sc.ExecuteScalarRequiredAsync<long>(ExecutionType.Write));
    }

    [Fact]
    public async Task ExecuteReaderAsync_WriteExecution_OnReadOnlyContext_ThrowsNotSupported()
    {
        using var context = CreateReadOnlyContext();
        await using var sc = context.CreateSqlContainer("INSERT INTO t VALUES (1) RETURNING id");

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await sc.ExecuteReaderAsync(ExecutionType.Write));
    }

    [Fact]
    public void BeginTransaction_Write_OnReadOnlyContext_ThrowsNotSupported()
    {
        using var context = CreateReadOnlyContext();

        Assert.Throws<NotSupportedException>(() => context.BeginTransaction(executionType: ExecutionType.Write));
    }
}
