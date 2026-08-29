using System;
using System.Collections.Generic;
using System.Linq;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// CORE-018: BaseTableGateway.CheckParameterLimit validated against the gateway constructor's
/// own _context.MaxParameterLimit, ignoring the operation context represented by the supplied
/// ISqlContainer. A singleton gateway shared across tenants with materially different dialect
/// parameter limits (e.g. SQL Server's 2100 vs MySQL's 65535) would incorrectly reject a valid
/// query for a high-limit tenant using the low-limit tenant's constructor-time limit, or
/// (in the reverse direction) let an oversized query through where the LOW-limit gateway
/// context happened to have the higher number instead.
/// </summary>
public class SharedGatewayParameterLimitTests
{
    [Fact]
    public void BuildRetrieve_WithHighLimitOperationContext_UsesThatContextsLimitNotGatewayConstructorContext()
    {
        // Gateway constructed against SQL Server (MaxParameterLimit = 2100).
        var lowLimitFactory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var lowLimitContext = new DatabaseContext(
            "Server=low;Database=test;EmulatedProduct=SqlServer", lowLimitFactory);
        var gateway = new TableGateway<TestEntity, int>(lowLimitContext);

        // A different tenant context sharing this same singleton gateway: MySQL, whose
        // MaxParameterLimit (65535) is far higher, and which (like SQL Server) does not support
        // set-valued parameters, so the id count is checked directly rather than collapsed to 1.
        var highLimitFactory = new fakeDbFactory(SupportedDatabase.MySql);
        var highLimitContext = new DatabaseContext(
            "Server=high;Database=test;EmulatedProduct=MySql", highLimitFactory);

        // Exceeds SQL Server's 2100 limit but is well within MySQL's 65535.
        var ids = Enumerable.Range(1, 3000).ToList();

        var ex = Record.Exception(() => gateway.BuildRetrieve(ids, "t", highLimitContext));

        Assert.Null(ex);
    }

    [Fact]
    public void BuildRetrieve_WithLowLimitOperationContext_StillRejectsOversizedQuery()
    {
        // Gateway constructed against MySQL (MaxParameterLimit = 65535) this time — the
        // constructor-time context alone must not be what makes a check pass or fail.
        var highLimitFactory = new fakeDbFactory(SupportedDatabase.MySql);
        var highLimitContext = new DatabaseContext(
            "Server=high;Database=test;EmulatedProduct=MySql", highLimitFactory);
        var gateway = new TableGateway<TestEntity, int>(highLimitContext);

        var lowLimitFactory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var lowLimitContext = new DatabaseContext(
            "Server=low;Database=test;EmulatedProduct=SqlServer", lowLimitFactory);

        // Exceeds SQL Server's 2100 limit.
        var ids = Enumerable.Range(1, 3000).ToList();

        Assert.Throws<TooManyParametersException>(
            () => gateway.BuildRetrieve(ids, "t", lowLimitContext));
    }
}
