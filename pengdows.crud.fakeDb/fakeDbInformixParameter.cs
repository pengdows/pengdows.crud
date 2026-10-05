namespace pengdows.crud.fakeDb;

/// <summary>
/// A parameter carrying Informix.Net.Core's <c>IfxType</c> property, created when
/// <see cref="fakeDbFactory.EmulatesInformixParameterMetadata"/> is set. Dialects find the property
/// by name, so tests can see what a dialect stamps on an <c>IfxParameter</c>.
/// </summary>
public class fakeDbInformixParameter : fakeDbParameter
{
    /// <summary>Mirrors <c>IfxParameter.IfxType</c>.</summary>
    public fakeIfxType IfxType { get; set; }
}

/// <summary>
/// The <c>Informix.Net.Core.IfxType</c> members pengdows.crud dialects set, by name (dialects parse the
/// name; the numeric values are not the driver's).
/// </summary>
public enum fakeIfxType
{
    Unset = 0,
    Text,
    Byte,
    Integer
}
