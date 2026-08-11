using System;
using System.IO;
using Xunit;

namespace pengdows.crud.Tests;

public class WorkflowCoverageRatchetTests
{
    [Fact]
    public void DeployWorkflow_CoverageRatchet_UsesDedicatedVariableWriteToken()
    {
        string workflowPath = Path.Combine(GetRepoRoot(), ".github", "workflows", "deploy.yml");
        string workflow = File.ReadAllText(workflowPath);

        Assert.Contains("- name: Ratchet coverage baselines", workflow, StringComparison.Ordinal);
        Assert.Contains("GH_TOKEN: ${{ secrets.ACTIONS_VARIABLES_TOKEN }}", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}", workflow, StringComparison.Ordinal);
    }

    private static string GetRepoRoot()
    {
        var start = new DirectoryInfo(AppContext.BaseDirectory);
        for (var current = start; current != null; current = current.Parent)
        {
            var slnPath = Path.Combine(current.FullName, "pengdows.crud.sln");
            if (File.Exists(slnPath))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate repository root for workflow validation.");
    }
}
