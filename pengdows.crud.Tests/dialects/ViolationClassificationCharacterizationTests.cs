using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Characterization tests for SqlDialect.IsUniqueViolation/IsForeignKeyViolation/
/// IsNotNullViolation/IsCheckConstraintViolation, written BEFORE converting these four
/// switch(DatabaseType) blocks into per-dialect virtual overrides (CLAUDE.md "Adding a New
/// Database" checklist items 2-5). Every case explicitly listed in each switch, for every
/// dialect, is asserted here so the refactor is provably behavior-preserving: green before the
/// refactor (against the switch-based implementation) and green after (against the per-dialect
/// override implementation), with no case list changing in between.
/// </summary>
public class ViolationClassificationCharacterizationTests
{
    // Sets both a "Number" property (what TryGetProviderErrorCode's reflection-based lookup
    // checks first — matching real SqlException.Number/MySqlException.Number) and HResult (what
    // DbException.ErrorCode returns by default — matching real SqliteException.ErrorCode, which
    // SqliteDialect.IsUniqueViolation reads directly rather than through TryGetProviderErrorCode),
    // so this one double works for every dialect's error-code mechanism.
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
    private static SybaseDialect Sybase() => new(new fakeDbFactory(SupportedDatabase.SybaseASE), NullLogger.Instance);
    private static SpannerDialect Spanner() => new(new fakeDbFactory(SupportedDatabase.Spanner), NullLogger.Instance);

    // ---------- IsUniqueViolation ----------

    [Fact] public void Unique_SqlServer() => Assert.True(SqlServer().IsUniqueViolation(new NumberedDbException(2627)));
    [Fact] public void Unique_SqlServer_OtherCode() => Assert.True(SqlServer().IsUniqueViolation(new NumberedDbException(2601)));
    [Fact] public void Unique_Postgres() => Assert.True(Postgres().IsUniqueViolation(new SqlStateDbException("23505")));
    [Fact] public void Unique_MySql() => Assert.True(MySql().IsUniqueViolation(new NumberedDbException(1062)));
    [Fact] public void Unique_MySql_DupUnique() => Assert.True(MySql().IsUniqueViolation(new NumberedDbException(1169)));
    [Fact] public void Unique_Oracle() => Assert.True(Oracle().IsUniqueViolation(new NumberedDbException(1)));
    [Fact] public void Unique_Sqlite_ByCode() => Assert.True(Sqlite().IsUniqueViolation(new NumberedDbException(1555)));
    [Fact] public void Unique_Sqlite_ByMessage() => Assert.True(Sqlite().IsUniqueViolation(new MessageOnlyDbException("UNIQUE constraint failed: t.id")));
    [Fact] public void Unique_DuckDb() => Assert.True(DuckDb().IsUniqueViolation(new SqlStateDbException("23505")));
    [Fact] public void Unique_Firebird() => Assert.True(Firebird().IsUniqueViolation(new MessageOnlyDbException("violation of PRIMARY OR UNIQUE KEY constraint")));
    [Fact] public void Unique_Db2() => Assert.True(Db2().IsUniqueViolation(new SqlStateDbException("23505")));
    // See ForeignKey_Db2_ByNumericSqlCode_NoSqlState below — same numeric-SQLCODE-fallback gap.
    [Fact] public void Unique_Db2_ByNumericSqlCode_NoSqlState() => Assert.True(Db2().IsUniqueViolation(new NumberedDbException(803)));
    [Fact] public void Unique_Snowflake_AlwaysFalse() => Assert.False(Snowflake().IsUniqueViolation(new SqlStateDbException("23505")));

    // ---------- IsForeignKeyViolation ----------

