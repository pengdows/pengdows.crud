namespace pengdows.crud.fakeDb;

/// <summary>
/// A parameter carrying ODP.NET's <c>OracleDbType</c> property, created when
/// <see cref="fakeDbFactory.EmulatesOracleParameterMetadata"/> is set. Dialects find the property
/// by name, so tests can see what a dialect stamps on an ODP.NET parameter.
/// </summary>
public class fakeDbOracleParameter : fakeDbParameter
{
    /// <summary>Mirrors <c>OracleParameter.OracleDbType</c>.</summary>
    public fakeOracleDbType OracleDbType { get; set; }
}

/// <summary>
/// The <c>Oracle.ManagedDataAccess.Client.OracleDbType</c> members pengdows.crud dialects set, by
/// name (dialects parse the name; the numeric values are not ODP.NET's).
/// </summary>
public enum fakeOracleDbType
{
    Unset = 0,
    BinaryDouble,
    BinaryFloat,
    IntervalDS
}
