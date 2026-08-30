using System;
using System.IO;
using Xunit;

namespace pengdows.crud.Tests;

public class PackageReadmeTests
{
    [Fact]
    public void PackageReadmeBuildBadgeTargetsExistingWorkflow()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string packageReadmePath = Path.Combine(repoRoot, "pengdows.crud", "README.md");
        string workflowPath = Path.Combine(repoRoot, ".github", "workflows", "deploy.yml");

        string readme = File.ReadAllText(packageReadmePath);

        Assert.True(File.Exists(workflowPath), $"Expected workflow file to exist at '{workflowPath}'.");
        Assert.Contains("actions/workflows/deploy.yml/badge.svg", readme);
        Assert.DoesNotContain("actions/workflows/build.yml/badge.svg", readme);
    }

    [Fact]
    public void PlainSqlReaderExamplesWrapAllIdentifiers()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string[] documentationPaths =
        {
            Path.Combine(repoRoot, "README.md"),
            Path.Combine(repoRoot, "llms-full.txt")
        };

        foreach (string documentationPath in documentationPaths)
        {
            string documentation = File.ReadAllText(documentationPath);

            Assert.Contains("""select.WrapObjectName("order_number")""", documentation);
            Assert.Contains("""select.WrapObjectName("orders")""", documentation);
            Assert.Contains("""select.WrapObjectName("customer_id")""", documentation);
            Assert.DoesNotContain("SELECT order_number FROM orders WHERE customer_id = ", documentation);
        }
    }
}