    [Fact] public void ForeignKey_SqlServer() => Assert.True(SqlServer().IsForeignKeyViolation(new NumberedDbException(547, "FOREIGN KEY constraint")));
    // Architecture-cleanup regression (caught by live integration re-verification, not a unit
    // test): a real SQL Server DELETE blocked by a child row's FK says "REFERENCE constraint",
    // NOT "FOREIGN KEY constraint" — confirmed live against a real SqlServer container deleting
    // a parent row: "The DELETE statement conflicted with the REFERENCE constraint
    // "FK__test_rela__..."". Only INSERT/UPDATE-blocked-by-missing-parent uses "FOREIGN KEY
    // constraint" wording. Expected RED until IsForeignKeyViolation also matches this phrasing.
    [Fact] public void ForeignKey_SqlServer_DeleteBlockedByChild_ReferenceConstraintWording() =>
        Assert.True(SqlServer().IsForeignKeyViolation(new NumberedDbException(547,
            "The DELETE statement conflicted with the REFERENCE constraint \"FK__test_rela__test___6B24EA82\". " +
            "The conflict occurred in database \"testdb\", table \"dbo.test_related\", column 'test_table_id'.")));
    [Fact] public void ForeignKey_Postgres() => Assert.True(Postgres().IsForeignKeyViolation(new SqlStateDbException("23503")));
    [Fact] public void ForeignKey_MySql() => Assert.True(MySql().IsForeignKeyViolation(new NumberedDbException(1216)));
    [Fact] public void ForeignKey_MySql_OnDeleteRestrict() => Assert.True(MySql().IsForeignKeyViolation(new NumberedDbException(1451)));
    [Fact] public void ForeignKey_Oracle() => Assert.True(Oracle().IsForeignKeyViolation(new NumberedDbException(2291)));
    // Message-only case, on its own, would also pass via the generic base-class default (it
    // contains "foreign key" case-insensitively) — the ByCode case below is what actually proves
    // Sqlite's own error-code check specifically (errorCode == 787), not just message matching.
    [Fact] public void ForeignKey_Sqlite_ByMessage() => Assert.True(Sqlite().IsForeignKeyViolation(new MessageOnlyDbException("FOREIGN KEY constraint failed")));
    [Fact] public void ForeignKey_Sqlite_ByCode() => Assert.True(Sqlite().IsForeignKeyViolation(new NumberedDbException(787, "unrelated message")));
    [Fact] public void ForeignKey_DuckDb() => Assert.True(DuckDb().IsForeignKeyViolation(new SqlStateDbException("23503")));
    [Fact] public void ForeignKey_Db2() => Assert.True(Db2().IsForeignKeyViolation(new SqlStateDbException("23503")));
    [Fact] public void ForeignKey_Db2_DeleteRestrict() => Assert.True(Db2().IsForeignKeyViolation(new SqlStateDbException("23504")));
    // Architecture-cleanup gap: Db2ExceptionTranslator ALSO falls back to the numeric SQLCODE
    // magnitude (530/531/532) when no SqlState is available at all — IBM.Data.Db2's DB2Exception
    // often doesn't populate SqlState, per Db2ExceptionTranslator.cs's own doc comment. Expected
    // RED until Db2Dialect.IsForeignKeyViolation widens to match that same fallback.
    [Fact] public void ForeignKey_Db2_ByNumericSqlCode_NoSqlState() => Assert.True(Db2().IsForeignKeyViolation(new NumberedDbException(532)));
    [Fact] public void ForeignKey_Snowflake_AlwaysFalse() => Assert.False(Snowflake().IsForeignKeyViolation(new SqlStateDbException("23503")));
    [Fact] public void ForeignKey_Firebird_ViaDefaultMessageCheck() => Assert.True(Firebird().IsForeignKeyViolation(new MessageOnlyDbException("violation of FOREIGN KEY constraint")));

    // ---------- IsNotNullViolation ----------

    [Fact] public void NotNull_SqlServer() => Assert.True(SqlServer().IsNotNullViolation(new NumberedDbException(515)));
    [Fact] public void NotNull_Postgres() => Assert.True(Postgres().IsNotNullViolation(new SqlStateDbException("23502")));
    [Fact] public void NotNull_MySql() => Assert.True(MySql().IsNotNullViolation(new NumberedDbException(1048)));
    [Fact] public void NotNull_Oracle() => Assert.True(Oracle().IsNotNullViolation(new NumberedDbException(1400)));
    [Fact] public void NotNull_Sqlite_ByMessage() => Assert.True(Sqlite().IsNotNullViolation(new MessageOnlyDbException("NOT NULL constraint failed")));
    [Fact] public void NotNull_Sqlite_ByCode() => Assert.True(Sqlite().IsNotNullViolation(new NumberedDbException(1299, "unrelated message")));
    [Fact] public void NotNull_DuckDb() => Assert.True(DuckDb().IsNotNullViolation(new SqlStateDbException("23502")));
    [Fact] public void NotNull_Firebird() => Assert.True(Firebird().IsNotNullViolation(new MessageOnlyDbException("validation error for column X, value \"*** null ***\"")));
    [Fact] public void NotNull_Db2() => Assert.True(Db2().IsNotNullViolation(new SqlStateDbException("23502")));
    // See ForeignKey_Db2_ByNumericSqlCode_NoSqlState above — same numeric-SQLCODE-fallback gap.
    [Fact] public void NotNull_Db2_ByNumericSqlCode_NoSqlState() => Assert.True(Db2().IsNotNullViolation(new NumberedDbException(407)));
    [Fact] public void NotNull_Snowflake_ByState() => Assert.True(Snowflake().IsNotNullViolation(new SqlStateDbException("23502")));
    [Fact] public void NotNull_Snowflake_ByMessage() => Assert.True(Snowflake().IsNotNullViolation(new MessageOnlyDbException("NULL result in a non-nullable column")));
    // Verified live against a real Spanner Omni + PGAdapter instance: Spanner returns SqlState
    // "P0001" (a generic raise-exception code) for EVERY constraint violation, not the ANSI
    // class-23 codes real PostgreSQL uses — inheriting PostgreSqlDialect's pure SqlState-based
    // check is therefore useless here; message-pattern matching (like Sqlite/Firebird) is the only
    // reliable signal. Real captured message: "P0001: name must not be NULL in table test_table."
    [Fact] public void NotNull_Spanner_ByMessage() => Assert.True(Spanner().IsNotNullViolation(new MessageOnlyDbException("P0001: name must not be NULL in table test_table.")));

