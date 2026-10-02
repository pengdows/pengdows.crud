using System.Text.Json;

namespace CrudBenchmarks;

/// <summary>
/// Stores each benchmark case's <see cref="SqlProofCase"/> as a fragment file. BenchmarkDotNet runs
/// every case in its own child process, so the parent merges the fragments after the run
/// (Program writes <c>sqlproof-report.md</c>).
/// </summary>
internal static class SqlProofArtifacts
{
    private const string Extension = ".sqlproof.json";

    /// <summary>Where the fragments live: under the run's results directory (see Program).</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetEnvironmentVariable("CRUD_BENCH_ARTIFACTS_DIR") ?? Directory.GetCurrentDirectory(),
            "sqlproof");

    public static void Write(string directory, SqlProofCase proofCase)
    {
        Directory.CreateDirectory(directory);
        var name = string.Join("__", proofCase.Job, proofCase.Workload, proofCase.Framework);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        File.WriteAllText(Path.Combine(directory, name + Extension), JsonSerializer.Serialize(proofCase));
    }

    public static IReadOnlyList<SqlProofCase> ReadAll(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return Array.Empty<SqlProofCase>();
        }

        return Directory.GetFiles(directory, "*" + Extension)
            .Order(StringComparer.Ordinal)
            .Select(file => JsonSerializer.Deserialize<SqlProofCase>(File.ReadAllText(file))!)
            .ToList();
    }

    public static void Clear(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(directory, "*" + Extension))
        {
            File.Delete(file);
        }
    }
}
