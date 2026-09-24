// =============================================================================
// FILE: SpannerDialect.cs
// PURPOSE: Google Cloud Spanner dialect, connected via its PostgreSQL interface.
//
// AI SUMMARY:
// - Backported from pengdows.crud 3.0 (same live-verification trail against a real Spanner
//   Omni + PGAdapter instance applies) to this non-breaking 2.0.6 patch line.
// - Isolation-level data lives in IsolationResolver.cs's central switch instead of a
//   dialect-owned override (2.0.6 predates that 3.0 refactor).
// - Advisory exception-category classification lives in SqlDialect.cs's private
//   TryClassifyProviderException switch as a new SupportedDatabase.Spanner case, reusing the
//   same four IsXxxViolation predicates below via virtual dispatch (same pattern 3.0's dialect
//   itself uses).
// - Spanner-specific constraint-violation message matching (NotNull/Check/delete-side ForeignKey
//   all surface as a generic SqlState "P0001" on Spanner, not the real ANSI codes) is
//   ALSO added to the shared PostgresExceptionTranslator (which already routes Spanner here,
//   same as CockroachDb/YugabyteDb) - 2.0.6's translators don't delegate to the dialect the way
//   3.0's do, so this logic has to be duplicated in both places rather than written once. Kept
//   carefully in sync: both copies use the identical message substrings.
// - Also covers Spanner Omni PostgreSQL databases reached through PGAdapter.
// - Verified live against a real Spanner Omni + PGAdapter instance: `ROW_NUMBER() OVER (...)`
//   fails with "P0001: Statements with WINDOW clauses are not supported". The inherited,
//   version-gated PostgreSqlDialect.SupportsWindowFunctions wrongly assumes this capability
//   transfers the way most of Spanner's PostgreSQL-interface SQL surface does.
// - PostgreSqlDialect's optimized batch-update SQL ("UPDATE t SET ... FROM (VALUES (@b0, ...))
//   AS s(...) WHERE t.pk = s.pk") fails against real Spanner with "42883: operator does not
//   exist: bigint = text" — Spanner's query planner doesn't infer the VALUES-derived table's
//   column types from the joined column the way real PostgreSQL does. Falls back to one
//   BuildUpdate container per entity instead.
// - Spanner's PostgreSQL interface (PGAdapter) doesn't register a "uuid" type Npgsql
//   recognizes — verified live: binding DbType.Guid with the inherited PostgreSqlDialect
//   PassThrough behavior fails at execution time with "The NpgsqlDbType 'Uuid' isn't present
//   in your database," even though the DDL text "UUID" is accepted at CREATE TABLE time.
//   Store as a hyphenated string instead, matching every other dialect without genuine
//   native Guid/UUID wire support (Sqlite/Oracle/Snowflake/Db2/DuckDB/Firebird).
// - Spanner returns SqlState "P0001" (a generic raise-exception code) for EVERY constraint
//   violation except unique, not the ANSI class-23 codes real PostgreSQL uses — verified live
//   against a real Spanner Omni + PGAdapter instance. Message-pattern matching is the only
//   reliable signal for NotNull/Check/delete-side ForeignKey, the same approach Sqlite/Firebird
//   already use for their own non-standard-SqlState shapes.
// - Foreign key violations are inconsistent between INSERT and DELETE on Spanner, verified live:
//   an INSERT referencing a nonexistent parent row genuinely does use the real ANSI SqlState
//   "23503" (inherited PostgreSqlDialect.IsForeignKeyViolation already classifies it correctly —
//   no override needed for that shape). But a DELETE blocked by a child row instead returns the
//   generic "P0001" code with a message pattern - the inherited SqlState-only check misses this
//   shape entirely.
// - SupportsOverridingSystemValue needs no override here - unlike 3.0 (where this is a dialect
//   capability flag ISqlDialect exposes), 2.0.6's TableGateway.Upsert.cs decides whether to emit
//   "OVERRIDING SYSTEM VALUE" via a direct product-type switch matching only PostgreSql/
//   AuroraPostgreSql - Spanner, being a distinct SupportedDatabase value, is already correctly
//   excluded by that switch with no code changes needed.
// =============================================================================

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.dialects;

