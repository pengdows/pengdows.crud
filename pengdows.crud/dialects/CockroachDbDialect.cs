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

using System.Collections.Generic;
using System.Data;
using System.Data.Common;
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

    /// <inheritdoc/>
    protected override IEnumerable<(string Key, string Value)> GetAdditionalStartupOptions(bool readOnly)
    {
        yield return ("client_encoding", "UTF8");
        yield return ("lock_timeout", "30s");
    }
}
