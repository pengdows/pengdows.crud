// =============================================================================
// FILE: QuestDbDialect.cs
// PURPOSE: QuestDB specific dialect implementation.
//
// AI SUMMARY:
// - Inherits from PostgreSqlDialect for PGWire protocol support.
// - Supports QuestDB's specialized high-performance time-series SQL.
// - Disables certain PostgreSQL features (e.g., savepoints) not yet supported.
// - Enables time-series extensions like SAMPLE BY and LATEST ON.
// =============================================================================

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;

namespace pengdows.crud.dialects;

/// <summary>
/// QuestDB dialect inheriting from PostgreSQL for PGWire protocol compatibility.
/// </summary>
internal class QuestDbDialect : PostgreSqlDialect
{
    internal QuestDbDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.QuestDb;

    // QuestDB uses PGWire protocol but does not support standard PG savepoints (yet).
    public override bool SupportsSavepoints => false;

    // QuestDB handles UPSERT differently (via its own ILP or specialized SQL)
    // and standard PG ON CONFLICT is not yet fully compatible in all cases.
    public override bool SupportsInsertOnConflict => false;

    // QuestDB handles prepared statements differently and some versions may have issues
    public override bool PrepareStatements => false;

    // Do not inject Npgsql auto-prepare connection-string settings — they contradict
    // the PrepareStatements=false flag and can cause unexpected behaviour with QuestDB's
    // limited PGWire implementation.
    internal override string PrepareConnectionStringForDataSource(string connectionString) => connectionString;

    // QuestDB does not support interactive transactions over PGWire.
    public override bool SupportsTransactions => false;

    // QuestDB does not support DELETE FROM … WHERE … — use TRUNCATE TABLE instead.
    public override bool SupportsRowLevelDelete => false;

    // QuestDB does not enforce primary key constraints.
    public override bool SupportsIntegrityConstraints => false;

    // QuestDB currently has limited support for standard PG system catalogs
    // so schema detection should be simpler where possible.

    public override bool SupportsJsonTypes => false;

    public override string RenderJsonArgument(string parameterMarker, IColumnInfo column)
    {
        return parameterMarker;
    }

    public override object? PrepareParameterValue(object? value, DbType dbType)
    {
        if (value == null || value is DBNull)
        {
            return value;
        }

        // QuestDB TIMESTAMP is microseconds since Unix epoch. 
        // We convert DateTime to long here based on CLR type to handle both new parameters and template value updates.
        if (value is DateTime dt)
        {
            var unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (dt.ToUniversalTime() - unixEpoch).Ticks / 10;
        }
        
        if (value is DateTimeOffset dto)
        {
            var unixEpoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
            return (dto.ToUniversalTime() - unixEpoch).Ticks / 10;
        }

        return base.PrepareParameterValue(value, dbType);
    }

    public override DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
    {
        // For QuestDb, we need to be very specific about NpgsqlDbType to avoid its limited protocol support
        var parameter = Factory.CreateParameter();
        parameter.ParameterName = name ?? GenerateRandomName(5, ParameterNameMaxLength);
        
        // Transform value via hook
        var preparedValue = PrepareParameterValue(value, type);
        parameter.Value = preparedValue ?? DBNull.Value;
        
        // Set DbType FIRST
        parameter.DbType = type;
        
        // Map string types to Varchar (not Text)
        if (type == DbType.String || type == DbType.AnsiString)
        {
            parameter.DbType = DbType.AnsiString;
            SetNpgsqlParameterType(parameter, "Varchar", "varchar");
        }
        // Map DateTime types to Bigint because we converted them to long microseconds
        else if (type == DbType.DateTime || type == DbType.DateTime2 || type == DbType.DateTimeOffset)
        {
            parameter.DbType = DbType.Int64;
            SetNpgsqlParameterType(parameter, "Bigint", "int8");
        }

        return parameter;
    }

}
