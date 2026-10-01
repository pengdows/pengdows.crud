using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;
using pengdows.crud.enums;

namespace pengdows.crud.Tests;

public sealed class DocumentationConsistencyTests
{
    [Fact]
    public void LlmsIndex_RelativeLinksResolve()
    {
        var root = FindRepositoryRoot();
        var indexPath = Path.Combine(root, "llms.txt");
        var index = File.ReadAllText(indexPath);

        foreach (Match match in Regex.Matches(index, @"\]\(([^)#]+)(?:#[^)]+)?\)"))
        {
            var link = match.Groups[1].Value;
            if (Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                continue;
            }

            Assert.True(File.Exists(Path.Combine(root, link.Replace('/', Path.DirectorySeparatorChar))),
                $"llms.txt link does not resolve: {link}");
        }
    }

    // Was DoNotAdvertiseAsyncTenantRegistryApis while 2.0.6 lacked them; BP-311 added them, so the
    // multitenancy docs must now document exactly the async members the interface has.
    [Fact]
    public void MultitenancyDocs_DocumentTheAsyncTenantRegistryApisThatExist()
    {
        var registry = typeof(pengdows.crud.tenant.ITenantContextRegistry);
        Assert.NotNull(registry.GetMethod("GetContextAsync"));
        Assert.NotNull(registry.GetMethod("AcquireLeaseAsync"));

        var root = FindRepositoryRoot();
        foreach (var path in new[]
                 {
                     Path.Combine(root, "docs", "connection", "multitenancy.md"),
                     Path.Combine(root, "docs", "connection", "multitenancy-architecture.md")
                 })
        {
            var contents = File.ReadAllText(path);
            Assert.Contains("GetContextAsync", contents);
            Assert.Contains("AcquireLeaseAsync", contents);
        }
    }

    [Fact]
    public void SupportedDatabaseDocs_ListEvery2PointZeroSixEnumValue()
    {
        var root = FindRepositoryRoot();
        var contents = File.ReadAllText(Path.Combine(root, "docs", "supported-databases.md"));

        foreach (var product in Enum.GetValues<SupportedDatabase>())
        {
            if (product == SupportedDatabase.Unknown)
            {
                continue;
            }

            Assert.Contains($"`{product}=", contents);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "pengdows.crud.sln")) &&
                File.Exists(Path.Combine(directory.FullName, "llms.txt")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
