// =============================================================================
// FILE: FlatFileDialect.cs
// PURPOSE: Dialect for pengdows.flatfile — a file-backed ADO.NET provider over
//          CSV/TSV/pipe/delimited/fixed-width/NDJSON files.
//
// STATUS: Partial. Only the properties below reflect a deliberate, verified decision
// against pengdows.flatfile's actual behavior (see citations on each). Everything not
// overridden here still falls through to SqlDialect's generic defaults and has NOT been
// verified against pengdows.flatfile — in particular isolation levels/profiles (its
// FlatFileTransaction is file-snapshot/undo-journal rollback, explicitly not real
// concurrent-connection isolation or MVCC per its own README), generated-key/identity
// plan (flatfile has no autoincrement/sequence/RETURNING concept at all), session
// settings, and CoerceConnectionMode/DbMode-Best selection. Decide those from real
// research against pengdows.flatfile's source, not by copying another embedded dialect's
// assumptions — see CLAUDE.md's "Adding a New Database" checklist and its SAP HANA
// callout for the same caution.
// =============================================================================

using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// Dialect for <c>pengdows.flatfile</c>. See the file-level STATUS remark above — this is a
/// deliberately partial dialect, not a finished one.
/// </summary>
internal class FlatFileDialect : SqlDialect
{
    internal FlatFileDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.FlatFile;

    /// <summary>
    /// Confirmed via <c>pengdows.flatfile/FlatFileConnectionStringBuilder.cs</c>'s own
    /// <c>KeyApplicationName</c> constant and <c>ApplicationName</c> property — a real, recognized
    /// keyword, not a guess. Without this set, reader/writer connection strings would be
    /// identical and collapse into one shared pool (the same bug class Access's
    /// <c>ReadOnlyPoolDiscriminatorSettingName</c> fix addressed, but avoided here entirely since
    /// FlatFile's own driver already supports the standard mechanism).
    /// </summary>
    public override string? ApplicationNameSettingName => "applicationName";

    /// <summary>
    /// pengdows.flatfile is a custom, in-process, file-based provider with no network handshake
    /// and no real connection pool to configure — architecturally identical to why
    /// <see cref="DuckDbDialect"/>'s equivalent is <see langword="false"/>.
    /// </summary>
    public override bool SupportsExternalPooling => false;

    /// <summary>
    /// Confirmed via <c>pengdows.flatfile/FlatFileConnectionStringBuilder.cs</c>'s own
    /// <c>KeyReadOnly</c> constant and <c>ReadOnly</c> property
    /// (<c>SetOrRemove(KeyReadOnly, value ? "true" : null)</c>) — a real, hard-enforced keyword:
    /// any mutating statement (DML/DDL) against a connection carrying this is rejected
    /// immediately by the provider itself, not just by pengdows.crud's own pre-flight checks.
    /// </summary>
    public override string? GetReadOnlyConnectionParameter() => "readonly=true";

    /// <summary>
    /// pengdows.flatfile's own SQL grammar supports only positional <c>?</c> parameters — its
    /// README states this explicitly ("Positional parameters only (?) — no named (@name)
    /// parameters"). Verified against the engine's real syntax, not assumed.
    /// </summary>
    public override bool SupportsNamedParameters => false;

    /// <summary>
    /// pengdows.flatfile has no stored-procedure/trigger/control-flow support at all (confirmed:
    /// its README lists this under "Not supported"). <see cref="ProcWrappingStyle.None"/> is the
    /// base default already, but this override documents that the value was verified, not left
    /// unexamined.
    /// </summary>
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.None;

    /// <summary>
    /// pengdows.flatfile is an embedded, per-directory file engine with no server process — the
    /// same classification as SQLite/DuckDB, not a client-server RDBMS.
    /// </summary>
    public override bool IsClientServerDatabase => false;

    /// <summary>
    /// Verified: pengdows.flatfile's SQL parser (<c>pengdows.sql/SqlParser.cs</c>) genuinely
    /// parses an <c>IF EXISTS</c> clause on <c>DROP TABLE</c>/<c>DROP VIEW</c>/<c>DROP INDEX</c>
    /// (see its <c>SqlAst.cs</c> <c>IfExists</c> properties), so the base <c>true</c> default is
    /// confirmed correct here rather than inherited blind.
    /// </summary>
    public override bool SupportsDropTableIfExists => true;

    /// <summary>
    /// <c>SELECT version()</c> — the base <see cref="SqlDialect.GetDatabaseVersionAsync"/> fallback
    /// when a dialect doesn't override <see cref="GetVersionQuery"/> — is not a SQL-standard
    /// function; it happens to work for Postgres-family and DuckDB engines because they each
    /// implement it as a real builtin (see <see cref="DuckDbDialect.GetVersionQuery"/>), not because
    /// any standard guarantees it. pengdows.flatfile's SQL grammar has no <c>version()</c> function
    /// at all, so that guess would simply fail against a real flatfile connection. Its ADO.NET
    /// provider instead exposes version the idiomatic ADO.NET way — <c>FlatFileConnection.ServerVersion</c>
    /// (currently hardcoded to <c>"1.0"</c>) — so read that directly instead of executing SQL.
    /// </summary>
    public override Task<string> GetDatabaseVersionAsync(ITrackedConnection connection)
    {
        return Task.FromResult(connection.ServerVersion);
    }
}
