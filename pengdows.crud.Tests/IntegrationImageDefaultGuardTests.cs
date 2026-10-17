using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace pengdows.crud.Tests;

// Reads the testbed SOURCE (no database, no container). The all-types integration matrix runs on one image per
// database: the container's default. The multi-version list (TestbedImageMatrix) feeds only the testbed's lighter
// per-version checks. The default used to be a second hard-coded copy of the tag in each container, so nothing kept
// "the version the all-types matrix runs on" the newest entry of the list. Each container now takes its default from
// TestbedImageMatrix.Latest(...), the first (newest) entry, so the two cannot drift.
public sealed class IntegrationImageDefaultGuardTests
{
    // Matrix key -> the container class that starts that database's default image.
    private static readonly (string Provider, string File)[] Containers =
    {
        ("PostgreSQL", "testbed/PostgreSQL/PostgreSqlTestContainer.cs"),
        ("MySQL", "testbed/MySQL/MySqlTestContainer.cs"),
        ("MariaDB", "testbed/mariaDb/MariaDbContainer.cs"),
        ("SQL Server", "testbed/SqlServer/SqlServerTestContainer.cs"),
        ("CockroachDB", "testbed/Cockroach/CockroachDbTestContainer.cs"),
        ("Firebird", "testbed/Firebird/FirebirdSqlTestContainer.cs"),
        ("TiDB", "testbed/TiDB/TiDBTestContainer.cs"),
        ("YugabyteDB", "testbed/Yugabyte/YugabyteTestContainer.cs"),
        ("Oracle", "testbed/Oracle/OracleTestContainer.cs"),
        ("Db2", "testbed/Db2/Db2TestContainer.cs")
    };

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "pengdows.crud.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    [Fact]
    public void EveryContainerDefault_ComesFromTheMatrix_NotAHardCodedTag()
    {
        var offenders = new List<string>();
        foreach (var (provider, file) in Containers)
        {
            var text = File.ReadAllText(Path.Combine(Root(), file));
            if (!text.Contains($"TestbedImageMatrix.Latest(\"{provider}\")", StringComparison.Ordinal))
            {
                offenders.Add($"{file}: default image is not TestbedImageMatrix.Latest(\"{provider}\")");
            }

            // A literal image tag as the default (`image ?? "repo:tag"`, `image = "repo:tag"`).
            if (Regex.IsMatch(text, "(\\?\\?|image\\s*=)\\s*\"[A-Za-z0-9./_-]+:[A-Za-z0-9._-]+\""))
            {
                offenders.Add($"{file}: still hard-codes a default image tag");
            }
        }

        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }

    [Fact]
    public void EveryMatrixProvider_HasAContainerThisGuardCovers()
    {
        var matrix = File.ReadAllText(Path.Combine(Root(), "testbed/ParallelTestOrchestrator.cs"));
        var keys = Regex.Matches(matrix, "\\[\"([^\"]+)\"\\]\\s*=\\s*\\[")
            .Select(m => m.Groups[1].Value)
            .ToArray();

        Assert.NotEmpty(keys);
        var covered = Containers.Select(c => c.Provider).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var uncovered = keys.Where(k => !covered.Contains(k)).ToArray();
        Assert.True(uncovered.Length == 0, "Matrix providers without a guarded container: " + string.Join(", ", uncovered));
    }
}
