using System;
using System.IO;
using System.Linq;
using CrudBenchmarks;
using Xunit;

namespace CrudBenchmarks.Tests;

// Each benchmark case runs in its own BenchmarkDotNet child process, so it records its SQL as a
// fragment file; the parent process merges them into one report after the run.
public sealed class SqlProofArtifactsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sqlproof-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static SqlProofCase Case(string job, string framework) =>
        new("ReadSingle", framework, job, EqualFooting: true, 40,
            new[] { new SqlProofStatement("SELECT id FROM benchmark WHERE id = $1", 40) });

    [Fact]
    public void WriteThenReadAll_RoundTripsEveryCase()
    {
        SqlProofArtifacts.Write(_dir, Case("Baseline", "Dapper"));
        SqlProofArtifacts.Write(_dir, Case("Pinned", "Pengdows"));

        var cases = SqlProofArtifacts.ReadAll(_dir);

        Assert.Equal(2, cases.Count);
        var pinned = Assert.Single(cases, c => c.Job == "Pinned");
        Assert.Equal("Pengdows", pinned.Framework);
        Assert.Equal(40, Assert.Single(pinned.Statements).Calls);
    }

    [Fact]
    public void Write_SameCaseTwice_KeepsTheLatest()
    {
        SqlProofArtifacts.Write(_dir, Case("Baseline", "Dapper"));
        SqlProofArtifacts.Write(_dir, Case("Baseline", "Dapper") with { Operations = 60 });

        Assert.Equal(60, Assert.Single(SqlProofArtifacts.ReadAll(_dir)).Operations);
    }

    [Fact]
    public void Clear_RemovesEveryFragment_AndReadAllOfAMissingDirectoryIsEmpty()
    {
        SqlProofArtifacts.Write(_dir, Case("Baseline", "Dapper"));

        SqlProofArtifacts.Clear(_dir);

        Assert.Empty(SqlProofArtifacts.ReadAll(_dir));
        Assert.Empty(SqlProofArtifacts.ReadAll(Path.Combine(_dir, "missing")));
    }
}
