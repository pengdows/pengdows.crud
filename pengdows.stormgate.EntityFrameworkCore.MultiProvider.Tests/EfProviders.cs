using System.Data.Common;
using IBM.EntityFrameworkCore;

namespace pengdows.stormgate.EntityFrameworkCore.MultiProvider.Tests;

/// <summary>
/// Wires each pengdows.crud-supported-database family that has a viable EF Core provider to that
/// provider's own way of accepting an externally-supplied <see cref="DbConnection"/>, and defines
/// the two independent compatibility tiers that come out of that: does StormGate's connection
/// admission control work against this provider, and separately, does fakeDb's full ADO.NET
/// substitution work well enough for real SQL generation, parameter binding, and SaveChanges to
/// be unit-tested against it. A provider can satisfy the first without satisfying the second.
///
/// <b>Tier 1 — <see cref="ConnectionControlCapable"/>.</b> Does the provider accept a fakeDb
/// connection at all, and does StormGate's DbConnectionInterceptor-based admission control work
/// against it? Proven by <see cref="EfProviderCompatibilityTests"/>, which only opens/closes the
/// connection and never creates a command. Because <c>DbConnectionInterceptor</c> operates purely
/// on ADO.NET connection open/close events — a layer no provider's command/parameter/reader
/// casting can interfere with — every provider that accepts an external connection at all
/// satisfies this tier. In production, against a real database, this tier is the only one that
/// matters: it is what makes a provider usable with <c>pengdows.stormgate.EntityFrameworkCore</c>
/// for connection-storm protection at all.
///
/// <b>Tier 2 — <see cref="DeepTestCapable"/>.</b> The much stronger, fakeDb-testing-specific
/// claim: real SQL generation, real string-parameter binding, and SaveChanges round-tripping, all
/// running against fakeDb with zero real database engine. Proven by
/// <see cref="EfProviderDeepTests"/>' shared theory. Accepting a connection (Tier 1) turned out to
/// be necessary but nowhere near sufficient for this — every Tier-2 ❌ below is a hardcoded
/// <c>(ConcreteProviderType)genericDbObject</c> cast INSIDE THAT PROVIDER'S OWN EF Core
/// implementation code. This has nothing to do with StormGate, pengdows.crud, or anything this
/// project does — StormGate has no involvement in Tier 2 at all, and fakeDb is a generic,
/// provider-agnostic ADO.NET fake. This file merely EXPOSES, through direct reproduction, a
/// pre-existing fact about how each provider package happens to be written. It also says nothing
/// about production viability: every Tier-1 provider works fine against a real database
/// regardless of its Tier-2 result. In normal production use, the provider's own ADO.NET
/// implementation is what constructs the concrete command/parameter/reader instances it then
/// casts back to, so those casts always succeed — the failure mode only exists when something
/// other than that provider's own driver constructs the object, which fakeDb does deliberately
/// and a real connection never would. Tier 2 is purely about whether fakeDb can stand in for that
/// provider's own connection in a unit test.
///
/// Confirmed by direct reproduction for every entry below, not assumed:
///
/// - PostgreSQL (Npgsql): Tier 1 yes, Tier 2 no. Reads and string-parameter queries work fine,
///   but SaveChanges INSERT/UPDATE crashes — NpgsqlModificationCommandBatch.Consume casts the
///   reader to concrete NpgsqlDataReader.
/// - Firebird: Tier 1 yes, Tier 2 no. Reads and non-string writes work, but ANY string-valued
///   parameter (read WHERE clause or write column) crashes —
///   FbStringTypeMapping.ConfigureParameter casts the parameter to concrete FbParameter.
/// - Oracle: Tier 1 yes, Tier 2 no. Fails on literally any command, even a plain read-only query —
///   OracleRelationalCommandBuilder...OracleRelationalCommand.CreateDbCommand casts the command
///   to concrete OracleCommand unconditionally.
/// - Db2: Tier 1 yes, Tier 2 no. Same failure mode as Oracle — Db2RelationalCommand.CreateDbCommand
///   casts to concrete DB2Command unconditionally. (An earlier claim that "Db2 works" meant only
///   Tier 1 and did not hold once a real command was created — always state which tier a
///   compatibility claim is actually about.)
///
/// None of the four Tier-2 failures above are fixable by extending fakeDb — the provider is
/// casting to ITS OWN concrete type, which fakeDb satisfying would mean literally becoming that
/// provider's type, defeating the point of an external, provider-agnostic fake. Contrast with
/// Snowflake below, which WAS a genuine fakeDb gap (a missing feature, not a provider casting to a
/// concrete type) and was fixed.
///
/// - Snowflake: Tier 1 yes, Tier 2 yes (after a fix). Initially found broken
///   (DbUpdateConcurrencyException on every SaveChanges), root cause traced to the real
///   EFCore.Snowflake source (SnowflakeModificationCommandBatch.ConsumeResultSetWithRowsAffectedOnlyAsync
///   reads reader.DbDataReader.RecordsAffected directly, which fakeDbDataReader hardcoded to 0).
///   Fixed by adding fakeDbConnection.EnqueueReaderResult(rows, recordsAffected) — Snowflake is
///   fully Tier 2 now.
/// - SQLite, SQL Server, MySQL, MariaDB (Pomelo): Tier 1 yes, Tier 2 yes, no caveats found. MySQL
///   and MariaDB are net8.0-only in this project's MemberData lists (see the #if NET8_0 guards
///   below) because Pomelo.EntityFrameworkCore.MySql has no EF Core 10-compatible release yet.
///
/// DuckDB is absent from both MemberData lists above, but NOT because StormGate is incompatible
/// with it — it is absent because EnergyExemplar.EntityFrameworkCore.DuckDb 1.0.2's UseDuckDb has
/// no overload accepting an arbitrary DbConnection at all (only a DuckDbConnectionOptions/
/// connection-string object), so fakeDb cannot plug into it the way this file's two tiers are
/// tested. That is a statement about this file's fakeDb-injection testing *method*, not about
/// whether StormGateConnectionInterceptor can actually govern DuckDB. Reflection over the package
/// shows UseDuckDb is a thin layer over Microsoft.EntityFrameworkCore.Sqlite — the DbConnection
/// object EF Core actually opens/closes is a genuine Microsoft.Data.Sqlite.SqliteConnection (with
/// DuckDB's own engine substituted in via its native SQLite-ABI-compatible library), the exact
/// same connection type already proven fully Tier 1 and Tier 2 compatible above. Confirmed
/// directly — not assumed — by <see cref="DuckDbInterceptorRealProviderTests"/>, which drives a
/// real embedded DuckDB engine (no Docker, no fakeDb) through StormGateConnectionInterceptor and
/// proves saturation actually blocks a second concurrent open.
///
/// - DuckDB.EFCore (github.com/denis-ivanov/DuckDB.EFCore, net10.0-only, a second and independent
///   DuckDB EF Core provider from EnergyExemplar's above): Tier 1 no — unlike EnergyExemplar's
///   package, its UseDuckDB DOES accept an arbitrary DbConnection at the API level, so it was
///   actually tried against fakeDb via ConnectionControlCapable. It failed immediately with
///   InvalidCastException: DuckDBRelationalConnection.OpenDbConnectionAsync casts the incoming
///   connection to concrete DuckDB.NET.Data.DuckDBConnection unconditionally, one layer earlier
///   than the command-creation-time casts documented for Oracle/Firebird/Db2 above, so it doesn't
///   survive even Tier 1. Same underlying class of problem as those three, though — a hardcoded
///   concrete-type cast inside the provider's own code, not a StormGate/pengdows.crud gap — and
///   confirmed directly, not assumed, by <see cref="DuckDBEFCoreInterceptorRealProviderTests"/>,
///   which proves StormGateConnectionInterceptor governs a genuine DuckDBConnection (no fakeDb, no
///   Docker) exactly as it does every provider above.
/// </summary>
public static class EfProviders
{
    // Single source of truth for whether Pomelo.EntityFrameworkCore.MySql (and therefore MySQL
    // and MariaDB, which both go through it) is referenced on this TFM — see the .csproj comment
    // on its net8.0-only PackageReference. ConnectionControlCapable/DeepTestCapable both branch on
    // this instead of each repeating their own #if NET8_0 guard. Configure()'s switch case still
    // needs its own #if NET8_0 — UseMySql/MySqlServerVersion aren't even resolvable types when
    // Pomelo isn't referenced, so that one can't become a runtime check on this same field — but
    // it guards the exact same condition as this field and must be kept in sync with it.
    // static readonly (not const): a literal-true/false const branch is a compile-time-constant
    // condition, and the C# compiler flags the unreachable net10.0 branch as CS0162 for that —
    // readonly still resolves to the same fixed value per TFM without tripping that warning.
#if NET8_0
    private static readonly bool IsMySqlProviderAvailable = true;
#else
    private static readonly bool IsMySqlProviderAvailable = false;
#endif


