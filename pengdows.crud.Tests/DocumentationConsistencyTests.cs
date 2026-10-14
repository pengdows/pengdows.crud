using System;
using System.IO;
using System.Linq;
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

    // A release cannot ship without saying what is in it: CHANGELOG.md has one "## [x.y.z]" heading per published
    // 2.0.x version and one for the version Directory.Build.props is about to release.
    [Fact]
    public void Changelog_HasAnEntryForEveryReleaseIncludingTheCurrentVersion()
    {
        var root = FindRepositoryRoot();
        var changelog = File.ReadAllText(Path.Combine(root, "CHANGELOG.md"));
        var props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));
        var current = Regex.Match(props, @"<VersionPrefix>([^<]+)</VersionPrefix>").Groups[1].Value;

        var expected = new[] { "2.0.0", "2.0.1", "2.0.2", "2.0.3", "2.0.4", "2.0.5", current };
        foreach (var version in expected)
        {
            Assert.Matches($@"(?m)^## \[{Regex.Escape(version)}\]", changelog);
        }
    }

    [Fact]
    public void Changelog_ListsNewestVersionFirst()
    {
        var root = FindRepositoryRoot();
        var changelog = File.ReadAllText(Path.Combine(root, "CHANGELOG.md"));
        var versions = Regex.Matches(changelog, @"(?m)^## \[(\d+\.\d+\.\d+)\]")
            .Cast<Match>()
            .Select(m => Version.Parse(m.Groups[1].Value))
            .ToList();

        Assert.Equal(versions.OrderByDescending(v => v), versions);
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("llms.txt")]
    public void Changelog_IsLinkedFrom(string file)
    {
        var root = FindRepositoryRoot();
        Assert.Contains("CHANGELOG.md", File.ReadAllText(Path.Combine(root, file)));
    }

    // 2.0.6 is the first 2.0 release that adds databases. The README, llms.txt and the changelog each say which ones are
    // new (a SupportedDatabase value of their own) and which compatible forks were verified with no code change.
    private static readonly (SupportedDatabase Value, string Name)[] DatabasesNewIn206 =
    {
        (SupportedDatabase.Db2, "Db2"),
        (SupportedDatabase.FlatFile, "FlatFile"),
        (SupportedDatabase.SingleStore, "SingleStore"),
        (SupportedDatabase.SybaseASE, "Sybase"),
        (SupportedDatabase.Spanner, "Spanner"),
        (SupportedDatabase.Informix, "Informix"),
        (SupportedDatabase.SapHana, "SAP HANA"),
        (SupportedDatabase.InterBase, "InterBase"),
        (SupportedDatabase.Access, "Access")
    };

    private static readonly string[] ForksVerifiedWithNoCodeChange =
    {
        "Percona Server for MySQL", "Citus", "TimescaleDB", "Fujitsu Enterprise Postgres"
    };

    [Theory]
    [InlineData("README.md")]
    [InlineData("llms.txt")]
    [InlineData("CHANGELOG.md")]
    public void NewDatabasesAndVerifiedForks_AreNamedIn(string file)
    {
        var contents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), file));

        foreach (var (value, name) in DatabasesNewIn206)
        {
            Assert.True(Enum.IsDefined(value), $"{value} is no longer a SupportedDatabase value");
            Assert.Contains(name, contents);
        }

        foreach (var fork in ForksVerifiedWithNoCodeChange)
        {
            Assert.Contains(fork, contents);
        }

        Assert.Matches(@"(?i)new in 2\.0\.6", contents);
        Assert.Matches(@"(?i)no code change", contents);
    }

    // The four forks are supported products new in 2.0.6, shown as subsidiaries of the type they are detected as:
    // each is listed under its parent (a line naming both) in the README's database list and its 2.0.6 section.
    [Theory]
    [InlineData("Percona Server for MySQL", "MySQL")]
    [InlineData("Citus", "PostgreSQL")]
    [InlineData("TimescaleDB", "PostgreSQL")]
    [InlineData("Fujitsu Enterprise Postgres", "PostgreSQL")]
    public void Readme_ListsEachVerifiedForkUnderItsParent(string fork, string parent)
    {
        var readme = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "README.md"));
        var lines = readme.Split('\n').Where(l => l.Contains(fork)).ToList();

        Assert.Contains(lines, l => l.TrimStart().StartsWith("- ") && l.Contains($"detected as {parent}"));
        Assert.Contains(lines, l => l.TrimStart().StartsWith("| ") && l.Contains(parent));
    }

    // The same list is restated where a reader meets the library first: the NuGet package page and the three skill
    // trees. Each must name every SupportedDatabase value's product, so none stays at the 2.0.5 fourteen.
    [Theory]
    [InlineData("pengdows.crud/README.md")]
    [InlineData("skills/claude/pengdows-crud/SKILL.md")]
    [InlineData("skills/codex/pengdows-crud/SKILL.md")]
    [InlineData("skills/gemini/pengdows-crud/SKILL.md")]
    public void EntryPointDocs_NameEveryDatabaseNewIn206(string file)
    {
        var contents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), file.Replace('/', Path.DirectorySeparatorChar)));

        foreach (var (_, name) in DatabasesNewIn206)
        {
            Assert.Contains(name, contents);
        }
    }

    // The integration project README says which databases a default run covers; it named the original eleven after
    // the always-on set had grown. Every always-on database must be named there (the opt-in ones have their own list).
    [Fact]
    public void IntegrationTestsReadme_NamesEveryAlwaysOnDatabase()
    {
        var readme = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "pengdows.crud.IntegrationTests", "README.md"));

        foreach (var name in new[] { "SQLite", "PostgreSQL", "SQL Server", "MySQL", "MariaDB", "Firebird", "CockroachDB",
                     "DuckDB", "Oracle", "YugabyteDB", "TiDB", "FlatFile", "Db2", "SingleStore", "Sybase", "Spanner",
                     "Informix", "SAP HANA", "InterBase", "Access", "Aurora" })
        {
            Assert.Contains(name, readme);
        }
    }

    // A table headed by a database name claims to cover every database. These three stopped at the original fourteen
    // (or fewer) until 2.0.6 was audited by hand, so each must name every SupportedDatabase value's product.
    [Theory]
    [InlineData("Default Pool Sizes")]
    [InlineData("Read-Only Enforcement Matrix")]
    [InlineData("Minimum Server Versions")]
    public void SupportedDatabasesTables_NameEveryDatabase(string heading)
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "supported-databases.md"));
        var start = doc.IndexOf(heading, StringComparison.OrdinalIgnoreCase);
        Assert.True(start >= 0, $"no section containing '{heading}' in docs/supported-databases.md");
        var next = doc.IndexOf("\n## ", start, StringComparison.Ordinal);
        var section = next < 0 ? doc[start..] : doc[start..next];

        // Product name or SupportedDatabase value name: a table may use either.
        var names = new[]
        {
            ("PostgreSQL", "PostgreSql"), ("SQL Server", "SqlServer"), ("Oracle", "Oracle"), ("Firebird", "Firebird"),
            ("CockroachDB", "CockroachDb"), ("MariaDB", "MariaDb"), ("MySQL", "MySql"), ("SQLite", "Sqlite"),
            ("DuckDB", "DuckDb"), ("YugabyteDB", "YugabyteDb"), ("TiDB", "TiDb"), ("Snowflake", "Snowflake"),
            ("Aurora", "Aurora"), ("Db2", "Db2"), ("FlatFile", "FlatFile"), ("SingleStore", "SingleStore"),
            ("Sybase", "SybaseASE"), ("Spanner", "Spanner"), ("Informix", "Informix"), ("HANA", "SapHana"),
            ("InterBase", "InterBase"), ("Access", "Access")
        };
        foreach (var (product, value) in names)
        {
            Assert.True(
                section.Contains(product, StringComparison.OrdinalIgnoreCase) ||
                section.Contains(value, StringComparison.OrdinalIgnoreCase),
                $"'{heading}' in docs/supported-databases.md does not mention {product}");
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
