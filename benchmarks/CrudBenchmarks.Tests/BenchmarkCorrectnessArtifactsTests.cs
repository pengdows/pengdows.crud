using System.IO;
using CrudBenchmarks;

namespace CrudBenchmarks.Tests;

// Items 8 and 9 from the independent StormGate/benchmark architecture review:
//
// Item 9: "the custom correctness artifact was written into BenchmarkDotNet's
// temporary/isolated output and then disappeared during cleanup. The benchmark needs durable
// counters/postconditions." Confirmed by reading the code: BenchmarkCorrectnessArtifacts wrote to
// a path relative to Directory.GetCurrentDirectory(), which for BenchmarkDotNet's default
// out-of-process toolchain is a per-run generated project directory under
// CrudBenchmarks/bin/<config>/<tfm>/<guid>/ that BDN deletes during its own artifact cleanup —
// exactly what benchmarks/CrudBenchmarks/results/sqlite-write-contention-run-2026-08-13.md's own
// "Note on artifact durability" section reports happening to it.
//
// The compounding bug this file locks in: CountFailures (and therefore the "Fails" column
// CorrectnessColumn renders in every benchmark report) returned a plain 0 whenever the artifact
// file was simply missing — identical to what it returns when the artifact exists and genuinely
// records zero issues. A reader has no way to distinguish "verified zero failures" from "we don't
// know, the file wasn't there." That is exactly how the 2026-08-13 report could show "Fails: 0"
// for Dapper/EF in the same table where "Exceptions during run" (from BDN's own preserved console
// log) shows 268 and 348 — the correctness artifact behind the Fails column was already gone by
// the time CountFailures ran.
public sealed class BenchmarkCorrectnessArtifactsTests : IDisposable
{
    private const string ArtifactsDirEnvVar = "CRUD_BENCH_ARTIFACTS_DIR";
    private readonly string _tempDir;
    private readonly string? _originalEnvVar;

    public BenchmarkCorrectnessArtifactsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "crud-bench-correctness-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _originalEnvVar = Environment.GetEnvironmentVariable(ArtifactsDirEnvVar);
        Environment.SetEnvironmentVariable(ArtifactsDirEnvVar, _tempDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(ArtifactsDirEnvVar, _originalEnvVar);
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public void CountFailures_WhenArtifactFileIsMissing_ReturnsNull_NotZero()
    {
        var count = BenchmarkCorrectnessArtifacts.CountFailures(
            "CrudBenchmarks.SomeBenchmark-20260101-000000",
            parameterKey: null!,
            scenario: "WriteStorm",
            frameworkName: "Dapper");

        Assert.False(count.HasValue, $"Expected null (unknown), got {count}");
    }

    [Fact]
    public void CountFailures_WhenArtifactExistsWithNoMatchingIssues_ReturnsZero_DistinctFromMissing()
    {
        BenchmarkCorrectnessArtifacts.Write("SomeBenchmark", Array.Empty<CorrectnessIssue>());

        var count = BenchmarkCorrectnessArtifacts.CountFailures(
            "CrudBenchmarks.SomeBenchmark-20260101-000000",
            parameterKey: null!,
            scenario: "WriteStorm",
            frameworkName: "Dapper");

        Assert.Equal(0, count);
    }

    [Fact]
    public void CountFailures_WhenArtifactRecordsMatchingIssues_ReturnsTheirSummedCount()
    {
        BenchmarkCorrectnessArtifacts.Write("SomeBenchmark", new[]
        {
            new CorrectnessIssue(null, "WriteStorm", "Dapper", "Exception: SqliteException", 268),
            new CorrectnessIssue(null, "WriteStorm", "EntityFramework", "Exception: SqliteException", 348),
        });

        var dapperCount = BenchmarkCorrectnessArtifacts.CountFailures(
            "CrudBenchmarks.SomeBenchmark-20260101-000000", null!, "WriteStorm", "Dapper");
        var efCount = BenchmarkCorrectnessArtifacts.CountFailures(
            "CrudBenchmarks.SomeBenchmark-20260101-000000", null!, "WriteStorm", "EntityFramework");

        Assert.Equal(268, dapperCount);
        Assert.Equal(348, efCount);
    }

    [Fact]
    public void Write_ThenRead_RoundTripsThroughTheConfiguredArtifactsDirectory_NotTheCurrentDirectory()
    {
        // Proves the fix for item 9: the artifact lands in CRUD_BENCH_ARTIFACTS_DIR, not a path
        // relative to whatever the process's current directory happens to be at write time — the
        // exact thing that made the artifact unrecoverable under BenchmarkDotNet's generated,
        // cleaned-up run directories.
        BenchmarkCorrectnessArtifacts.Write("SomeBenchmark", new[]
        {
            new CorrectnessIssue(null, "WriteStorm", "Dapper", "Exception: SqliteException", 5),
        });

        var expectedPath = Path.Combine(_tempDir, "SomeBenchmark-correctness.json");
        Assert.True(File.Exists(expectedPath), $"Expected artifact at {expectedPath}");
    }
}
