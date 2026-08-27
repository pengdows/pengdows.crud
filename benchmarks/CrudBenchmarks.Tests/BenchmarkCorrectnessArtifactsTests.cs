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
        // Proves the fix for item 9: the artifact lands under CRUD_BENCH_ARTIFACTS_DIR, not a
        // path relative to whatever the process's current directory happens to be at write
        // time — the exact thing that made the artifact unrecoverable under BenchmarkDotNet's
        // generated, cleaned-up run directories. It lands in a "correctness-fragments"
        // subdirectory, one file per process ID, per the fix below.
        BenchmarkCorrectnessArtifacts.Write("SomeBenchmark", new[]
        {
            new CorrectnessIssue(null, "WriteStorm", "Dapper", "Exception: SqliteException", 5),
        });

        var fragmentsDir = Path.Combine(_tempDir, "correctness-fragments");
        var matches = Directory.Exists(fragmentsDir)
            ? Directory.GetFiles(fragmentsDir, "SomeBenchmark-*-correctness.json")
            : Array.Empty<string>();
        Assert.True(matches.Length == 1, $"Expected exactly one fragment under {fragmentsDir}, found {matches.Length}");
    }

    // Regression test for the real bug found on 2026-08-27: each [Benchmark] method
    // (Pengdows/Dapper/EntityFramework) runs in its own separately spawned process. The
    // original implementation wrote one shared file per benchmark class with
    // File.WriteAllText, so whichever process's Cleanup() happened to run (or finish) last
    // silently overwrote every earlier framework's recorded issues — confirmed in practice on
    // ConnectionPoolProtectionBenchmarks, where the surviving file only ever reflected
    // whichever framework/scenario ran dead last across the whole class, making "Fails: 0" for
    // every other row (including every Pengdows row) look verified when it never was. This
    // test simulates two different processes' fragments existing simultaneously (as they would
    // mid-run, before any single "last write" could clobber the others) and asserts both
    // frameworks' issues are visible, not just the most-recently-written one.
    [Fact]
    public void CountFailures_MergesIssuesAcrossMultipleProcessFragments_InsteadOfOverwriting()
    {
        // Simulate the Dapper process's fragment (written first, in real usage).
        WriteRawFragment("SomeBenchmark", processId: 1001, new[]
        {
            new CorrectnessIssue(null, "WriteStorm", "Dapper", "Exception: SqliteException", 268),
        });

        // Simulate the EntityFramework process's fragment (written last, in real usage — the
        // one that used to silently erase Dapper's fragment above under the old shared-file
        // design).
        WriteRawFragment("SomeBenchmark", processId: 1002, new[]
        {
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
    public void ClearFragmentsFromPreviousRun_RemovesStaleFragments_SoTheyCannotPolluteAFreshRun()
    {
        BenchmarkCorrectnessArtifacts.Write("SomeBenchmark", new[]
        {
            new CorrectnessIssue(null, "WriteStorm", "Dapper", "Exception: SqliteException", 5),
        });

        BenchmarkCorrectnessArtifacts.ClearFragmentsFromPreviousRun();

        var count = BenchmarkCorrectnessArtifacts.CountFailures(
            "CrudBenchmarks.SomeBenchmark-20260101-000000", null!, "WriteStorm", "Dapper");
        Assert.False(count.HasValue, $"Expected null after clearing stale fragments, got {count}");
    }

    // Regression test for the third silent-failure instance found on 2026-08-27:
    // PostgreSqlConnectionGovernanceBenchmarks' Dapper_StormGate method has zero correctness
    // issues to record (it's the governed, succeeding path), so its fragment's Issues array is
    // empty — exactly like "no fragment was ever written." CorrectnessColumn needs to tell
    // these apart: a class with fragments but an unresolvable method identity should warn ("?"),
    // while a class with no fragments at all should render "-" without a warning. This proves
    // the fact HasFragmentsFor reports (fragment existence) is independent of whether that
    // fragment's own Issues array happens to be empty.
    [Fact]
    public void HasFragmentsFor_WhenFragmentExistsWithNoIssues_StillReportsTrue()
    {
        BenchmarkCorrectnessArtifacts.Write("SomeBenchmark", Array.Empty<CorrectnessIssue>());

        Assert.True(BenchmarkCorrectnessArtifacts.HasFragmentsFor("CrudBenchmarks.SomeBenchmark-20260101-000000"));
    }

    [Fact]
    public void HasFragmentsFor_WhenNoFragmentWasEverWritten_ReportsFalse()
    {
        Assert.False(BenchmarkCorrectnessArtifacts.HasFragmentsFor("CrudBenchmarks.SomeOtherBenchmark-20260101-000000"));
    }

    // Regression test for the missing-denominator gap flagged on 2026-08-27: 1,950/2,158
    // failure counts for PostgreSqlConnectionGovernanceBenchmarks had no recorded total attempt
    // count alongside them, forcing the count to be reconstructed after the fact from
    // BenchmarkDotNet job config instead of read directly. Write() now accepts an optional
    // totalAttempted value; this proves it round-trips through the fragment file.
    [Fact]
    public void Write_WithTotalAttempted_RoundTripsThroughTheFragmentFile()
    {
        BenchmarkCorrectnessArtifacts.Write(
            "SomeBenchmark",
            new[] { new CorrectnessIssue(null, "Uncontrolled", "Dapper", "Exception: PostgresException", 1950) },
            totalAttempted: 4000);

        var fragmentsDir = Path.Combine(_tempDir, "correctness-fragments");
        var path = Directory.GetFiles(fragmentsDir, "SomeBenchmark-*-correctness.json").Single();
        var json = File.ReadAllText(path);

        Assert.Contains("\"totalAttempted\": 4000", json);
    }

    private void WriteRawFragment(string benchmarkClassName, int processId, CorrectnessIssue[] issues)
    {
        var fragmentsDir = Path.Combine(_tempDir, "correctness-fragments");
        Directory.CreateDirectory(fragmentsDir);
        var path = Path.Combine(fragmentsDir, $"{benchmarkClassName}-{processId}-correctness.json");
        var json = System.Text.Json.JsonSerializer.Serialize(
            new { BenchmarkClassName = benchmarkClassName, GeneratedUtc = DateTime.UtcNow, Issues = issues },
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
        File.WriteAllText(path, json);
    }
}
