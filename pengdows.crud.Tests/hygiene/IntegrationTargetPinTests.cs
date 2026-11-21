using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Pins which integration test methods run only for particular databases. The integration runner starts one process per
/// database and excludes, by name, every method pinned to a database other than that one. Without the exclusion such a method
/// runs in every other process, skips itself there, and that skip is a configuration skip, which REVIEW_POLICY.md (Ring 4)
/// does not allow. Only a capability skip (a database that lacks the feature) may remain, and it must be attributed
/// [SKIP:&lt;Product&gt;].
///
/// A method is pinned to a database when its body calls <c>Skip.If</c>/<c>Skip.IfNot</c> naming that database, runs it through
/// <c>RunTestAgainstProviderAsync</c>, or its class overrides <c>GetSupportedProviders</c> to that database alone. The manifest
/// <c>pengdows.crud.IntegrationTests/integration-targets.txt</c> is generated from the test source; regenerate it with
/// <c>PIN_INTEGRATION_TARGETS=1</c>. The default run fails when the manifest is stale.
/// </summary>
public class IntegrationTargetPinTests
{
    // Top-level classes only (no indentation): a nested class is test data inside its outer class, whose provider list and
    // tests it shares, so its text is part of the outer class.
    private static readonly Regex ClassDecl = new(
        @"(?m)^(?:public|internal)\s+(?:(?:sealed|static|partial|abstract)\s+)*class\s+(\w+)", RegexOptions.Compiled);

    private static readonly Regex NamespaceDecl = new(@"(?m)^namespace\s+([\w.]+)\s*;", RegexOptions.Compiled);

    private static readonly Regex TestAttribute = new(
        @"\[(?:SkippableFact|Fact|SkippableTheory|Theory)\b[^\]]*\]", RegexOptions.Compiled);

    private static readonly Regex MethodHeader = new(
        @"\b(?:Task|ValueTask|void)(?:<[^>]*>)?\s+(\w+)\s*\(", RegexOptions.Compiled);

    private static readonly Regex SkipStatement = new(@"Skip\.If(?:Not)?\s*\(", RegexOptions.Compiled);

    private static readonly Regex RunAgainst = new(@"RunTestAgainst(?:Provider|Providers)Async\(", RegexOptions.Compiled);

    private static readonly Regex EnsureTargeted = new(@"EnsureTargetedProviderAvailable\(", RegexOptions.Compiled);

    private static readonly Regex HelperCall = new(@"\b((?:SkipUnless|SkipIf|Require)\w*)\s*\(", RegexOptions.Compiled);

    private static readonly Regex DatabaseToken = new(@"SupportedDatabase\.(\w+)", RegexOptions.Compiled);

    private static readonly Regex LiteralFirstArgumentCall = new(
        @"\b(\w+)\s*\(\s*SupportedDatabase\.(\w+)", RegexOptions.Compiled);

    internal sealed record TestMethod(string FullName, string[] Databases);

