using System.Reflection;
using BenchmarkDotNet.Attributes;
using CrudBenchmarks;
using Dapper;
using Microsoft.Data.Sqlite;

namespace CrudBenchmarks.Tests;

// Every arm of the file-backed SQLite writer benchmarks has to actually do its work: a pinned-writer or queue arm that
// quietly writes nothing would look very fast. These run each arm once, small, and check the database afterwards.
// They also pin the shape of the classes (which arms exist, and that none is missing a counterpart).
public sealed class SqliteFileWriterBenchmarksTests
{
    private static async Task<int> ValueAsync(SqliteFileWriterBenchmarks b, int id)
    {
        await using var connection = new SqliteConnection(b.DatabaseConnectionStringForTests);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>("SELECT value FROM stress_test WHERE id = @id", new { id });
    }

    private static async Task<SqliteFileWriterBenchmarks> NewSequentialAsync()
    {
        var b = new SqliteFileWriterBenchmarks { RecordCount = 3 };
        await b.GlobalSetup();
        return b;
    }

    public static IEnumerable<object[]> WriteArms()
    {
        yield return new object[] { nameof(SqliteFileWriterBenchmarks.Write_Dapper) };
        yield return new object[] { nameof(SqliteFileWriterBenchmarks.Write_DapperPinnedWriter) };
        yield return new object[] { nameof(SqliteFileWriterBenchmarks.Write_DapperWriterQueue) };
        yield return new object[] { nameof(SqliteFileWriterBenchmarks.Write_Pengdows) };
    }

    [Theory]
    [MemberData(nameof(WriteArms))]
    public async Task EveryWriteArm_UpdatesTheRowsItIsMeantTo(string arm)
    {
        var b = await NewSequentialAsync();
        try
        {
            var seeded = await ValueAsync(b, 1);
            var method = typeof(SqliteFileWriterBenchmarks).GetMethod(arm)!;

            var affected = await (Task<int>)method.Invoke(b, null)!;

            Assert.Equal(3, affected);
            Assert.NotEqual(seeded, await ValueAsync(b, 1));
            Assert.Equal(0, await ValueAsync(b, 1)); // write i sets value = i, ids 1..3 get 0..2
            Assert.Equal(2, await ValueAsync(b, 3));
        }
        finally
        {
            b.GlobalCleanup();
        }
    }

    public static IEnumerable<object[]> ReadArms()
    {
        yield return new object[] { nameof(SqliteFileWriterBenchmarks.ReadSingle_Dapper) };
        yield return new object[] { nameof(SqliteFileWriterBenchmarks.ReadSingle_DapperSingleConnection) };
        yield return new object[] { nameof(SqliteFileWriterBenchmarks.ReadSingle_Pengdows) };
    }

    [Theory]
    [MemberData(nameof(ReadArms))]
    public async Task EveryReadArm_ReturnsTheRowItAsksFor(string arm)
    {
        var b = await NewSequentialAsync();
        try
        {
            var method = typeof(SqliteFileWriterBenchmarks).GetMethod(arm)!;

            var value = await (Task<int>)method.Invoke(b, null)!;

            // the last of three reads asks for id 3, whose seeded value is id * 10
            Assert.Equal(30, value);
        }
        finally
        {
            b.GlobalCleanup();
        }
    }

    [Fact]
    public void TheSequentialClass_HasEveryWriterPattern_AndABaselineToCompareAgainst()
    {
        var methods = typeof(SqliteFileWriterBenchmarks).GetMethods()
            .Where(m => m.GetCustomAttribute<BenchmarkAttribute>() != null)
            .Select(m => m.Name)
            .ToHashSet();

        foreach (var arm in new[]
                 {
                     "Write_Dapper", "Write_DapperPinnedWriter", "Write_DapperWriterQueue", "Write_Pengdows",
                     "ReadSingle_Dapper", "ReadSingle_DapperSingleConnection", "ReadSingle_Pengdows"
                 })
        {
            Assert.Contains(arm, methods);
        }

        // One baseline per category (writes and reads), so each ratio is against per-operation Dapper in its own group.
        Assert.Equal(2, typeof(SqliteFileWriterBenchmarks).GetMethods()
            .Count(m => m.GetCustomAttribute<BenchmarkAttribute>()?.Baseline == true));
    }

    // ---- storms --------------------------------------------------------------------------------------------

    private static async Task<SqliteFileWriterStormBenchmarks> NewStormAsync()
    {
        var b = new SqliteFileWriterStormBenchmarks
        {
            BusyTimeoutMs = 5000,
            StormConcurrency = 6,
            WritesPerTransaction = 4,
            MixedWriters = 3,
            WritesPerWriter = 5,
            MixedReaders = 3
        };
        await b.GlobalSetup();
        return b;
    }

