using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.isolation;

namespace pengdows.crud.dialects;

/// <summary>Dialect for Cloud Spanner databases using the PostgreSQL interface.</summary>
/// <remarks>Also covers Spanner Omni PostgreSQL databases reached through PGAdapter.</remarks>
internal sealed class SpannerDialect : PostgreSqlDialect
{
    internal SpannerDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger, SupportedDatabase.Spanner) { }

    public override SupportedDatabase DatabaseType => SupportedDatabase.Spanner;
    public override bool SupportsMerge => false;
    public override bool SupportsOverridingSystemValue => false;
    public override bool SupportsSetValuedParameters => false;

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

    // Spanner returns SqlState "P0001" (a generic raise-exception code) for EVERY constraint
    // violation, not the ANSI class-23 codes real PostgreSQL uses — verified live against a real
    // Spanner Omni + PGAdapter instance. Inheriting PostgreSqlDialect's pure SqlState-based checks
    // is therefore useless here; message-pattern matching is the only reliable signal, the same
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

    // ClassifyException's base-class generic message fallback (SqlDialect.cs) only recognizes a
    // constraint violation via keywords like "constraint"/"violates" — Spanner's real NotNull
    // message ("... must not be NULL in table ...") contains neither, so without this override it
    // silently classified as Unknown even after IsNotNullViolation above was fixed to recognize
    // it. Reuses the same four predicates IDbExceptionTranslator.Translate delegates to (item 22)
    // so this category-level classifier can't drift from the constraint-kind classifier.
    protected override bool TryClassifyProviderException(DbException ex, out DbErrorCategory category)
    {
        if (IsUniqueViolation(ex) || IsForeignKeyViolation(ex) || IsNotNullViolation(ex) || IsCheckConstraintViolation(ex))
        {
            category = DbErrorCategory.ConstraintViolation;
            return true;
        }

        return base.TryClassifyProviderException(ex, out category);
    }

    internal override HashSet<IsolationLevel> GetSupportedIsolationLevels(bool allowSnapshotIsolation) => new()
    {
        IsolationLevel.RepeatableRead,
        IsolationLevel.Serializable
    };

    internal override Dictionary<IsolationProfile, IsolationLevel> GetIsolationProfileMapping(bool allowSnapshotIsolation) => new()
    {
        [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.RepeatableRead,
        [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable,
        [IsolationProfile.FastWithRisks] = IsolationLevel.RepeatableRead
    };

    public override string GetBaseSessionSettings() => string.Empty;
}