    internal static string IntegrationRoot()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return Path.Combine(root, "pengdows.crud.IntegrationTests");
    }

    internal static string ManifestPath() => Path.Combine(IntegrationRoot(), "integration-targets.txt");

    internal static IEnumerable<string> TestSources() =>
        Directory.EnumerateFiles(IntegrationRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    // The text from a position up to the matching close brace of the first '{' found (or to ';' for an expression body).
    private static string BodyAfter(string text, int from)
    {
        var brace = text.IndexOf('{', from);
        var arrow = text.IndexOf("=>", from, StringComparison.Ordinal);
        if (arrow >= 0 && (brace < 0 || arrow < brace))
        {
            var semi = text.IndexOf(';', arrow);
            return semi < 0 ? text.Substring(arrow) : text.Substring(arrow, semi - arrow);
        }

        if (brace < 0)
        {
            return string.Empty;
        }

        var depth = 0;
        for (var i = brace; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}' && --depth == 0)
            {
                return text.Substring(brace, i - brace + 1);
            }
        }

        return text.Substring(brace);
    }

    private static readonly Regex FamilyToken = new(@"DatabaseFamilies\.(\w+)", RegexOptions.Compiled);

    private static readonly Regex StaticProviderField = new(
        @"static\s+readonly\s+SupportedDatabase\[\]\s+(\w+)\s*=\s*([^;]*);", RegexOptions.Compiled);

    private static readonly Regex Identifier = new(@"\b[A-Za-z_]\w*\b", RegexOptions.Compiled);

    // The Except(...) that directly follows a DatabaseFamilies member (matched at the member's own position).
    private static readonly Regex FamilyExcept = new(
        @"DatabaseFamilies\.\w+\s*\.Except\(\s*new\s*\[\]\s*\{([^}]*)\}", RegexOptions.Compiled);

    // The databases a provider expression names: literal SupportedDatabase members, DatabaseFamilies members (expanded from
    // their initializer in DatabaseFamilies.cs), and static provider arrays declared in the same class (expanded the same way).
    internal static IEnumerable<string> ProvidersIn(string expression, string classText)
    {
        // Members named inside a family's Except(...) are exclusions, not targets.
        var exceptSpans = FamilyExcept.Matches(expression)
            .Select(m => (Start: m.Groups[1].Index, End: m.Groups[1].Index + m.Groups[1].Length))
            .ToList();

        foreach (Match token in DatabaseToken.Matches(expression))
        {
            if (exceptSpans.Any(span => token.Index >= span.Start && token.Index < span.End))
            {
                continue;
            }

            // "is not X" and "!= X" name what the provider list leaves out, and Unknown is never a database a run selects.
            var before = expression.Substring(0, token.Index).TrimEnd();
            if (before.EndsWith("not", StringComparison.Ordinal) || before.EndsWith("!=", StringComparison.Ordinal) ||
                token.Groups[1].Value == "Unknown")
            {
                continue;
            }

            yield return token.Groups[1].Value;
        }

        foreach (Match family in FamilyToken.Matches(expression))
        {
            // DatabaseFamilies.X.Except(new[] { ... }) is the family minus the named members (a stated capability exclusion).
            var except = FamilyExcept.Match(expression, family.Index);
            var excluded = except.Success && except.Index == family.Index
                ? DatabaseToken.Matches(except.Groups[1].Value).Select(t => t.Groups[1].Value).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in FamilyMembers(family.Groups[1].Value).Where(m => !excluded.Contains(m)))
            {
                yield return member;
            }
        }

        foreach (Match field in StaticProviderField.Matches(classText))
        {
            var name = field.Groups[1].Value;
            if (!Regex.IsMatch(expression, @"\b" + Regex.Escape(name) + @"\b"))
            {
                continue;
            }

            foreach (var member in ProvidersIn(field.Groups[2].Value, string.Empty))
            {
                yield return member;
            }
        }
    }

    private static IEnumerable<string> FamilyMembers(string family)
    {
        var source = File.ReadAllText(Path.Combine(IntegrationRoot(), "Infrastructure", "DatabaseFamilies.cs"));
        var match = Regex.Match(source,
            @"public\s+static\s+readonly\s+SupportedDatabase\[\]\s+" + Regex.Escape(family) + @"\s*=\s*([^;]*);");
        return match.Success ? DatabaseToken.Matches(match.Groups[1].Value).Select(t => t.Groups[1].Value) : Array.Empty<string>();
    }

    // The first argument of a call, up to the top-level comma or closing parenthesis.
    private static string FirstArgument(string body, int openParen)
    {
        var depth = 0;
        for (var i = openParen; i < body.Length; i++)
        {
            var c = body[i];
            if (c == '(' || c == '[' || c == '{')
            {
                depth++;
            }
            else if (c == ')' || c == ']' || c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return body.Substring(openParen + 1, i - openParen - 1);
                }
            }
            else if (c == ',' && depth == 1)
            {
                return body.Substring(openParen + 1, i - openParen - 1);
            }
        }

        return string.Empty;
    }

    // Databases a method body targets by configuration: the database named in a Skip.If(Not) statement, or a provider-scoped run.
    private static IEnumerable<string> TargetsIn(string body, string classText)
    {
        foreach (Match skip in SkipStatement.Matches(body))
        {
            var end = body.IndexOf(';', skip.Index);
            var statement = end < 0 ? body.Substring(skip.Index) : body.Substring(skip.Index, end - skip.Index);
            foreach (var target in ProvidersIn(statement, classText))
            {
                yield return target;
            }

            // Access has no container and runs only on Windows, so a Windows-only check is its configuration.
            if (statement.Contains("OperatingSystem.IsWindows()", StringComparison.Ordinal))
            {
                yield return "Access";
            }
        }

        foreach (Match run in RunAgainst.Matches(body))
        {
            foreach (var target in ProvidersIn(FirstArgument(body, run.Index + run.Length - 1), classText))
            {
                yield return target;
            }
        }

        foreach (Match ensure in EnsureTargeted.Matches(body))
        {
            foreach (var target in ProvidersIn(FirstArgument(body, ensure.Index + ensure.Length - 1), classText))
            {
                yield return target;
            }
        }
    }

    // A test that reads which databases this run selects (INTEGRATION_ONLY or TESTBED_ONLY) is the whole matrix, not a test of
    // one database.
    private static bool IntegrationOnlySelected(string body) =>
        body.Contains("INTEGRATION_ONLY", StringComparison.Ordinal) || body.Contains("TESTBED_ONLY", StringComparison.Ordinal);

    // Skips that each name a database and together name two or more of them need all of them in one process: the combined
    // target ("A+B") pins the method to that combination. Null when the body has fewer than two such skips.
    private static string? ConjunctiveTargetIn(string body, string classText)
    {
        var groups = new List<string[]>();
        foreach (Match skip in SkipStatement.Matches(body))
        {
            var end = body.IndexOf(';', skip.Index);
            var statement = end < 0 ? body.Substring(skip.Index) : body.Substring(skip.Index, end - skip.Index);
            var named = ProvidersIn(statement, classText).Distinct(StringComparer.Ordinal).ToArray();
            if (named.Length > 0)
            {
                groups.Add(named);
            }
        }

        var all = groups.SelectMany(g => g).Distinct(StringComparer.Ordinal).OrderBy(d => d, StringComparer.Ordinal).ToArray();
        return groups.Count >= 2 && all.Length >= 2 ? string.Join("+", all) : null;
    }

    // A call to a method declared in the same class whose first argument is a literal database runs that helper for that
    // database alone (the helper's own run uses the argument), so the caller is pinned to it.
    private static IEnumerable<string> LiteralHelperTargetsIn(string body, string classText)
    {
        foreach (Match call in LiteralFirstArgumentCall.Matches(body))
        {
            var name = call.Groups[1].Value;
            var declared = Regex.IsMatch(classText,
                @"\b(?:private|internal|public|protected)\s+(?:static\s+)?(?:async\s+)?[\w<>\[\],. ]+\s+" + Regex.Escape(name) + @"\s*\(");
            if (declared)
            {
                yield return call.Groups[2].Value;
            }
        }
    }

    // Targets of same-class helpers a body calls (for example SkipUnlessFirebirdSelected), one level deep.
    private static IEnumerable<string> HelperTargetsIn(string body, string classText)
    {
        foreach (Match call in HelperCall.Matches(body))
        {
            var name = call.Groups[1].Value;
            var header = Regex.Match(classText, @"\b" + Regex.Escape(name) + @"\s*\(\s*\)\s*(?=\{|=>)");
            if (!header.Success)
            {
                continue;
            }

            foreach (var target in TargetsIn(BodyAfter(classText, header.Index + header.Length), classText))
            {
                yield return target;
            }
        }
    }

    internal static IReadOnlyList<TestMethod> AllTests()
    {
        var tests = new List<TestMethod>();
        foreach (var file in TestSources())
        {
            var text = File.ReadAllText(file);
            var ns = NamespaceDecl.Match(text);
            if (!ns.Success)
            {
                continue;
            }

            var classes = ClassDecl.Matches(text).Cast<Match>().ToList();
            for (var c = 0; c < classes.Count; c++)
            {
                var classStart = classes[c].Index;
                var classEnd = c + 1 < classes.Count ? classes[c + 1].Index : text.Length;
                var classText = text.Substring(classStart, classEnd - classStart);
                const string overrideHeader = "override IEnumerable<SupportedDatabase> GetSupportedProviders()";
                var overrideAt = classText.IndexOf(overrideHeader, StringComparison.Ordinal);
                var classDatabases = overrideAt < 0
                    ? Array.Empty<string>()
                    : ProvidersIn(BodyAfter(classText, overrideAt + overrideHeader.Length), classText).ToArray();

                foreach (Match attribute in TestAttribute.Matches(classText))
                {
                    var header = MethodHeader.Match(classText, attribute.Index + attribute.Length);
                    if (!header.Success)
                    {
                        continue;
                    }

                    var body = BodyAfter(classText, header.Index + header.Length);
                    var conjunctive = ConjunctiveTargetIn(body, classText);
                    var ownTargets = TargetsIn(body, classText).Concat(HelperTargetsIn(body, classText))
                        .Concat(LiteralHelperTargetsIn(body, classText))
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(d => d, StringComparer.Ordinal)
                        .ToArray();
                    // A method that names its own database runs for that database alone; the class list only covers a
                    // method that names none (it runs through RunTestAgainstAllProviders).
                    var databases = conjunctive != null
                        ? new[] { conjunctive }
                        : ownTargets.Length > 0 ? ownTargets : classDatabases.Distinct(StringComparer.Ordinal).OrderBy(d => d, StringComparer.Ordinal).ToArray();
                    if (conjunctive == null && ownTargets.Length == 0 && IntegrationOnlySelected(body))
                    {
                        // The whole matrix: it reads which databases this run selects. The testbed step covers it.
                        databases = new[] { "Matrix" };
                    }
                    var fullName = $"{ns.Groups[1].Value}.{classes[c].Groups[1].Value}.{header.Groups[1].Value}";
                    tests.Add(new TestMethod(fullName, databases));
                }
            }
        }

        return tests;
    }

    internal static string Render(IEnumerable<TestMethod> tests) =>
        string.Concat(tests.Where(t => t.Databases.Length > 0)
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .Select(t => $"{t.FullName}\t{string.Join(",", t.Databases)}\n"));

    [Fact]
    public void Manifest_MatchesTheTestSource()
    {
        var expected = Render(AllTests());
        if (Environment.GetEnvironmentVariable("PIN_INTEGRATION_TARGETS") == "1")
        {
            File.WriteAllText(ManifestPath(), expected);
            return;
        }

        Assert.True(File.Exists(ManifestPath()),
            "integration-targets.txt is missing; run with PIN_INTEGRATION_TARGETS=1 to generate it.");
        Assert.Equal(expected.Replace("\r\n", "\n"), File.ReadAllText(ManifestPath()).Replace("\r\n", "\n"));
    }

    // The runner excludes a pinned method with the term FullyQualifiedName!=Name&FullyQualifiedName!~Name( : the fact itself,
    // and every row of a theory (whose names carry their arguments after "("). A test with another name is never excluded,
    // so a name that is merely a prefix of another test's name is safe; this test pins that.
    [Fact]
    public void PinnedNames_ExcludeOnlyThemselvesAndTheirTheoryRows()
    {
        var tests = AllTests();
        foreach (var pinned in tests.Where(t => t.Databases.Length > 0))
        {
            var collisions = tests
                .Where(other => other.FullName != pinned.FullName && other.FullName.Contains(pinned.FullName + "(", StringComparison.Ordinal))
                .Select(other => other.FullName)
                .ToList();
            Assert.True(collisions.Count == 0, $"{pinned.FullName} would also exclude: {string.Join(", ", collisions)}");
        }
    }

    // A negated member is every database except it, so naming it is not a target. The DateOnly/TimeOnly fixture
    // filters Unknown out of "all databases"; pinning to Unknown would stop those tests running in any process.
    [Fact]
    public void ProvidersIn_NegatedMember_IsNotATarget()
    {
        var targets = ProvidersIn("Enum.GetValues<SupportedDatabase>().Where(p => p is not SupportedDatabase.Unknown)", string.Empty).ToArray();

        Assert.Empty(targets);
    }

    [Fact]
    public void ProvidersIn_LiteralMember_IsATarget()
    {
        var targets = ProvidersIn("IntegrationTestConfiguration.EnabledProviders.Where(p => p == SupportedDatabase.Oracle)", string.Empty).ToArray();

        Assert.Equal(new[] { "Oracle" }, targets);
    }

    // A test whose skips name two databases needs both enabled in one process. Such a test is pinned to the combination
    // ("A+B", not "A,B"), which the runner executes in a process of its own; a single-database process never runs it.
    [Fact]
    public void ConjunctiveSkips_PinTheCombination()
    {
        var test = AllTests().Single(t => t.FullName ==
            "pengdows.crud.IntegrationTests.Core.WrappedTableNameMultiTenantIntegrationTests.SharedGateway_MixedNamespaceSupport_GeneratesCorrectSqlPerTenant_UnderConcurrentInterleaving");

        Assert.Equal(new[] { "PostgreSql+Sqlite" }, test.Databases);
    }

    // A fact that calls a same-class helper with a literal database runs the helper for that database alone.
    [Fact]
    public void FactsThatCallAHelperWithALiteralDatabase_ArePinnedToIt()
    {
        var tests = AllTests().ToDictionary(t => t.FullName, t => t.Databases, StringComparer.Ordinal);

        Assert.Equal(new[] { "MySql" }, tests["pengdows.crud.IntegrationTests.ErrorHandling.ReadOnlyRefusalTranslationTests.ServerInReadOnlyMode_MySql_WriteIsReadOnlyViolation"]);
        Assert.Equal(new[] { "TiDb" }, tests["pengdows.crud.IntegrationTests.ErrorHandling.ReadOnlyRefusalTranslationTests.ServerInReadOnlyMode_TiDb_WriteIsReadOnlyViolation"]);
    }

    // A capability skip (the database lacks the feature under test) must be attributed [SKIP:<Product>]. A skip whose
    // condition is configuration (which databases the run enables) or platform is not a capability skip and is pinned or
    // excluded instead, so it is not checked here.
    [Fact]
    public void CapabilitySkips_AreAttributedToTheirProduct()
    {
        var violations = new List<string>();
        foreach (var file in TestSources())
        {
            var text = File.ReadAllText(file);
            foreach (Match skip in SkipStatement.Matches(text))
            {
                var end = text.IndexOf(';', skip.Index);
                var statement = end < 0 ? text.Substring(skip.Index) : text.Substring(skip.Index, end - skip.Index);
                if (Regex.IsMatch(statement, @"IsEnabled|EnabledProviders|OperatingSystem|Directory\.Exists|_sqlite|_postgres|GetTestConfigurations"))
                {
                    continue;
                }

                if (!statement.Contains("\"[SKIP:", StringComparison.Ordinal))
                {
                    violations.Add($"{Path.GetFileName(file)}: {statement.Replace("\n", " ").Trim()}");
                }
            }
        }

        Assert.True(violations.Count == 0, "Capability skips without [SKIP:<Product>]:\n" + string.Join("\n", violations));
    }

    // A method that names its own database runs for that database alone, even when its class lists several.
    [Fact]
    public void MethodNamingItsOwnDatabase_IsPinnedToIt_NotToTheClassList()
    {
        var tests = AllTests().ToDictionary(t => t.FullName, t => t.Databases, StringComparer.Ordinal);

        Assert.Equal(new[] { "MariaDb" }, tests["pengdows.crud.IntegrationTests.DatabaseSpecific.TransactionRollbackOnKilledConnectionTests.MariaDb_KillingConnectionMidTransaction_RollsBackUncommittedInsert"]);
    }

    // A family minus a stated exclusion (DatabaseFamilies.X.Except(...)) pins exactly the members that remain.
    [Fact]
    public void FamilyExceptLists_PinExactlyTheRemainingDatabases()
    {
        var tests = AllTests().ToDictionary(t => t.FullName, t => t.Databases, StringComparer.Ordinal);

        Assert.Equal(new[] { "AuroraMySql", "MariaDb", "MySql" }, tests["pengdows.crud.IntegrationTests.DatabaseSpecific.MySqlSpatialRoundTripTests.MySqlSpatialTypes_RoundTripThroughCrudMapper"]);
        Assert.Equal(new[] { "AuroraPostgreSql", "PostgreSql", "YugabyteDb" }, tests["pengdows.crud.IntegrationTests.DatabaseSpecific.PostgreSqlRangeRoundTripTests.BoundedRange_RoundTrips"]);
    }

    // A test that picks its databases from the run's INTEGRATION_ONLY is the whole matrix: it is pinned to "Matrix", which no
    // database process selects, and the testbed step covers the matrix (Informix can only run there).
    [Fact]
    public void EnvironmentSelectedMatrixTest_IsPinnedToMatrix()
    {
        var tests = AllTests().ToDictionary(t => t.FullName, t => t.Databases, StringComparer.Ordinal);

        Assert.Equal(new[] { "Matrix" }, tests["pengdows.crud.IntegrationTests.IntegrationMatrixTests.All_supported_providers_complete_successfully"]);
    }
}