    /// <summary>Tier 1: the databases verified by <see cref="EfProviderCompatibilityTests"/> (connection accept + StormGate admission control only — this is the production-relevant tier).</summary>
    public static IEnumerable<object[]> ConnectionControlCapable()
    {
        yield return new object[] { SupportedDatabase.Sqlite };
        yield return new object[] { SupportedDatabase.SqlServer };
        yield return new object[] { SupportedDatabase.PostgreSql };
        if (IsMySqlProviderAvailable)
        {
            yield return new object[] { SupportedDatabase.MySql };
            yield return new object[] { SupportedDatabase.MariaDb };
        }
        yield return new object[] { SupportedDatabase.Oracle };
        yield return new object[] { SupportedDatabase.Firebird };
        yield return new object[] { SupportedDatabase.Snowflake };
        yield return new object[] { SupportedDatabase.Db2 };
        // SupportedDatabase.DuckDB (via DuckDB.EFCore, net10.0-only) is deliberately absent here —
        // tried and found to fail even Tier 1: DuckDBRelationalConnection.OpenDbConnectionAsync
        // casts the incoming connection to concrete DuckDB.NET.Data.DuckDBConnection
        // unconditionally, so a fakeDbConnection throws InvalidCastException on Open(), before a
        // command is ever created. See DuckDBEFCoreInterceptorRealProviderTests for the real,
        // non-fakeDb proof that StormGateConnectionInterceptor governs it against a genuine
        // DuckDBConnection regardless.
    }

