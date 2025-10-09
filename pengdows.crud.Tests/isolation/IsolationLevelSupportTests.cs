#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using pengdows.crud.enums;
using pengdows.crud.isolation;
using Xunit;

#endregion

namespace pengdows.crud.Tests.isolation;

public class IsolationLevelSupportTests
{
    [Theory]
    [MemberData(nameof(GetSupportedLevelExpectations))]
    public void GetSupportedLevels_MatchesExpectations(
        SupportedDatabase database,
        bool rcsiEnabled,
        bool allowSnapshotIsolation,
        IsolationLevel[] expected)
    {
        var resolver = new IsolationResolver(database, rcsiEnabled, allowSnapshotIsolation);
        var actual = resolver.GetSupportedLevels().OrderBy(level => level).ToArray();

        Assert.Equal(expected.OrderBy(level => level).ToArray(), actual);
    }

    [Fact]
    public void Validate_UnsupportedLevel_Throws()
    {
        var sqlResolver = new IsolationResolver(
            SupportedDatabase.SqlServer,
            readCommittedSnapshotEnabled: true,
            allowSnapshotIsolation: true);
        Assert.Throws<InvalidOperationException>(() => sqlResolver.Validate(IsolationLevel.Chaos));

        var pgResolver = new IsolationResolver(
            SupportedDatabase.PostgreSql,
            readCommittedSnapshotEnabled: false,
            allowSnapshotIsolation: false);
        Assert.Throws<InvalidOperationException>(() => pgResolver.Validate(IsolationLevel.ReadUncommitted));
    }

    public static IEnumerable<object[]> GetSupportedLevelExpectations()
    {
        yield return new object[]
        {
            SupportedDatabase.SqlServer,
            true,
            true,
            new[]
            {
                IsolationLevel.ReadUncommitted,
                IsolationLevel.ReadCommitted,
                IsolationLevel.RepeatableRead,
                IsolationLevel.Serializable,
                IsolationLevel.Snapshot
            }
        };

        yield return new object[]
        {
            SupportedDatabase.PostgreSql,
            false,
            false,
            new[]
            {
                IsolationLevel.ReadCommitted,
                IsolationLevel.RepeatableRead,
                IsolationLevel.Serializable
            }
        };

        yield return new object[]
        {
            SupportedDatabase.MySql,
            false,
            false,
            new[]
            {
                IsolationLevel.ReadUncommitted,
                IsolationLevel.ReadCommitted,
                IsolationLevel.RepeatableRead,
                IsolationLevel.Serializable
            }
        };

        yield return new object[]
        {
            SupportedDatabase.DuckDB,
            false,
            false,
            new[]
            {
                IsolationLevel.Serializable
            }
        };
    }
}
