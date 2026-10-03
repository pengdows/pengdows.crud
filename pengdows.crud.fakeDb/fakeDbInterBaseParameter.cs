using System.Diagnostics.CodeAnalysis;

namespace pengdows.crud.fakeDb;

/// <summary>
/// A parameter that behaves like InterBaseSql.Data.InterBaseClient's <c>IBParameter</c> for arrays,
/// created when <see cref="fakeDbFactory.EmulatesInterBaseParameterMetadata"/> is set: assigning an
/// array <see cref="Value"/> throws "Unknown type" (the driver infers the type from the value and
/// has no array mapping) unless <see cref="IBDbType"/> was set to <see cref="fakeIBDbType.Array"/>
/// first (confirmed live, InterBase 15).
/// </summary>
public class fakeDbInterBaseParameter : fakeDbParameter
{
    /// <summary>Mirrors <c>IBParameter.IBDbType</c>.</summary>
    public fakeIBDbType IBDbType { get; set; }

    [AllowNull]
    public override object Value
    {
        get => base.Value;
        set
        {
            if (value is Array and not byte[] && IBDbType != fakeIBDbType.Array)
            {
                throw new ArgumentException($"Unknown type: {value.GetType()}.");
            }

            base.Value = value;
        }
    }
}

/// <summary>The <c>IBDbType</c> members pengdows.crud dialects set, by name.</summary>
public enum fakeIBDbType
{
    VarChar = 0,
    Array
}
