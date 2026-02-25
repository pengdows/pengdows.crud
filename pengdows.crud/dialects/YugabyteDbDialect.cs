// =============================================================================
// FILE: YugabyteDbDialect.cs
// PURPOSE: YugabyteDB specific dialect implementation.
//
// AI SUMMARY:
// - Inherits from PostgreSqlDialect for high compatibility.
// - Supports YugabyteDB's distributed PostgreSQL (YSQL).
// - Identifies itself via the "YB" string in the version information.
// - Enables potential optimizations for distributed transaction retries.
// =============================================================================

using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.dialects;

/// <summary>
/// YugabyteDB dialect inheriting from PostgreSQL for distributed SQL compatibility.
/// </summary>
internal class YugabyteDbDialect : PostgreSqlDialect
{
    internal YugabyteDbDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger, SupportedDatabase.YugabyteDb)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.YugabyteDb;

    // YugabyteDB (YSQL) is built on PostgreSQL 11+ and supports most PG features.
    // It has specific performance characteristics for distributed primary keys.

        // Disable prepared statements for YugabyteDB to avoid potential "Connection is not open" issues
        // or other distributed SQL planning quirks.
        public override bool PrepareStatements => false;
    
        public override string GetBaseSessionSettings()
        {
            return $"{base.GetBaseSessionSettings()}\nSET client_encoding = 'UTF8';\nSET lock_timeout = '30s';";
        }
    }
    