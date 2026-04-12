#region

using System;
using System.Collections.Generic;
using pengdows.crud.fakeDb;

#endregion

namespace pengdows.crud.Tests;

internal static class fakeDbFactoryTestExtensions
{
    public static void SetNonQueryResult(this fakeDbFactory factory, int value)
    {
        // Seed multiple connections to cover init + ops under various strategies
        for (var i = 0; i < 8; i++)
        {
            var c = new fakeDbConnection();
            c.EnableDataPersistence = factory.EnableDataPersistence;
            c.EnqueueNonQueryResult(value);
            c.EnqueueNonQueryResult(value);
            c.EnqueueNonQueryResult(value);
            c.SetDefaultScalarOnce(value);
            factory.Connections.Add(c);
        }
    }

    public static void SetScalarResult(this fakeDbFactory factory, object? value)
    {
        // Build reader row: ExecuteScalarCore uses ExecuteReaderAsync internally,
        // so we must feed the reader queue (single row, single column).
        var readerRow = MakeScalarReaderRow(value);

        foreach (var c in factory.Connections)
        {
            c.EnqueueReaderResult(readerRow);
            c.EnqueueScalarResult(value); // also feed ADO.NET scalar path (used by dialect internals)
            c.SetDefaultScalarOnce(value);
        }

        // Ensure additional fallbacks
        for (var i = 0; i < 4; i++)
        {
            var extra = new fakeDbConnection();
            extra.EnableDataPersistence = factory.EnableDataPersistence;
            extra.EnqueueReaderResult(readerRow);
            extra.EnqueueScalarResult(value);
            extra.SetDefaultScalarOnce(value);
            factory.Connections.Add(extra);
        }
    }

    private static List<Dictionary<string, object?>> MakeScalarReaderRow(object? value)
    {
        return [new Dictionary<string, object?> { ["scalar"] = value }];
    }

    public static void SetNonQueryException(this fakeDbFactory factory, Exception exception)
    {
        for (var i = 0; i < 6; i++)
        {
            var c = new fakeDbConnection();
            c.EnableDataPersistence = factory.EnableDataPersistence;
            c.SetNonQueryExecuteException(exception);
            factory.Connections.Add(c);
        }
    }

    public static void SetScalarException(this fakeDbFactory factory, Exception exception)
    {
        // Use the new global persistent scalar exception which applies to all future connections
        factory.SetGlobalPersistentScalarException(exception);
    }

    public static void SetConnectionException(this fakeDbFactory factory, Exception exception)
    {
        // Connection failure expected during initialization
        var init = new fakeDbConnection();
        init.EnableDataPersistence = factory.EnableDataPersistence;
        init.SetCustomFailureException(exception);
        init.SetFailOnOpen();
        factory.Connections.Add(init);
    }

    public static void SetException(this fakeDbFactory factory, Exception exception)
    {
        // Seed several failing connections to catch any op connection
        for (var i = 0; i < 6; i++)
        {
            var op = new fakeDbConnection();
            op.EnableDataPersistence = factory.EnableDataPersistence;
            op.SetCustomFailureException(exception);
            op.SetFailOnCommand();
            factory.Connections.Add(op);
        }
    }

    /// <summary>
    /// Sets up the factory to handle ID population scenarios for TableGateway.CreateAsync.
    /// This accounts for the DatabaseContext connection lifecycle:
    /// 1. Initialization connection is used for database detection, version queries, session settings
    /// 2. For Standard mode: initialization connection is disposed, operations use new connections
    /// 3. For SingleConnection/SingleWriter/KeepAlive: initialization connection is kept and reused
    /// </summary>
    public static void SetIdPopulationResult(this fakeDbFactory factory, object? generatedId, int rowsAffected = 1)
    {
        factory.Connections.Clear();

        // Primary connection + 6 fallbacks to cover Standard mode (new connection per op)
        for (var i = 0; i < 7; i++)
        {
            factory.Connections.Add(MakeIdPopulationConnection(factory.EnableDataPersistence, generatedId, rowsAffected));
        }
    }

    private static fakeDbConnection MakeIdPopulationConnection(bool enableDataPersistence, object? generatedId, int rowsAffected)
    {
        var c = new fakeDbConnection();
        c.EnableDataPersistence = enableDataPersistence;

        // Database detection / version queries (initialization phase)
        c.ScalarResultsByCommand["SELECT VERSION()"] = "Test Database 1.0";
        c.ScalarResultsByCommand["SELECT @@VERSION"] = "Test Database 1.0";
        c.ScalarResultsByCommand["SELECT version()"] = "Test Database 1.0";
        c.ScalarResultsByCommand["PRAGMA version"] = "Test Database 1.0";
        c.ScalarResultsByCommand[
            "SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE name = DB_NAME()"] = 0;
        c.ScalarResultsByCommand[
            "SELECT snapshot_isolation_state FROM sys.databases WHERE name = DB_NAME()"] = 0;

        // Identity retrieval queries (post-INSERT)
        c.ScalarResultsByCommand["SELECT SCOPE_IDENTITY()"] = generatedId;
        c.ScalarResultsByCommand["SELECT last_insert_rowid()"] = generatedId;
        c.ScalarResultsByCommand["SELECT LAST_INSERT_ID()"] = generatedId;
        c.ScalarResultsByCommand["SELECT lastval()"] = generatedId;
        c.ScalarResultsByCommand["SELECT @@IDENTITY"] = generatedId;

        c.EnqueueNonQueryResult(rowsAffected);
        c.EnqueueReaderResult(MakeScalarReaderRow(generatedId));
        c.SetDefaultScalarOnce(generatedId);
        c.EnqueueScalarResult(generatedId);

        return c;
    }
}