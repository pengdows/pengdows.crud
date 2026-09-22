namespace pengdows.stormgate.Tests;

public sealed class ReadmeTests
{
    [Fact]
    public void Readme_IncludesEntityFrameworkCoreExampleWithCallerOwnedGatedConnection()
    {
        var readmePath = Path.Combine(GetRepositoryRoot(), "pengdows.stormgate", "README.md");
        var readme = File.ReadAllText(readmePath);

        Assert.Contains("## Entity Framework Core", readme, StringComparison.Ordinal);
        Assert.Contains("await gate.OpenAsync", readme, StringComparison.Ordinal);
        Assert.Contains("UseSqlServer(connection, contextOwnsConnection: false)", readme, StringComparison.Ordinal);
        Assert.Contains("contextOwnsConnection: false", readme, StringComparison.Ordinal);
    }

    private static string GetRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "pengdows.crud.sln")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
