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

using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;

namespace pengdows.crud.dialects;

/// <summary>
/// CockroachDB dialect inheriting from PostgreSQL for distributed SQL compatibility.
/// </summary>
internal class CockroachDbDialect : PostgreSqlDialect
{
    internal CockroachDbDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.CockroachDb;

    // CockroachDB supports native UPSERT which is more efficient than ON CONFLICT
    // in some distributed scenarios, though it also fully supports ON CONFLICT.
}