/// <summary>Dialect for Cloud Spanner databases using the PostgreSQL interface.</summary>
/// <remarks>Also covers Spanner Omni PostgreSQL databases reached through PGAdapter.</remarks>
internal sealed class SpannerDialect : PostgreSqlDialect
{
    internal SpannerDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger, SupportedDatabase.Spanner) { }

    public override SupportedDatabase DatabaseType => SupportedDatabase.Spanner;
    public override bool SupportsMerge => false;
    public override bool SupportsSetValuedParameters => false;

    // Verified live against a real Spanner Omni + PGAdapter instance: `ROW_NUMBER() OVER (...)`
    // fails with "P0001: Statements with WINDOW clauses are not supported". PostgreSqlDialect's
    // inherited, version-gated `true` wrongly assumes this capability transfers the way most of
    // Spanner's PostgreSQL-interface SQL surface does.
    public override bool SupportsWindowFunctions => false;

    // PostgreSqlDialect's optimized batch-update SQL ("UPDATE t SET ... FROM (VALUES (@b0, ...))
    // AS s(...) WHERE t.pk = s.pk") fails against real Spanner with "42883: operator does not
    // exist: bigint = text" — Spanner's query planner doesn't infer the VALUES-derived table's
    // column types from the joined column the way real PostgreSQL does, so the untyped VALUES
    // parameter stays "text" and the comparison to a typed key column is rejected outright.
    // Falls back to one BuildUpdate container per entity instead (TableGateway.Batch.cs already
    // has this fallback for every other dialect where this flag is false).
    public override bool SupportsBatchUpdate => false;
    public override bool SupportsSavepoints => false;
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.None;
    public override IsolationLevel ReadCommittedCompatibleIsolationLevel => IsolationLevel.Serializable;

    // Spanner's PostgreSQL interface (PGAdapter) doesn't register a "uuid" type Npgsql
    // recognizes — verified live: binding DbType.Guid with the inherited PostgreSqlDialect
    // PassThrough behavior fails at execution time with "The NpgsqlDbType 'Uuid' isn't present
    // in your database," even though the DDL text "UUID" is accepted at CREATE TABLE time.
    // Store as a hyphenated string instead, matching every other dialect without genuine
    // native Guid/UUID wire support (Sqlite/Oracle/Snowflake/Db2/DuckDB/Firebird).
    protected override GuidStorageFormat GuidFormat => GuidStorageFormat.String;

    // Spanner returns SqlState "P0001" (a generic raise-exception code) for NotNull/Check and
    // delete-side ForeignKey violations, not the ANSI class-23 codes real PostgreSQL uses —
    // verified live against a real Spanner Omni + PGAdapter instance. Inheriting PostgreSqlDialect's
    // pure SqlState-based checks misses those shapes; message-pattern matching is the only reliable signal, the same
    // approach Sqlite/Firebird already use for their own non-standard-SqlState shapes.
    public override bool IsNotNullViolation(DbException ex) =>
        ex.Message.Contains("must not be NULL", StringComparison.OrdinalIgnoreCase);

    // Real captured message: "P0001: Check constraint `test_table`.`chk_value_positive` is
    // violated for key (1)" — see IsNotNullViolation's comment for why message-matching is
    // necessary here at all.
    public override bool IsCheckConstraintViolation(DbException ex) =>
        ex.Message.Contains("Check constraint", StringComparison.OrdinalIgnoreCase) &&
        ex.Message.Contains("is violated", StringComparison.OrdinalIgnoreCase);

    // Foreign key violations are inconsistent between INSERT and DELETE on Spanner, verified live:
    // an INSERT referencing a nonexistent parent row genuinely does use the real ANSI SqlState
    // "23503" (inherited PostgreSqlDialect.IsForeignKeyViolation already classifies it correctly —
    // no override needed for that shape). But a DELETE blocked by a child row instead returns the
    // generic "P0001" code with message "Foreign key constraint violation when deleting or
    // updating referenced row(s): referencing row(s) found in table `...`." — the inherited
    // SqlState-only check misses this shape entirely, surfacing as a generic
    // DatabaseOperationException instead of ForeignKeyViolationException. Keep the inherited
    // SqlState check (covers INSERT) and add the message pattern for DELETE.
    public override bool IsForeignKeyViolation(DbException ex) =>
        base.IsForeignKeyViolation(ex) ||
        (ex.Message.Contains("Foreign key constraint violation", StringComparison.OrdinalIgnoreCase) &&
         ex.Message.Contains("referenced row", StringComparison.OrdinalIgnoreCase));

    // Isolation-level data (GetSupportedIsolationLevels/GetIsolationProfileMapping on 3.0) lives
    // in IsolationResolver.cs's SupportedDatabase.Spanner cases on this branch instead.

    public override string GetBaseSessionSettings() => string.Empty;
}
