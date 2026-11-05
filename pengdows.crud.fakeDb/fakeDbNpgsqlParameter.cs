namespace pengdows.crud.fakeDb;

/// <summary>
/// A <see cref="fakeDbParameter"/> with Npgsql's provider metadata properties, created when
/// <see cref="fakeDbFactory.EmulatesNpgsqlParameterMetadata"/> is set. Dialects find these properties
/// by name, as on a real <c>NpgsqlParameter</c>.
/// </summary>
internal class fakeDbNpgsqlParameter : fakeDbParameter
{
    /// <summary>Mirrors <c>NpgsqlParameter.NpgsqlDbType</c>.</summary>
    public fakeNpgsqlDbType NpgsqlDbType { get; set; }

    /// <summary>Mirrors <c>NpgsqlParameter.DataTypeName</c>.</summary>
    public string? DataTypeName { get; set; }
}

/// <summary>
/// The <c>NpgsqlTypes.NpgsqlDbType</c> members pengdows.crud dialects set, with Npgsql's values.
/// </summary>
internal enum fakeNpgsqlDbType
{
    Bigint = 1,
    Boolean = 2,
    Integer = 9,
    Smallint = 18,
    Text = 19,
    Time = 20,
    Unknown = 40,
    Uuid = 27,
    TimeTz = 31,
    Jsonb = 36,
    Inet = 24,
    Interval = 30,
    MacAddr = 34,
    Hstore = 37,
    Cidr = 44,
    MacAddr8 = 54,
    BigIntRange = 1073741825,
    DateRange = 1073741831,
    IntegerRange = 1073741833,
    NumericRange = 1073741837,
    TimestampRange = 1073741845,
    TimestampTzRange = 1073741850,
    PgLsn = 59,
    Array = int.MinValue
}