    // ---------- IsCheckConstraintViolation ----------

    [Fact] public void Check_SqlServer() => Assert.True(SqlServer().IsCheckConstraintViolation(new NumberedDbException(547, "CHECK constraint")));
    [Fact] public void Check_Postgres() => Assert.True(Postgres().IsCheckConstraintViolation(new SqlStateDbException("23514")));
    [Fact] public void Check_MySql() => Assert.True(MySql().IsCheckConstraintViolation(new NumberedDbException(3819)));
    [Fact] public void Check_MySql_OtherCode() => Assert.True(MySql().IsCheckConstraintViolation(new NumberedDbException(4025)));
    // Architecture-cleanup gap: MySqlExceptionTranslator ALSO recognizes a check violation by
    // message pattern ("constraint" + "failed for") when no numeric error code is present at
    // all (see MySqlTranslatorTests.MessagePattern_ConstraintFailedFor_MapsTo_CheckConstraintViolationException).
    // Expected RED until MySqlDialect.IsCheckConstraintViolation widens to match.
    [Fact] public void Check_MySql_ByMessagePattern_NoErrorCode() => Assert.True(MySql().IsCheckConstraintViolation(new MessageOnlyDbException("constraint failed for `orders`")));
    [Fact] public void Check_Oracle() => Assert.True(Oracle().IsCheckConstraintViolation(new NumberedDbException(2290)));
    [Fact] public void Check_Sqlite_ByMessage() => Assert.True(Sqlite().IsCheckConstraintViolation(new MessageOnlyDbException("CHECK constraint failed")));
    [Fact] public void Check_Sqlite_ByCode() => Assert.True(Sqlite().IsCheckConstraintViolation(new NumberedDbException(275, "unrelated message")));
    [Fact] public void Check_DuckDb() => Assert.True(DuckDb().IsCheckConstraintViolation(new SqlStateDbException("23514")));
    [Fact] public void Check_Db2() => Assert.True(Db2().IsCheckConstraintViolation(new SqlStateDbException("23513")));
    // See ForeignKey_Db2_ByNumericSqlCode_NoSqlState above — same numeric-SQLCODE-fallback gap.
    [Fact] public void Check_Db2_ByNumericSqlCode_NoSqlState() => Assert.True(Db2().IsCheckConstraintViolation(new NumberedDbException(545)));
    [Fact] public void Check_Snowflake_AlwaysFalse() => Assert.False(Snowflake().IsCheckConstraintViolation(new SqlStateDbException("23514")));
    [Fact] public void Check_Firebird_ViaDefaultMessageCheck() => Assert.True(Firebird().IsCheckConstraintViolation(new MessageOnlyDbException("check constraint failed")));
    // See NotNull_Spanner_ByMessage above — same SqlState "P0001"-for-everything gap. Real
    // captured message: "P0001: Check constraint `test_table`.`chk_value_positive` is violated
    // for key (1)".
    [Fact] public void Check_Spanner_ByMessage() => Assert.True(Spanner().IsCheckConstraintViolation(new MessageOnlyDbException("P0001: Check constraint `test_table`.`chk_value_positive` is violated for key (1)")));

