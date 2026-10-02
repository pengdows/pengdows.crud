using System.Linq;
using CrudBenchmarks;
using Xunit;

namespace CrudBenchmarks.Tests;

// Backs PostgreSqlMethodologyBenchmarks' SQL proof: every equal-footing case records what
// pg_stat_statements saw, and the report shows whether the frameworks sent the same statement
// and exactly one statement per operation.
public sealed class SqlProofTests
{
    private static SqlProofCase Case(string framework, long operations, params (string Query, long Calls)[] statements) =>
        new("ReadSingle", framework, "Baseline", EqualFooting: true, operations,
            statements.Select(s => new SqlProofStatement(s.Query, s.Calls)).ToList());

    [Fact]
    public void Normalize_IgnoresQuotingCaseWhitespaceAndTableQualifiers()
    {
        Assert.Equal(
            SqlProof.Normalize("SELECT id, name FROM benchmark WHERE id = $1"),
            SqlProof.Normalize("select \"b\".\"id\",  \"b\".\"name\"\nFROM \"benchmark\" \"b\" WHERE \"b\".\"id\" = $1"));
    }

    [Fact]
    public void Normalize_KeepsRealDifferences()
    {
        Assert.NotEqual(
            SqlProof.Normalize("SELECT id FROM benchmark WHERE id = $1"),
            SqlProof.Normalize("SELECT b.id FROM (SELECT id FROM benchmark WHERE id = $1) AS b LIMIT $2"));
    }

    [Fact]
    public void Analyze_SameStatementOncePerOperation_HasNoIssues()
    {
        var issues = SqlProof.Analyze(new[]
        {
            Case("Pengdows", 40, ("SELECT \"b\".\"id\" FROM \"benchmark\" \"b\" WHERE \"b\".\"id\" = $1", 40)),
            Case("Dapper", 40, ("SELECT id FROM benchmark WHERE id = $1", 40)),
        });

        Assert.Empty(issues);
    }

    [Fact]
    public void Analyze_ExtraStatementsBeyondTheOperationCount_AreReported()
    {
        var issues = SqlProof.Analyze(new[]
        {
            Case("Pengdows", 40, ("SELECT id FROM benchmark WHERE id = $1", 40), ("SET statement_timeout = $1", 40)),
        });

        var issue = Assert.Single(issues);
        Assert.Contains("Pengdows", issue);
        Assert.Contains("80 statements for 40 operations", issue);
    }

    [Fact]
    public void Analyze_DifferentStatementsBetweenEqualFootingFrameworks_AreReported()
    {
        var issues = SqlProof.Analyze(new[]
        {
            Case("Dapper", 40, ("SELECT id FROM benchmark WHERE id = $1", 40)),
            Case("EntityFramework", 40, ("SELECT b.id FROM (SELECT id FROM benchmark WHERE id = $1) AS b LIMIT $2", 40)),
        });

        var issue = Assert.Single(issues);
        Assert.Contains("ReadSingle", issue);
        Assert.Contains("EntityFramework", issue);
    }

    [Fact]
    public void Analyze_CellsThatAreNotEqualFooting_AreCountedButNotCompared()
    {
        var linq = new SqlProofCase("ReadSingle", "EntityFramework_Linq", "Baseline", EqualFooting: false, 40,
            new[] { new SqlProofStatement("SELECT b.id FROM benchmark AS b WHERE b.id = $1 LIMIT $2", 40) });

        var issues = SqlProof.Analyze(new[] { Case("Dapper", 40, ("SELECT id FROM benchmark WHERE id = $1", 40)), linq });

        Assert.Empty(issues);
    }

    [Fact]
    public void Analyze_ComparesFrameworksOnlyWithinTheSameJob()
    {
        var pinned = new SqlProofCase("ReadSingle", "Pengdows", "Pinned", EqualFooting: true, 40,
            new[] { new SqlProofStatement("SELECT id FROM benchmark WHERE id = $1", 40) });

        Assert.Empty(SqlProof.Analyze(new[] { Case("Dapper", 40, ("SELECT id FROM benchmark WHERE id = $1", 40)), pinned }));
    }

    [Fact]
    public void Report_ListsEveryCaseWithItsStatementsAndTheIssues()
    {
        var report = SqlProof.Report(new[]
        {
            Case("Dapper", 40, ("SELECT id FROM benchmark WHERE id = $1", 40)),
            Case("EntityFramework", 40, ("SELECT b.id FROM (SELECT id FROM benchmark WHERE id = $1) AS b LIMIT $2", 40)),
        });

        Assert.Contains("| Baseline | ReadSingle | Dapper | yes | 40 | 40 | 0.00 |", report);
        Assert.Contains("SELECT id FROM benchmark WHERE id = $1", report);
        Assert.Contains("## Issues", report);
    }

    // Npgsql resets a pooled connection when it is returned (live, Npgsql 8 with prepared statements:
    // the parts of DISCARD ALL that keep prepared statements). That is the driver, the same for every
    // framework that closes its connection, so it is reported per operation, not as extra statements.
    private static readonly (string, long)[] Reset =
    {
        ("CLOSE ALL", 40), ("UNLISTEN *", 40), ("SELECT pg_advisory_unlock_all()", 40), ("DISCARD SEQUENCES", 40),
        ("DISCARD TEMP", 40), ("RESET ALL", 40), ("SET SESSION AUTHORIZATION DEFAULT", 40), ("DISCARD ALL", 1)
    };

    [Fact]
    public void Analyze_NpgsqlPoolResetStatements_AreNotCountedAsExtraStatements()
    {
        var issues = SqlProof.Analyze(new[]
        {
            Case("Dapper", 40, Reset.Append(("SELECT id FROM benchmark WHERE id = $1", 40L)).ToArray()),
            Case("Pengdows", 40, Reset.Append(("SELECT \"id\" FROM \"benchmark\" WHERE \"id\" = $1", 40L)).ToArray()),
        });

        Assert.Empty(issues);
    }

    [Fact]
    public void Analyze_EqualFootingFrameworksWithDifferentResetsPerOperation_AreReported()
    {
        var issues = SqlProof.Analyze(new[]
        {
            Case("Dapper", 40, Reset.Append(("SELECT id FROM benchmark WHERE id = $1", 40L)).ToArray()),
            Case("Pengdows", 40, ("SELECT id FROM benchmark WHERE id = $1", 40)),
        });

        var issue = Assert.Single(issues);
        Assert.Contains("connection resets", issue);
        Assert.Contains("Pengdows", issue);
    }

    [Fact]
    public void Report_ShowsResetsPerOperation()
    {
        var report = SqlProof.Report(new[]
        {
            Case("Dapper", 40, Reset.Append(("SELECT id FROM benchmark WHERE id = $1", 40L)).ToArray()),
        });

        Assert.Contains("| Baseline | ReadSingle | Dapper | yes | 40 | 40 | 1.00 |", report);
    }
}
