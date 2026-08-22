// =============================================================================
// FILE: Db2Dialect.cs
// PURPOSE: IBM Db2 for Linux/Unix/Windows (Db2 LUW) specific dialect implementation.
//
// AI SUMMARY:
// - Supports Db2 LUW 11.1+ (native BOOLEAN, GENERATE_UUID(), FINAL TABLE clause).
// - Key features:
//   * MERGE statement for upserts (ANSI syntax, base RenderMergeSource default applies)
//   * Parameter marker: @ (named parameters; driver also supports positional ?)
//   * Identifier quoting: "name" (ANSI double quotes, no session setting required)
//   * Generated keys via SELECT ... FROM FINAL TABLE (INSERT ...) — single round trip
//   * Paging via OFFSET n ROWS FETCH NEXT m ROWS ONLY (base SupportsOffsetFetch default)
// - GUIDs stored as CHAR(36) strings (client-generated, no need for GENERATE_UUID()).
// - Phase 1 implementation: SQL-generation correctness only, validated via fakeDb.
//   Real-driver specifics (exact FINAL TABLE/MERGE acceptance, DB2Exception SQLCODE/
//   SQLSTATE property shape, isolation level enum mapping) are verified in Phase 2
//   against a live ibmcom/db2 Docker container.
// =============================================================================

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.dialects;

/// <summary>
/// IBM Db2 for Linux/Unix/Windows (Db2 LUW) dialect.
/// </summary>
/// <remarks>
/// <para>
/// <strong>UPSERT:</strong> Uses ANSI MERGE statement (base <see cref="SqlDialect.RenderMergeSource"/>
/// default — <c>USING (VALUES (...)) AS s (...)</c> — is valid Db2 LUW syntax).
/// </para>
/// <para>
/// <strong>Generated keys:</strong> Uses <c>SELECT ... FROM FINAL TABLE (INSERT ...)</c>,
/// IBM's recommended single-round-trip approach for retrieving identity values.
/// </para>
/// <para>
/// <strong>Parameters:</strong> Uses <c>@name</c> named parameters.
/// </para>
/// </remarks>
internal sealed class Db2Dialect : SqlDialect
{
    internal Db2Dialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.Db2;
    public override string ParameterMarker => "@";
    public override bool SupportsNamedParameters => true;

    // Db2 stores GUIDs as CHAR(36) strings — IDs are generated client-side, so there's
    // no need for Db2's GENERATE_UUID()/GENERATE_UUID_BINARY() server-side functions.
    protected override GuidStorageFormat GuidFormat => GuidStorageFormat.String;

    // IBM.Data.Db2's DB2Parameter.DbType setter throws ArgumentException ("No mapping exists
    // from DbType Guid to a known DB2Type") the instant DbType.Guid is assigned — before
    // GuidFormat's ApplyGuidFormat conversion ever runs. Remap here (same pattern as Oracle)
    // so the parameter is created with DbType.String from the start.
    protected override DbType RemapDbType(DbType type) => type switch
    {
        DbType.Guid => DbType.String,
        _ => type
    };

    public override DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
    {
        // Db2's TIMESTAMP column (this config has no offset-aware type — see
        // TypeHydrationTableCreator/TestTableCreator's Db2 DDL) rejects a DateTimeOffset value
        // outright ("CLI0114E Datetime field overflow"). Coerce to a plain UTC DateTime, matching
        // Firebird/MySQL/Snowflake's handling of the same limitation. This applies regardless of
        // whether the value is null — Db2's driver rejects DbType.DateTimeOffset unconditionally,
        // so a null nullable DateTimeOffset must be remapped too, not just a populated one.
        if (type == DbType.DateTimeOffset)
        {
            object coerced = value is DateTimeOffset dto
                ? DateTime.SpecifyKind(dto.UtcDateTime, DateTimeKind.Unspecified)
                : DBNull.Value;
            return base.CreateDbParameter<object?>(name, DbType.DateTime, coerced);
        }

        return base.CreateDbParameter(name, type, value);
    }

    // Db2 LUW supports ANSI MERGE regardless of detected version — matches Oracle's
    // pattern of not gating this behind MaxSupportedStandard/IsInitialized.
    public override bool SupportsMerge => true;

    public override bool SupportsSavepoints => true;

    // Db2 LUW stored procedures are invoked via SQL-standard CALL syntax — same wrapping style
    // as MySQL/MariaDB (CallProcWrappingStrategy's own doc comment already names Db2 as an
    // intended consumer). Result sets are returned via a cursor declared WITH RETURN TO CALLER
    // inside the procedure body, which the CALL statement's caller consumes like an ordinary
    // query result set — no special SQL is needed on the calling side for that part.
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.Call;

    // Db2 LUW rejects a bare "SAVEPOINT name" (SQL0104N, expects "on rollback retain cursors")
    // — confirmed against a live ibmcom/db2 container during Phase 2 testbed validation.
    // ROLLBACK TO SAVEPOINT does not need the extra clause and matches the base default.
    public override string GetSavepointSql(string name)
    {
        return $"SAVEPOINT {WrapObjectName(name)} ON ROLLBACK RETAIN CURSORS";
    }

    // These three special registers are session-level state that survives transaction rollback
    // (their SET statements are not transaction-controlled) and can silently change the meaning
    // of subsequent SQL for whichever caller borrows a pooled connection next:
    //   - CURRENT ISOLATION: overrides the package/dynamic-SQL isolation level a prior borrower
    //     may have set via SET CURRENT ISOLATION (independent of IsolationResolver's own
    //     transaction-level mapping — this resets the connection-level override, not the mapping).
    //   - CURRENT TEMPORAL SYSTEM_TIME / BUSINESS_TIME: a non-null value implicitly rewrites
    //     SELECT (and, for BUSINESS_TIME, UPDATE/DELETE) against temporal tables to an as-of-time
    //     view. Default is NULL; a prior borrower could have left either set.
    // Verified live against Db2 LUW 11.5.8.0 that all three execute successfully via
    // ExecuteNonQuery, both individually and batched as one semicolon-separated statement.
    public override string GetBaseSessionSettings()
    {
        return "SET CURRENT ISOLATION RESET; " +
               "SET CURRENT TEMPORAL SYSTEM_TIME = NULL; " +
               "SET CURRENT TEMPORAL BUSINESS_TIME = NULL;";
    }

    public override bool SupportsIdentityColumns => true;
    public override bool SupportsInsertReturning => true;

    // Db2's generated-key retrieval wraps the ENTIRE insert statement:
    //   SELECT "Id" FROM FINAL TABLE (INSERT INTO t (...) VALUES (...))
    // rather than appending a trailing RETURNING/OUTPUT clause like other dialects.
    public override bool WrapsInsertStatementForReturning => true;

    public override string RenderInsertReturningPrefix(string idColumnWrapped)
    {
        return $"SELECT {idColumnWrapped} FROM FINAL TABLE (";
    }

    public override GeneratedKeyPlan GetGeneratedKeyPlan()
    {
        return GeneratedKeyPlan.Returning;
    }

    // Base RenderMergeSource's default hardcodes the incoming-row alias as "s" —
    // match that here so upsert SET/UPDATE fragments referencing the incoming row agree.
    public override string UpsertIncomingColumn(string columnName)
    {
        return $"{WrapObjectName("s")}.{WrapObjectName(columnName)}";
    }

    public override string GetVersionQuery()
    {
        return "SELECT service_level FROM TABLE (SYSPROC.ENV_GET_INST_INFO()) AS INSTANCEINFO";
    }
}
