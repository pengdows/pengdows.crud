#region

using System;
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
        foreach (var c in factory.Connections)
        {
            c.EnqueueScalarResult(value);
            c.SetDefaultScalarOnce(value);
        }

        // Ensure additional fallbacks
        for (var i = 0; i < 4; i++)
        {
            var extra = new fakeDbConnection();
            extra.EnableDataPersistence = factory.EnableDataPersistence;
            extra.EnqueueScalarResult(value);
            extra.SetDefaultScalarOnce(value);
            factory.Connections.Add(extra);
        }
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
        // Clear existing connections to start fresh
        factory.Connections.Clear();

        // Connection 1: Primary connection used for both initialization AND operations
        // This connection must handle:
        // - Database detection/version queries during DatabaseContext initialization
        // - INSERT operations and ID population during TableGateway.CreateAsync
        var primaryConnection = new fakeDbConnection();
        primaryConnection.EnableDataPersistence = factory.EnableDataPersistence;

        // Set up database detection queries for initialization phase
        primaryConnection.ScalarResultsByCommand["SELECT VERSION()"] = "Test Database 1.0";
        primaryConnection.ScalarResultsByCommand["SELECT @@VERSION"] = "Test Database 1.0";
        primaryConnection.ScalarResultsByCommand["SELECT version()"] = "Test Database 1.0";
        primaryConnection.ScalarResultsByCommand["PRAGMA version"] = "Test Database 1.0";
        // SQL Server specific queries
        primaryConnection.ScalarResultsByCommand[
            "SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE name = DB_NAME()"] = 0;
        primaryConnection.ScalarResultsByCommand[
            "SELECT snapshot_isolation_state FROM sys.databases WHERE name = DB_NAME()"] = 0;

        // Set up operation results for CreateAsync phase
        primaryConnection.EnqueueNonQueryResult(rowsAffected); // For INSERT statement
        primaryConnection.SetDefaultScalarOnce(generatedId); // For INSERT...RETURNING or first scalar call
        primaryConnection.EnqueueScalarResult(generatedId); // For subsequent scalar calls

        // Set up specific ID retrieval queries (for databases that don't support INSERT RETURNING)
        primaryConnection.ScalarResultsByCommand["SELECT SCOPE_IDENTITY()"] = generatedId;
        primaryConnection.ScalarResultsByCommand["SELECT last_insert_rowid()"] = generatedId;
        primaryConnection.ScalarResultsByCommand["SELECT LAST_INSERT_ID()"] = generatedId;
        primaryConnection.ScalarResultsByCommand["SELECT lastval()"] = generatedId;
        primaryConnection.ScalarResultsByCommand["SELECT @@IDENTITY"] = generatedId;

        factory.Connections.Add(primaryConnection);

        // Connection 2..N: Fallbacks for Standard mode and extra ops
        for (var i = 0; i < 6; i++)
        {
            var fx = new fakeDbConnection();
            fx.EnableDataPersistence = factory.EnableDataPersistence;
            fx.EnqueueNonQueryResult(rowsAffected);
            fx.SetDefaultScalarOnce(generatedId);
            fx.EnqueueScalarResult(generatedId);
            // Set up version queries for fallback connections too
            fx.ScalarResultsByCommand["SELECT VERSION()"] = "Test Database 1.0";
            fx.ScalarResultsByCommand["SELECT @@VERSION"] = "Test Database 1.0";
            fx.ScalarResultsByCommand["SELECT version()"] = "Test Database 1.0";
            fx.ScalarResultsByCommand["PRAGMA version"] = "Test Database 1.0";
            fx.ScalarResultsByCommand[
                "SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE name = DB_NAME()"] = 0;
            fx.ScalarResultsByCommand["SELECT snapshot_isolation_state FROM sys.databases WHERE name = DB_NAME()"] = 0;
            // Set up ID retrieval queries
            fx.ScalarResultsByCommand["SELECT SCOPE_IDENTITY()"] = generatedId;
            fx.ScalarResultsByCommand["SELECT last_insert_rowid()"] = generatedId;
            fx.ScalarResultsByCommand["SELECT LAST_INSERT_ID()"] = generatedId;
            fx.ScalarResultsByCommand["SELECT lastval()"] = generatedId;
            fx.ScalarResultsByCommand["SELECT @@IDENTITY"] = generatedId;
            factory.Connections.Add(fx);
        }
    }
}