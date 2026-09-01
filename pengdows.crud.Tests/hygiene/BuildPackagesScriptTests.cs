using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace pengdows.crud.Tests;

public class BuildPackagesScriptTests
{
    [Fact]
    public void BuildScript_ListsAllRequiredPackages()
    {
        var scriptPath = GetScriptPath();
        var contents = File.ReadAllText(scriptPath);

        Assert.Contains("dotnet pack", contents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pengdows.crud.abstractions/pengdows.crud.abstractions.csproj", contents,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pengdows.crud/pengdows.crud.csproj", contents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pengdows.crud.fakeDb/pengdows.crud.fakeDb.csproj", contents,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pengdows.stormgate/pengdows.stormgate.csproj", contents,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildScript_DoesNotIncludeIntegrationOrTestbedProjects()
    {
        var scriptPath = GetScriptPath();
        var contents = File.ReadAllText(scriptPath);

        Assert.DoesNotContain("pengdows.crud.IntegrationTests", contents, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("testbed", contents, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeployWorkflow_PublishesEntityFrameworkCoreStormGatePackage()
    {
        var root = GetRepoRoot();
        var workflowPath = Path.Combine(root, ".github", "workflows", "deploy.yml");
        var contents = File.ReadAllText(workflowPath);

        Assert.Contains("pengdows.stormgate.EntityFrameworkCore", contents, StringComparison.Ordinal);
        Assert.Contains("pengdows.stormgate.entityframeworkcore", contents, StringComparison.Ordinal);
    }

    [Fact]
    public void TestbedDatabaseImages_AreExplicitlyPinned()
    {
        var root = GetRepoRoot();
        var expected = new Dictionary<string, string>
        {
            ["testbed/Db2/Db2TestContainer.cs"] = "ibmcom/db2:11.5.8.0",
            ["testbed/MySQL/MySqlTestContainer.cs"] = "mysql:8.4.11",
            ["testbed/mariaDb/MariaDbContainer.cs"] = "mariadb:11.4.12",
            ["testbed/PostgreSQL/PostgreSqlTestContainer.cs"] = "postgres:16.4",
            ["testbed/TiDB/TiDBTestContainer.cs"] = "pingcap/tidb:v8.5.7",
            ["testbed/Yugabyte/YugabyteTestContainer.cs"] = "yugabytedb/yugabyte:2025.2.5.2-b5",
            ["testbed/Firebird/FirebirdSqlTestContainer.cs"] = "firebirdsql/firebird:3.0.9",
            ["testbed/SqlServer/SqlServerTestContainer.cs"] = "mcr.microsoft.com/mssql/server:2022-CU25-GDR2-ubuntu-22.04",
            ["testbed/Oracle/OracleTestContainer.cs"] = "gvenzl/oracle-free:23.26.2-slim-faststart"
        };

        foreach (var (relativePath, image) in expected)
        {
            var contents = File.ReadAllText(Path.Combine(root, relativePath));
            Assert.Contains(image, contents, StringComparison.Ordinal);
        }
    }

    // =========================================================================
    // Version consistency — Directory.Build.props must declare the release version.
    // =========================================================================

    [Fact]
    public void DirectoryBuildProps_Version_Is_2_0_6()
    {
        var root = GetRepoRoot();
        var propsPath = Path.Combine(root, "Directory.Build.props");
        Assert.True(File.Exists(propsPath), $"Directory.Build.props not found at {propsPath}");

        var contents = File.ReadAllText(propsPath);
        Assert.Contains("<VersionPrefix>2.0.6</VersionPrefix>", contents, StringComparison.Ordinal);
        Assert.Contains("<AssemblyVersion>$(VersionPrefix).0</AssemblyVersion>", contents, StringComparison.Ordinal);
        Assert.Contains("<FileVersion>$(VersionPrefix).0</FileVersion>", contents, StringComparison.Ordinal);
    }

    private static string GetScriptPath()
    {
        var root = GetRepoRoot();
        return Path.Combine(root, "build-packages.sh");
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

        throw new DirectoryNotFoundException("Could not locate repository root for build script validation.");
    }
}
