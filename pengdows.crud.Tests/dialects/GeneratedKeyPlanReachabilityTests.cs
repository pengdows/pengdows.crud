#region

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// CORE-016/TEST-010: <see cref="GeneratedKeyPlan.SessionScopedFunction"/> is the one plan value
/// that would make TableGateway.Core.cs's unconditional "default path" call
/// PopulateGeneratedIdAsync as the PRIMARY (non-fallback) ID-retrieval mechanism, reintroducing
/// the exact two-lease correctness hazard CompoundStatement/ReaderInsertedId/Returning were built
/// to avoid — a session-scoped last-insert-id query is not guaranteed to land on the same
/// physical connection that ran the INSERT. Tracing every dialect confirms this plan value is
/// currently unreachable: every dialect where the base SqlDialect.HasSessionScopedLastIdFunction()
/// would return true either overrides GetGeneratedKeyPlan() away from it (MySql/MariaDb/Sqlite,
/// and TiDb which inherits MySql's override) or has SupportsInsertReturning=true so the
/// Returning/OutputInserted branch fires first (SqlServer). This test locks that safety property
/// down as a regression gate: if a future dialect change reintroduces reachability, this test
/// goes red immediately instead of silently reintroducing the hazard. See docs/planning/future-work.md
/// (CORE-016/TEST-010) for the full investigation, including why PrefetchSequence is also
/// currently dead (Oracle overrides back to Returning) and the transaction-pinning mitigation for
/// the residual, real-provider-only hazard that remains for the fallback paths that ARE reachable.
/// </summary>
public class GeneratedKeyPlanReachabilityTests
{
    public static IEnumerable<object[]> AllSupportedDatabases()
    {
        foreach (var value in Enum.GetValues<SupportedDatabase>())
        {
            yield return new object[] { value };
        }
    }

    [Theory]
    [MemberData(nameof(AllSupportedDatabases))]
    public void GetGeneratedKeyPlan_NeverReturnsSessionScopedFunction(SupportedDatabase database)
    {
        var factory = new fakeDbFactory(database);
        var dialect = SqlDialectFactory.CreateDialectForType(database, factory, NullLogger.Instance);

        var plan = dialect.GetGeneratedKeyPlan();

        Assert.NotEqual(GeneratedKeyPlan.SessionScopedFunction, plan);
    }
}
