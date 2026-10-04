using System;
using System.Data;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Critical path coverage tests to ensure 90%+ coverage on database access fundamentals.
/// These tests target error handling, edge cases, and security-critical paths.
/// </summary>
public class CriticalPathCoverageTests
{

    /// <summary>
    /// Test read-only context write operation rejection
    /// </summary>
    [Fact]
    public void DatabaseContext_ReadOnlyContextWriteOperation_ThrowsInvalidOperation()
    {
        // Test lines 338, 343 security checks
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadOnly
        };

        using var context = new DatabaseContext(config, factory);

        // Write operations on read-only context should fail
        Assert.Throws<NotSupportedException>(() =>
            context.BeginTransaction(executionType: ExecutionType.Write));
    }

}