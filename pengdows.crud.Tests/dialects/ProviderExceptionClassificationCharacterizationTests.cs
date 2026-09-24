using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Characterization tests for SqlDialect.TryClassifyProviderException (private, non-virtual —
/// the one candidate in this file's switch inventory not already virtual), written BEFORE
/// promoting it to protected virtual and splitting its switch(DatabaseType) block into
/// per-dialect overrides (CLAUDE.md "Adding a New Database" checklist item 11 — the exception
/// classification duplicate-maintenance trap). Exercised through the public ClassifyException
/// entry point against real dialect instances, since that's the actual production call path.
/// </summary>
public class ProviderExceptionClassificationCharacterizationTests
{
    private sealed class NumberedDbException : DbException
    {
        public int Number { get; }
        public NumberedDbException(int number, string message = "") : base(message)
        {
            Number = number;
            HResult = number;
        }
    }

    private sealed class SqlStateDbException : DbException
    {
        public new string SqlState { get; }
        public SqlStateDbException(string sqlState, string message = "") : base(message) => SqlState = sqlState;
    }

    private sealed class MessageOnlyDbException : DbException
    {
        public MessageOnlyDbException(string message) : base(message) { }
    }

    private static SqlServerDialect SqlServer() => new(new fakeDbFactory(SupportedDatabase.SqlServer), NullLogger.Instance);
    private static PostgreSqlDialect Postgres() => new(new fakeDbFactory(SupportedDatabase.PostgreSql), NullLogger.Instance);
    private static MySqlDialect MySql() => new(new fakeDbFactory(SupportedDatabase.MySql), NullLogger.Instance);
    private static OracleDialect Oracle() => new(new fakeDbFactory(SupportedDatabase.Oracle), NullLogger.Instance);
    private static SqliteDialect Sqlite() => new(new fakeDbFactory(SupportedDatabase.Sqlite), NullLogger.Instance);
    private static DuckDbDialect DuckDb() => new(new fakeDbFactory(SupportedDatabase.DuckDB), NullLogger.Instance);
    private static FirebirdDialect Firebird() => new(new fakeDbFactory(SupportedDatabase.Firebird), NullLogger.Instance);
    private static Db2Dialect Db2() => new(new fakeDbFactory(SupportedDatabase.Db2), NullLogger.Instance);
    private static SnowflakeDialect Snowflake() => new(new fakeDbFactory(SupportedDatabase.Snowflake), NullLogger.Instance);
    private static SpannerDialect Spanner() => new(new fakeDbFactory(SupportedDatabase.Spanner), NullLogger.Instance);

    // ---------- SqlServer ----------
    [Fact] public void SqlServer_Deadlock() => Assert.Equal(DbErrorCategory.Deadlock, SqlServer().ClassifyException(new NumberedDbException(1205)));
    [Fact] public void SqlServer_Serialization() => Assert.Equal(DbErrorCategory.SerializationFailure, SqlServer().ClassifyException(new NumberedDbException(3960)));
    [Fact] public void SqlServer_Timeout() => Assert.Equal(DbErrorCategory.Timeout, SqlServer().ClassifyException(new NumberedDbException(-2)));
    [Fact] public void SqlServer_Constraint() => Assert.Equal(DbErrorCategory.ConstraintViolation, SqlServer().ClassifyException(new NumberedDbException(2627)));

    // ---------- Postgres family ----------
    [Fact] public void Postgres_Deadlock() => Assert.Equal(DbErrorCategory.Deadlock, Postgres().ClassifyException(new SqlStateDbException("40P01")));
    [Fact] public void Postgres_Serialization() => Assert.Equal(DbErrorCategory.SerializationFailure, Postgres().ClassifyException(new SqlStateDbException("40001")));
    [Fact] public void Postgres_AmbiguousResult() => Assert.Equal(DbErrorCategory.AmbiguousResult, Postgres().ClassifyException(new SqlStateDbException("40003")));
    [Fact] public void Postgres_Timeout_LockNotAvailable() => Assert.Equal(DbErrorCategory.Timeout, Postgres().ClassifyException(new SqlStateDbException("55P03")));
    [Fact] public void Postgres_Timeout_QueryCanceled() => Assert.Equal(DbErrorCategory.Timeout, Postgres().ClassifyException(new SqlStateDbException("57014")));
    [Fact] public void Postgres_Constraint_Class23() => Assert.Equal(DbErrorCategory.ConstraintViolation, Postgres().ClassifyException(new SqlStateDbException("23505")));

    // ---------- MySql family ----------
    [Fact] public void MySql_Deadlock() => Assert.Equal(DbErrorCategory.Deadlock, MySql().ClassifyException(new NumberedDbException(1213)));
    [Fact] public void MySql_Timeout() => Assert.Equal(DbErrorCategory.Timeout, MySql().ClassifyException(new NumberedDbException(1205)));
    [Fact] public void MySql_Serialization() => Assert.Equal(DbErrorCategory.SerializationFailure, MySql().ClassifyException(new SqlStateDbException("40001")));
    [Fact] public void MySql_Constraint() => Assert.Equal(DbErrorCategory.ConstraintViolation, MySql().ClassifyException(new NumberedDbException(1062)));

    // ---------- Oracle ----------
    [Fact] public void Oracle_Deadlock() => Assert.Equal(DbErrorCategory.Deadlock, Oracle().ClassifyException(new NumberedDbException(60)));
    [Fact] public void Oracle_Serialization() => Assert.Equal(DbErrorCategory.SerializationFailure, Oracle().ClassifyException(new NumberedDbException(8177)));
    [Fact] public void Oracle_Constraint() => Assert.Equal(DbErrorCategory.ConstraintViolation, Oracle().ClassifyException(new NumberedDbException(1)));

    // ---------- Sqlite ----------
    [Fact] public void Sqlite_ReadOnly() => Assert.Equal(DbErrorCategory.ReadOnlyViolation, Sqlite().ClassifyException(new NumberedDbException(8)));
    [Fact] public void Sqlite_Constraint_ByCode19() => Assert.Equal(DbErrorCategory.ConstraintViolation, Sqlite().ClassifyException(new NumberedDbException(19)));
    [Fact] public void Sqlite_Constraint_ByCode1555() => Assert.Equal(DbErrorCategory.ConstraintViolation, Sqlite().ClassifyException(new NumberedDbException(1555)));
    [Fact] public void Sqlite_Constraint_ByExtendedCode() => Assert.Equal(DbErrorCategory.ConstraintViolation, Sqlite().ClassifyException(new NumberedDbException(2067)));
    // Extended result code 787 (SQLITE_CONSTRAINT_FOREIGNKEY = (19 | (3<<8))) — low byte masking.
    [Fact] public void Sqlite_Constraint_ByMaskedExtendedCode() => Assert.Equal(DbErrorCategory.ConstraintViolation, Sqlite().ClassifyException(new NumberedDbException(787)));

    // ---------- DuckDB ----------
    [Fact] public void DuckDb_Constraint_BySqlState() => Assert.Equal(DbErrorCategory.ConstraintViolation, DuckDb().ClassifyException(new SqlStateDbException("23505")));
    [Fact] public void DuckDb_Constraint_ByMessage() => Assert.Equal(DbErrorCategory.ConstraintViolation, DuckDb().ClassifyException(new MessageOnlyDbException("Constraint Error: duplicate key")));
    [Fact] public void DuckDb_Serialization_ByMessage() => Assert.Equal(DbErrorCategory.SerializationFailure, DuckDb().ClassifyException(new MessageOnlyDbException("Conflict on update")));

    // ---------- Firebird ----------
    [Fact] public void Firebird_Serialization_BySqlState() => Assert.Equal(DbErrorCategory.SerializationFailure, Firebird().ClassifyException(new SqlStateDbException("40001")));
    [Fact] public void Firebird_Serialization_ByMessage() => Assert.Equal(DbErrorCategory.SerializationFailure, Firebird().ClassifyException(new MessageOnlyDbException("update conflicts with concurrent update")));
    [Fact] public void Firebird_Constraint_BySqlState() => Assert.Equal(DbErrorCategory.ConstraintViolation, Firebird().ClassifyException(new SqlStateDbException("23000")));
    [Fact] public void Firebird_Constraint_ByMessage() => Assert.Equal(DbErrorCategory.ConstraintViolation, Firebird().ClassifyException(new MessageOnlyDbException("violation of PRIMARY OR UNIQUE KEY constraint")));

    // ---------- Db2 ----------
    [Fact] public void Db2_Serialization() => Assert.Equal(DbErrorCategory.SerializationFailure, Db2().ClassifyException(new SqlStateDbException("40001")));
    [Fact] public void Db2_Constraint() => Assert.Equal(DbErrorCategory.ConstraintViolation, Db2().ClassifyException(new SqlStateDbException("23505")));

    // ---------- Snowflake ----------
    [Fact] public void Snowflake_Constraint_BySqlState() => Assert.Equal(DbErrorCategory.ConstraintViolation, Snowflake().ClassifyException(new SqlStateDbException("23502")));
    [Fact] public void Snowflake_Constraint_ByMessage() => Assert.Equal(DbErrorCategory.ConstraintViolation, Snowflake().ClassifyException(new MessageOnlyDbException("NULL result in a non-nullable column")));

    // ---------- Generic message-based fallback (no provider-specific match) ----------
    [Fact] public void Fallback_Deadlock_ByMessage() => Assert.Equal(DbErrorCategory.Deadlock, SqlServer().ClassifyException(new MessageOnlyDbException("a deadlock was detected")));
    [Fact] public void Fallback_Unknown_ForUnrelatedMessage() => Assert.Equal(DbErrorCategory.Unknown, SqlServer().ClassifyException(new MessageOnlyDbException("something else entirely")));

    // ---------- Spanner ----------
    // Live-verified: Spanner's NotNull/Check violation messages ("... must not be NULL in table
    // ...", "Check constraint `t`.`c` is violated for key (...)") don't contain any of
    // ClassifyException's generic fallback keywords ("constraint", "unique ", "foreign key",
    // "not-null", "violates" — note "is violated" != "violates"), so without a dedicated
    // TryClassifyProviderException override this fell through to Unknown even after
    // IsNotNullViolation/IsCheckConstraintViolation were fixed to recognize these messages.
    [Fact] public void Spanner_NotNull_ClassifiesAsConstraintViolation() =>
        Assert.Equal(DbErrorCategory.ConstraintViolation, Spanner().ClassifyException(new MessageOnlyDbException("P0001: name must not be NULL in table test_table.")));
    [Fact] public void Spanner_Check_ClassifiesAsConstraintViolation() =>
        Assert.Equal(DbErrorCategory.ConstraintViolation, Spanner().ClassifyException(new MessageOnlyDbException("P0001: Check constraint `test_table`.`chk_value_positive` is violated for key (1)")));
    [Fact] public void Spanner_Unique_ClassifiesAsConstraintViolation() =>
        Assert.Equal(DbErrorCategory.ConstraintViolation, Spanner().ClassifyException(new SqlStateDbException("23505")));
}
