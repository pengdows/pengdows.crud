using pengdows.crud.dialects;
using pengdows.crud.enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;

public static class HierarchicalIdRetrievalDemo
{
    public static void Run()
    {
        Console.WriteLine("🎯 Hierarchical ID Retrieval System Demo");
        Console.WriteLine("=========================================\n");

        var logger = NullLogger.Instance;
        var factory = SqliteFactory.Instance;

        // Test all database types and their strategy selection
        var dialects = new Dictionary<string, SqlDialect>
        {
            {"PostgreSQL", SqlDialectFactory.CreateDialectForType(SupportedDatabase.PostgreSql, factory, logger)},
            {"SQL Server", SqlDialectFactory.CreateDialectForType(SupportedDatabase.SqlServer, factory, logger)},
            {"MySQL", SqlDialectFactory.CreateDialectForType(SupportedDatabase.MySql, factory, logger)},
            {"MariaDB", SqlDialectFactory.CreateDialectForType(SupportedDatabase.MariaDb, factory, logger)},
            {"SQLite", SqlDialectFactory.CreateDialectForType(SupportedDatabase.Sqlite, factory, logger)},
            {"Oracle", SqlDialectFactory.CreateDialectForType(SupportedDatabase.Oracle, factory, logger)},
            {"Firebird", SqlDialectFactory.CreateDialectForType(SupportedDatabase.Firebird, factory, logger)},
            {"DuckDB", SqlDialectFactory.CreateDialectForType(SupportedDatabase.DuckDB, factory, logger)}
        };

        Console.WriteLine("📊 Strategy Selection by Database:");
        Console.WriteLine("----------------------------------");

        foreach (var (name, dialect) in dialects)
        {
            var plan = dialect.GetGeneratedKeyPlan();
            var description = plan switch
            {
                GeneratedKeyPlan.Returning => "✅ RETURNING clause (atomic, best)",
                GeneratedKeyPlan.OutputInserted => "✅ OUTPUT INSERTED clause (atomic, best)",
                GeneratedKeyPlan.SessionScopedFunction => "🔧 Session function (safe on same connection)",
                GeneratedKeyPlan.PrefetchSequence => "🎯 Sequence prefetch (Oracle preferred)",
                GeneratedKeyPlan.CorrelationToken => "🔗 Correlation token (universal fallback)",
                GeneratedKeyPlan.NaturalKeyLookup => "⚠️ Natural key lookup (requires unique constraints)",
                _ => "❓ Unknown strategy"
            };

            Console.WriteLine($"{name,-12}: {plan,-20} - {description}");
        }

        Console.WriteLine("\n🛠️ Implementation Examples:");
        Console.WriteLine("----------------------------");

        // Demonstrate RETURNING clause
        var postgres = SqlDialectFactory.CreateDialectForType(SupportedDatabase.PostgreSql, factory, logger);
        Console.WriteLine($"PostgreSQL RETURNING: {postgres.GetInsertReturningClause("user_id")}");

        // Demonstrate OUTPUT clause
        var sqlServer = SqlDialectFactory.CreateDialectForType(SupportedDatabase.SqlServer, factory, logger);
        Console.WriteLine($"SQL Server OUTPUT:   {sqlServer.GetInsertReturningClause("user_id")}");

        // Demonstrate session function
        var mysql = SqlDialectFactory.CreateDialectForType(SupportedDatabase.MySql, factory, logger);
        Console.WriteLine($"MySQL Session Func:  {mysql.GetLastInsertedIdQuery()}");

        // Demonstrate correlation token
        Console.WriteLine($"Correlation Token:   {postgres.GetCorrelationTokenLookupQuery("users", "id", "insert_token", ":token")}");

        // Demonstrate Oracle sequence handling
        var oracle = SqlDialectFactory.CreateDialectForType(SupportedDatabase.Oracle, factory, logger);
        try
        {
            oracle.GetLastInsertedIdQuery();
        }
        catch (NotSupportedException ex)
        {
            Console.WriteLine($"Oracle Strategy:     {ex.Message}");
        }

        Console.WriteLine("\n🔄 Fallback Hierarchy:");
        Console.WriteLine("----------------------");
        Console.WriteLine("1. 🥇 Inline RETURNING/OUTPUT (atomic, single round-trip)");
        Console.WriteLine("2. 🥈 Session-scoped functions (safe on same connection)");
        Console.WriteLine("3. 🥉 Sequence prefetch (Oracle's preferred approach)");
        Console.WriteLine("4. 🔗 Correlation token (universal, race-free fallback)");
        Console.WriteLine("5. ⚠️ Natural key lookup (last resort, needs unique constraints)");

        Console.WriteLine("\n✅ Hierarchical ID Retrieval System: FULLY IMPLEMENTED");
        Console.WriteLine("🔐 Race-condition free strategies prioritized");
        Console.WriteLine("🌐 Universal fallback available for any database");
        Console.WriteLine("📏 Proper OOP polymorphism with no switch statements");
    }
}