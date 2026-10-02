namespace pengdows.crud.fakeDb;

/// <summary>
/// A <see cref="fakeDbParameter"/> with Npgsql's provider metadata properties, created when
/// <see cref="fakeDbFactory.EmulatesNpgsqlParameterMetadata"/> is set. Dialects find these properties
/// by name, as on a real <c>NpgsqlParameter</c>.
/// </summary>
public class fakeDbNpgsqlParameter : fakeDbParameter
{
    /// <summary>Mirrors <c>NpgsqlParameter.NpgsqlDbType</c>.</summary>
    public fakeNpgsqlDbType NpgsqlDbType { get; set; }

    /// <summary>Mirrors <c>NpgsqlParameter.DataTypeName</c>.</summary>
    public string? DataTypeName { get; set; }
}

/// <summary>
/// The <c>NpgsqlTypes.NpgsqlDbType</c> members pengdows.crud dialects set, with Npgsql's values.
/// </summary>
public enum fakeNpgsqlDbType
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
    PgLsn = 59,
    Array = int.MinValue
}
