using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests.dialects;

// REV-044: the synchronous constructor path used to block on the async-only version detection
// (DetectDatabaseInfoAsync), running async ADO.NET calls under GetAwaiter().GetResult(). It now
// takes a synchronous core that issues only synchronous commands.
public class SynchronousVersionDetectionTests
{
    public static IEnumerable<object[]> Databases() =>
        Enum.GetValues<SupportedDatabase>()
            .Where(db => db != SupportedDatabase.Unknown)
            .Select(db => new object[] { db });

    [Theory]
    [MemberData(nameof(Databases))]
    public void CreateDialect_Synchronous_IssuesNoAsyncCommands(SupportedDatabase database)
    {
        var factory = new fakeDbFactory(database);
        var connection = (fakeDbConnection)factory.CreateConnection();
        connection.Open();
        using var tracked = new TrackedConnection(connection);

        SqlDialectFactory.CreateDialect(tracked, factory, NullLoggerFactory.Instance);

        Assert.Empty(connection.CreatedCommands.Where(c => c.AsyncExecuteCount > 0)
            .Select(c => c.CommandText));
    }

    [Theory]
    [MemberData(nameof(Databases))]
    public void DetectDatabaseInfo_Synchronous_IssuesNoAsyncCommands(SupportedDatabase database)
    {
        var factory = new fakeDbFactory(database);
        var connection = (fakeDbConnection)factory.CreateConnection();
        connection.Open();
        using var tracked = new TrackedConnection(connection);
        var dialect = (SqlDialect)SqlDialectFactory.CreateDialectForType(database, factory, NullLogger.Instance);

        dialect.DetectDatabaseInfo(tracked);

        Assert.Empty(connection.CreatedCommands.Where(c => c.AsyncExecuteCount > 0)
            .Select(c => c.CommandText));
    }
}
