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

    // Db2 LUW supports ANSI MERGE regardless of detected version — matches Oracle's
    // pattern of not gating this behind MaxSupportedStandard/IsInitialized.
    public override bool SupportsMerge => true;

    public override bool SupportsSavepoints => true;

    // Db2 LUW rejects a bare "SAVEPOINT name" (SQL0104N, expects "on rollback retain cursors")
    // — confirmed against a live ibmcom/db2 container during Phase 2 testbed validation.
    // ROLLBACK TO SAVEPOINT does not need the extra clause and matches the base default.
    public override string GetSavepointSql(string name)
    {
        return $"SAVEPOINT {WrapObjectName(name)} ON ROLLBACK RETAIN CURSORS";
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
