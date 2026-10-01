using System;
using System.IO;
using Xunit;

namespace pengdows.crud.Tests;

public class TestProjectHygieneTests
{
    [Fact]
    public void TestProjectDoesNotDeclareMocksFolder()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string testProjectPath = Path.Combine(repoRoot, "pengdows.crud.Tests", "pengdows.crud.Tests.csproj");

        string projectXml = File.ReadAllText(testProjectPath);

        Assert.DoesNotContain("<Folder Include=\"Mocks\\\" />", projectXml);
        Assert.DoesNotContain("<Folder Include=\"Mocks\\\"/>", projectXml);
    }
}