    /// <summary>
    /// Tier 2: the databases verified by <see cref="EfProviderDeepTests"/>' shared theory — real
    /// SQL generation, real string-parameter binding, and SaveChanges round-tripping all confirmed
    /// working, with zero provider-specific test code beyond the connection-wiring in
    /// <see cref="Configure"/>. A strict subset of <see cref="ConnectionControlCapable"/>.
    ///
    /// Deliberately extended beyond a single string parameter and a single-row insert, per
    /// external review: a provider's concrete-type casts live inside individual type mappings and
    /// individual pipeline stages, not centrally, so passing one shallow probe does not establish
    /// the others. Every database in this list is also confirmed, for every provider, by:
    /// <see cref="EfProviderTypeMatrixTests"/> (a representative CLR type matrix — long, decimal,
    /// DateTime, Guid, bool, nullable — each bound as a real parameter via a local-variable
    /// closure and materialized back, not just the one string case), <see cref="EfProviderTransactionTests"/>
    /// (an explicit BeginTransactionAsync/CommitAsync/RollbackAsync round-trip, including
    /// injected commit/rollback failures that must propagate as the real exception rather than an
    /// InvalidCastException from a provider unwrapping DbTransaction to its own concrete type),
    /// and <see cref="EfProviderBatchingTests"/> (several entities inserted in one SaveChangesAsync
    /// call — the exact code path, EF's modification-command batching, inside which the Npgsql
    /// failure documented below actually lives; a provider passing on one row is not thereby
    /// proven to pass once batching activates).
    ///
    /// PostgreSql, Firebird, Oracle, and Db2 are deliberately absent — see the scoped,
    /// individually-labeled regression tests in <see cref="EfProviderDeepTests"/> that lock in
    /// exactly why each one is excluded. Being absent here says nothing about production
    /// viability — see the Tier 1/Tier 2 distinction in the type-level doc comment.
    /// </summary>
    public static IEnumerable<object[]> DeepTestCapable()
    {
        yield return new object[] { SupportedDatabase.Sqlite };
        yield return new object[] { SupportedDatabase.SqlServer };
        if (IsMySqlProviderAvailable)
        {
            yield return new object[] { SupportedDatabase.MySql };
            yield return new object[] { SupportedDatabase.MariaDb };
        }
        yield return new object[] { SupportedDatabase.Snowflake };
    }

    public static void Configure(SupportedDatabase database, DbContextOptionsBuilder builder, DbConnection connection)
    {
        switch (database)
        {
            case SupportedDatabase.Sqlite:
                builder.UseSqlite(connection, contextOwnsConnection: false);
                break;

            case SupportedDatabase.SqlServer:
                builder.UseSqlServer(connection, contextOwnsConnection: false);
                break;

            case SupportedDatabase.PostgreSql:
                builder.UseNpgsql(connection, contextOwnsConnection: false);
                break;

#if NET8_0
            case SupportedDatabase.MySql:
            case SupportedDatabase.MariaDb:
                builder.UseMySql(connection, new MySqlServerVersion(new Version(8, 0, 33)));
                break;
#endif

            case SupportedDatabase.Oracle:
                builder.UseOracle(connection, contextOwnsConnection: false);
                break;

            case SupportedDatabase.Firebird:
                builder.UseFirebird(connection);
                break;

            case SupportedDatabase.Snowflake:
                builder.UseSnowflake(connection, contextOwnsConnection: false);
                break;

            case SupportedDatabase.Db2:
                builder.UseDb2(connection, _ => { });
                break;

            // SupportedDatabase.DuckDB (DuckDB.EFCore) is deliberately NOT wired up here — it
            // fails even Tier 1 against fakeDb (see the class doc comment above and
            // DuckDBEFCoreInterceptorRealProviderTests), so nothing ever calls Configure() with
            // it, and every caller of Configure() is driven exclusively by MemberData lists or
            // hardcoded SupportedDatabase literals, neither of which include it. A case here
            // would be genuinely unreachable dead code.

            default:
                throw new NotSupportedException(
                    $"No EF Core provider is wired up for {database} in this test project.");
        }
    }
}
