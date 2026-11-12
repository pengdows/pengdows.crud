using System.Reflection;
using pengdows.crud.dialects;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Dialect plumbing (SQL fragments and capability flags the core uses internally) lives on IInternalSqlDialect, which
/// is internal to pengdows.crud.abstractions. It must not sit on the public ISqlDialect, where it would be API that
/// consumers could depend on. (CLAUDE.md: "IsClientServerDatabase (internal dialect plumbing on IInternalSqlDialect,
/// not part of the public ISqlDialect)".)
/// </summary>
public class DialectPlumbingPlacementTests
{
    public static TheoryData<string> PlumbingMembers => new()
    {
        "PreservesTrailingWhitespace",
        "RequiresMergeStatementTerminator",
        "CurrentUtcTimestampExpression",
        "SelectWithoutTableSuffix",
        "SkipLockedClause",
        "SkipLockedTableHint",
    };

    [Theory]
    [MemberData(nameof(PlumbingMembers))]
    public void PlumbingMember_IsNotOnThePublicISqlDialect(string member)
    {
        Assert.Null(typeof(ISqlDialect).GetProperty(member));
    }

    [Theory]
    [MemberData(nameof(PlumbingMembers))]
    public void PlumbingMember_IsOnTheInternalDialectInterface(string member)
    {
        var internalDialect = typeof(ISqlDialect).Assembly.GetType("pengdows.crud.dialects.IInternalSqlDialect");

        Assert.NotNull(internalDialect);
        Assert.NotNull(internalDialect!.GetProperty(member, BindingFlags.Public | BindingFlags.Instance));
    }
}
