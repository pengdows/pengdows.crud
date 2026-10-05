// =============================================================================
// FILE: CockroachDbDialect.cs
// PURPOSE: CockroachDB specific dialect implementation.
//
// AI SUMMARY:
// - Inherits from PostgreSqlDialect for high compatibility.
// - Supports CockroachDB's distributed SQL features.
// - Identifies itself via the "Cockroach" string in the version information.
// - Upserts use the inherited INSERT ... ON CONFLICT path (MERGE is disabled for CockroachDB);
//   adds client_encoding/lock_timeout session settings and startup options.
// =============================================================================

using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.exceptions.translators;
using pengdows.crud.infrastructure;

namespace pengdows.crud.dialects;

/// <summary>
/// CockroachDB dialect inheriting from PostgreSQL for distributed SQL compatibility.
/// </summary>
internal class CockroachDbDialect : PostgreSqlDialect
{
    // Confirmed live (CockroachDB 25.1, Npgsql 9): a prepared multi-statement command fails with
    // "34000: unknown portal" on execute; unprepared it runs.
    internal override bool PreparesMultiStatementCommands => false;

    // Confirmed live (CockroachDB 25.1): a text parameter assigns to an ENUM column, while an
    // untyped one in a VALUES list (the batch update's source) fails with "could not determine data
    // type of placeholder".
    internal override bool SendsEnumParametersUntyped => false;

    internal CockroachDbDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger, SupportedDatabase.CockroachDb)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.CockroachDb;

    // CockroachDB supports GENERATED ALWAYS AS IDENTITY DDL but not the OVERRIDING SYSTEM VALUE
    // INSERT clause (cockroachdb/cockroach#68201); emitting it is a syntax error.
    internal override bool SupportsOverridingSystemValue => false;

    // CockroachDB only supports SERIALIZABLE isolation; READ COMMITTED is not available.
    public override IsolationLevel ReadCommittedCompatibleIsolationLevel => IsolationLevel.Serializable;

    // CockroachDB supports native UPSERT which is more efficient than ON CONFLICT
    // in some distributed scenarios, though it also fully supports ON CONFLICT.

    public override string GetBaseSessionSettings()
    {
        return base.GetBaseSessionSettings() + DistributedSessionSettings;
    }

    public override Version? ParseVersion(string versionString)
    {
        if (string.IsNullOrWhiteSpace(versionString))
        {
            return null;
        }

        var match = Regex.Match(versionString, @"CockroachDB\b.*?\bv(?<version>\d+(?:\.\d+){1,3})",
            RegexOptions.IgnoreCase);
        if (match.Success && Version.TryParse(match.Groups["version"].Value, out var version))
        {
            return version;
        }

        return null;
    }

    /// <inheritdoc/>
    protected override IEnumerable<(string Key, string Value)> GetAdditionalStartupOptions(bool readOnly)
    {
        yield return ("client_encoding", "UTF8");
        yield return ("lock_timeout", "30s");
    }

    // Isolation mapping (DEC-010; was IsolationResolver's per-database switch, same names as 3.0).
    internal override HashSet<IsolationLevel> GetSupportedIsolationLevels(bool allowSnapshotIsolation) =>
        new HashSet<IsolationLevel>
        {
            IsolationLevel.Serializable
        };

    internal override Dictionary<IsolationProfile, IsolationLevel> GetIsolationProfileMapping(bool allowSnapshotIsolation) =>
        new Dictionary<IsolationProfile, IsolationLevel>
        {
            [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.Serializable,
            [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable,
            [IsolationProfile.FastWithRisks] = IsolationLevel.Serializable
        };

    // REV-039: CockroachDB shares PostgreSQL's type mappings and Npgsql value types, but not
    // hstore (CockroachDB has no hstore type).
    internal static DatabaseTraits CreateCockroachDbTraits() =>
        new(SupportedDatabase.CockroachDb, PostgreSqlFamilyExceptionTranslator)
        {
            SpatialFormat = SpatialWireFormat.ExtendedWkb,
            IntervalFormat = IntervalWireFormat.Iso8601,
            BindsNpgsqlValueTypes = true,
            RegisterTypeMappings = registry =>
                RegisterPostgreSqlFamilyTypeMappings(registry, SupportedDatabase.CockroachDb)
        };
}
