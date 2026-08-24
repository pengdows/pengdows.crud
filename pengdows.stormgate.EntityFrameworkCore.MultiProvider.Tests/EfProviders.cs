using System.Data.Common;
using IBM.EntityFrameworkCore;

namespace pengdows.stormgate.EntityFrameworkCore.MultiProvider.Tests;

/// <summary>
/// Wires each pengdows.crud-supported-database family that has a viable EF Core provider to that
/// provider's own way of accepting an externally-supplied <see cref="DbConnection"/>.
///
/// <see cref="All"/> is the narrow claim: does the provider accept a fakeDb connection at all,
/// and does StormGate's DbConnectionInterceptor-based admission control work against it — proven
/// by <see cref="EfProviderCompatibilityTests"/>, which only opens/closes the connection and
/// never creates a command.
///
/// <see cref="DeepTestCapable"/> is the much stronger claim — real SQL generation, real
/// parameter binding, and SaveChanges round-tripping, proven by <see cref="EfProviderDeepTests"/>.
/// Accepting a connection turned out to be necessary but nowhere near sufficient: several
/// providers that pass the narrow test crash the moment a real DbCommand/DbParameter/DbDataReader
/// is actually used, because their own code casts fakeDb's objects to their concrete provider
/// type somewhere in the pipeline. Confirmed by direct reproduction for every entry below, not
/// assumed:
///
/// - PostgreSQL (Npgsql): reads and string-parameter queries work fine, but SaveChanges
///   INSERT/UPDATE crashes — NpgsqlModificationCommandBatch.Consume casts the reader to concrete
///   NpgsqlDataReader.
/// - Firebird: reads and non-string writes work, but ANY string-valued parameter (read WHERE
///   clause or write column) crashes — FbStringTypeMapping.ConfigureParameter casts the parameter
///   to concrete FbParameter.
/// - Oracle: fails on literally any command, even a plain read-only query —
///   OracleRelationalCommandBuilder...OracleRelationalCommand.CreateDbCommand casts the command
///   to concrete OracleCommand unconditionally.
/// - Db2: same failure mode as Oracle — Db2RelationalCommand.CreateDbCommand casts to concrete
///   DB2Command unconditionally. (An earlier claim that "Db2 works" was based only on the narrow
///   connection-lifecycle test above and did not hold once a real command was created.)
///
/// None of the four are fixable by extending fakeDb — the provider is casting to ITS OWN
/// concrete type, which fakeDb satisfying would mean literally becoming that provider's type,
/// defeating the point of an external, provider-agnostic fake. Contrast with Snowflake below,
/// which WAS a genuine fakeDb gap (a missing feature, not a provider casting to a concrete type)
/// and was fixed.
///
/// Snowflake: initially found broken (DbUpdateConcurrencyException on every SaveChanges), root
/// cause traced to the real EFCore.Snowflake source
/// (SnowflakeModificationCommandBatch.ConsumeResultSetWithRowsAffectedOnlyAsync reads
/// reader.DbDataReader.RecordsAffected directly, which fakeDbDataReader hardcoded to 0). Fixed by
/// adding fakeDbConnection.EnqueueReaderResult(rows, recordsAffected) — Snowflake is fully
/// DeepTestCapable now.
///
/// DuckDB and DuckDB alone remains excluded from even <see cref="All"/> — a confirmed
/// architectural gap, not an assumed one: reflected over both viable packages' actual public API
/// before writing any test code. EnergyExemplar.EntityFrameworkCore.DuckDb 1.0.2 (the only one
/// with a net8.0 build) has no overload accepting an arbitrary DbConnection at all — only a
/// DuckDbConnectionOptions object or a file-path-based Parquet configuration. DuckDB.EFCore (the
/// more actively developed provider) only targets net10.0, incompatible with this project's
/// deliberate net8.0-only scoping.
/// </summary>
public static class EfProviders
{
    /// <summary>The databases verified by <see cref="EfProviderCompatibilityTests"/> (connection accept + admission control only).</summary>
    public static IEnumerable<object[]> All()
    {
        yield return new object[] { SupportedDatabase.SqlServer };
        yield return new object[] { SupportedDatabase.PostgreSql };
        yield return new object[] { SupportedDatabase.MySql };
        yield return new object[] { SupportedDatabase.MariaDb };
        yield return new object[] { SupportedDatabase.Oracle };
        yield return new object[] { SupportedDatabase.Firebird };
        yield return new object[] { SupportedDatabase.Snowflake };
        yield return new object[] { SupportedDatabase.Db2 };
    }

    /// <summary>
    /// The databases verified by <see cref="EfProviderDeepTests"/>' shared theory: real SQL
    /// generation, real string-parameter binding, and SaveChanges round-tripping all confirmed
    /// working, with zero provider-specific test code beyond the connection-wiring in
    /// <see cref="Configure"/>. PostgreSql, Firebird, Oracle, and Db2 are deliberately absent —
    /// see the scoped, individually-labeled regression tests in <see cref="EfProviderDeepTests"/>
    /// that lock in exactly why each one is excluded.
    /// </summary>
    public static IEnumerable<object[]> DeepTestCapable()
    {
        yield return new object[] { SupportedDatabase.Sqlite };
        yield return new object[] { SupportedDatabase.SqlServer };
        yield return new object[] { SupportedDatabase.MySql };
        yield return new object[] { SupportedDatabase.MariaDb };
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

            case SupportedDatabase.MySql:
            case SupportedDatabase.MariaDb:
                builder.UseMySql(connection, new MySqlServerVersion(new Version(8, 0, 33)));
                break;

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

            default:
                throw new NotSupportedException(
                    $"No EF Core provider is wired up for {database} in this test project.");
        }
    }
}
