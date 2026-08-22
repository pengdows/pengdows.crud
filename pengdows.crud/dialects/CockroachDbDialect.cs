// =============================================================================
// FILE: CockroachDbDialect.cs
// PURPOSE: CockroachDB specific dialect implementation.
//
// AI SUMMARY:
// - Inherits from PostgreSqlDialect for high compatibility.
// - Supports CockroachDB's distributed SQL features.
// - Identifies itself via the "Cockroach" string in the version information.
// - Enables native UPSERT support and distributed transaction tuning.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.dialects;

/// <summary>
/// CockroachDB dialect inheriting from PostgreSQL for distributed SQL compatibility.
/// </summary>
internal class CockroachDbDialect : PostgreSqlDialect
{
    internal CockroachDbDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger, SupportedDatabase.CockroachDb)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.CockroachDb;

    // CockroachDB only supports SERIALIZABLE isolation; READ COMMITTED is not available.
    public override IsolationLevel ReadCommittedCompatibleIsolationLevel => IsolationLevel.Serializable;

    // CockroachDB supports native UPSERT which is more efficient than ON CONFLICT
    // in some distributed scenarios, though it also fully supports ON CONFLICT.

    public override string GetBaseSessionSettings()
    {
        return $"{base.GetBaseSessionSettings()}\nSET client_encoding = 'UTF8';\nSET lock_timeout = '30s';";
    }

    /// <summary>
    /// CockroachDB's "SELECT version()" banner (e.g. real output captured live:
    /// "CockroachDB CCL v23.1.30 (x86_64-pc-linux-gnu, built 2024/12/09 17:37:15, go1.19.13)")
    /// contains no "PostgreSQL" token, so the inherited <see cref="PostgreSqlDialect.ParseVersion"/>
    /// override never activates its gcc-collision fix and falls through to the base
    /// <see cref="SqlDialect.ParseVersion"/>, which takes the LAST dotted-number match in the
    /// string — the Go toolchain version (e.g. "1.19.13") instead of the real product version
    /// (e.g. "23.1.30"), silently disabling every IsVersionAtLeast()-gated capability check.
    /// Extract the version that immediately follows "CockroachDB ... v" instead.
    /// </summary>
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
}