    public static IEnumerable<object[]> CoordinatedStormArms()
    {
        yield return new object[] { nameof(SqliteFileWriterStormBenchmarks.Storm_DapperPinnedWriter) };
        yield return new object[] { nameof(SqliteFileWriterStormBenchmarks.Storm_DapperWriterQueue) };
        yield return new object[] { nameof(SqliteFileWriterStormBenchmarks.Storm_Pengdows) };
    }

    [Theory]
    [MemberData(nameof(CoordinatedStormArms))]
    public async Task EveryCoordinatedStormArm_CommitsEveryTransaction(string arm)
    {
        var b = await NewStormAsync();
        try
        {
            await (Task)typeof(SqliteFileWriterStormBenchmarks).GetMethod(arm)!.Invoke(b, null)!;

            Assert.Equal(6, b.CommittedTransactions);
            Assert.Equal(0, b.FailedTransactions);
        }
        finally
        {
            b.GlobalCleanup();
        }
    }

    [Fact]
    public async Task TheUncoordinatedStormArm_AccountsForEveryTransaction_WhetherItCommittedOrFailed()
    {
        var b = await NewStormAsync();
        try
        {
            await b.Storm_Dapper();

            Assert.Equal(6, b.CommittedTransactions + b.FailedTransactions);
        }
        finally
        {
            b.GlobalCleanup();
        }
    }

    [Fact]
    public async Task TheDefaultsArm_AccountsForEveryTransaction_AndMayRejectSomeByDesign()
    {
        var b = await NewStormAsync();
        try
        {
            await b.Storm_PengdowsDefaults();

            Assert.Equal(6, b.CommittedTransactions + b.FailedTransactions);
        }
        finally
        {
            b.GlobalCleanup();
        }
    }

    public static IEnumerable<object[]> MixedArms()
    {
        yield return new object[] { nameof(SqliteFileWriterStormBenchmarks.Mixed_Dapper) };
        yield return new object[] { nameof(SqliteFileWriterStormBenchmarks.Mixed_DapperPinnedWriter) };
        yield return new object[] { nameof(SqliteFileWriterStormBenchmarks.Mixed_DapperSingleConnection) };
        yield return new object[] { nameof(SqliteFileWriterStormBenchmarks.Mixed_DapperWriterQueue) };
        yield return new object[] { nameof(SqliteFileWriterStormBenchmarks.Mixed_Pengdows) };
    }

    [Theory]
    [MemberData(nameof(MixedArms))]
    public async Task EveryMixedArm_CompletesEveryWrite_AndReadsWhileTheyRun(string arm)
    {
        var b = await NewStormAsync();
        try
        {
            await (Task)typeof(SqliteFileWriterStormBenchmarks).GetMethod(arm)!.Invoke(b, null)!;

            Assert.Equal(3 * 5, b.CommittedWrites);
            Assert.Equal(0, b.FailedWrites);
            Assert.True(b.CompletedReads > 0, "the readers never ran, so the arm measured nothing about reads");
        }
        finally
        {
            b.GlobalCleanup();
        }
    }

    // ---- poisoned writer -----------------------------------------------------------------------------------

    private static async Task<SqliteFileWriterPoisonBenchmarks> NewPoisonAsync()
    {
        var b = new SqliteFileWriterPoisonBenchmarks { TransactionCount = 20, SeverAtTransaction = 10 };
        await b.GlobalSetup();
        return b;
    }

    [Fact]
    public async Task APinnedWriterWithNoRecovery_FailsEveryTransactionFromTheSeverOn()
    {
        var b = await NewPoisonAsync();
        try
        {
            await b.Poison_DapperPinnedWriter();

            Assert.Equal(10, b.FailedTransactions); // transactions 10..19
            Assert.Equal(10, b.CommittedTransactions);
        }
        finally
        {
            b.GlobalCleanup();
        }
    }

    [Theory]
    [InlineData(nameof(SqliteFileWriterPoisonBenchmarks.Poison_DapperPinnedWriterWithReconnect))]
    [InlineData(nameof(SqliteFileWriterPoisonBenchmarks.Poison_Pengdows))]
    public async Task ARecoveringWriter_LosesOnlyTheTransactionInFlightWhenTheConnectionDies(string arm)
    {
        var b = await NewPoisonAsync();
        try
        {
            await (Task)typeof(SqliteFileWriterPoisonBenchmarks).GetMethod(arm)!.Invoke(b, null)!;

            Assert.Equal(1, b.FailedTransactions);
            Assert.Equal(19, b.CommittedTransactions);
        }
        finally
        {
            b.GlobalCleanup();
        }
    }
}