    // ---------- HasSessionScopedLastIdFunction ----------

    [Fact] public void SessionScopedLastId_MySql_True() => Assert.True(MySql().HasSessionScopedLastIdFunction());
    [Fact] public void SessionScopedLastId_Sqlite_True() => Assert.True(Sqlite().HasSessionScopedLastIdFunction());
    [Fact] public void SessionScopedLastId_SqlServer_True() => Assert.True(SqlServer().HasSessionScopedLastIdFunction());
    [Fact] public void SessionScopedLastId_Sybase_True() => Assert.True(Sybase().HasSessionScopedLastIdFunction());
    [Fact] public void SessionScopedLastId_Postgres_False() => Assert.False(Postgres().HasSessionScopedLastIdFunction());
    [Fact] public void SessionScopedLastId_DuckDb_False() => Assert.False(DuckDb().HasSessionScopedLastIdFunction());
    [Fact] public void SessionScopedLastId_Oracle_DefaultFalse() => Assert.False(Oracle().HasSessionScopedLastIdFunction());

    // ---------- GetGeneratedKeyPlan ----------
    //
    // Oracle and MySql both have their OWN complete GetGeneratedKeyPlan() overrides, unrelated to
    // the base class's SupportsInsertReturning/HasSessionScopedLastIdFunction decision tree this
    // refactor touched (Oracle: always Returning, regardless of the base class's now-deleted
    // Oracle special case that claimed PrefetchSequence and was already dead code before this
    // refactor began; MySql: ReaderInsertedId or CompoundStatement depending on the driver). A
    // minimal stub dialect isolates the base class's own decision logic instead of relying on a
    // real dialect that might have its own unrelated override masking what's actually being
    // verified — exactly the trap the Oracle/MySql assumptions below fell into on the first pass.

    private sealed class StubDialect : SqlDialect
    {
        private readonly bool _supportsReturning;
        private readonly bool _hasSessionScopedLastId;

        public StubDialect(bool supportsReturning, bool hasSessionScopedLastId)
            : base(new fakeDbFactory(SupportedDatabase.Unknown), NullLogger.Instance)
        {
            _supportsReturning = supportsReturning;
            _hasSessionScopedLastId = hasSessionScopedLastId;
        }

        public override SupportedDatabase DatabaseType => SupportedDatabase.Unknown;
        public override bool SupportsInsertReturning => _supportsReturning;
        public override bool HasSessionScopedLastIdFunction() => _hasSessionScopedLastId;
    }

    [Fact]
    public void GeneratedKeyPlan_BaseDefault_SupportsReturning_UsesReturning() =>
        Assert.Equal(GeneratedKeyPlan.Returning, new StubDialect(true, false).GetGeneratedKeyPlan());

    [Fact]
    public void GeneratedKeyPlan_BaseDefault_NoReturning_HasSessionScopedId_UsesSessionScopedFunction() =>
        Assert.Equal(GeneratedKeyPlan.SessionScopedFunction, new StubDialect(false, true).GetGeneratedKeyPlan());

    [Fact]
    public void GeneratedKeyPlan_BaseDefault_NeitherReturningNorSessionScoped_UsesCorrelationToken() =>
        Assert.Equal(GeneratedKeyPlan.CorrelationToken, new StubDialect(false, false).GetGeneratedKeyPlan());

    [Fact]
    public void GeneratedKeyPlan_SqlServer_IsOutputInserted()
    {
        var sqlServer = SqlServer();
        Assert.True(sqlServer.SupportsInsertReturning);
        Assert.Equal(GeneratedKeyPlan.OutputInserted, sqlServer.GetGeneratedKeyPlan());
    }

    [Fact]
    public void GeneratedKeyPlan_Postgres_IsReturning()
    {
        var postgres = Postgres();
        Assert.True(postgres.SupportsInsertReturning);
        Assert.Equal(GeneratedKeyPlan.Returning, postgres.GetGeneratedKeyPlan());
    }

    [Fact]
    public void GeneratedKeyPlan_Oracle_IsReturning_ViaItsOwnPreexistingOverride()
    {
        // OracleDialect's own GetGeneratedKeyPlan() override already existed before this refactor
        // and always returns Returning — the base class used to also carry an unreachable
        // "Oracle special case: PrefetchSequence" branch that this refactor deleted as dead code
        // (Oracle's real override already shadowed it completely).
        Assert.Equal(GeneratedKeyPlan.Returning, Oracle().GetGeneratedKeyPlan());
    }
}
