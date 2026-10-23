#region

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

#endregion

namespace pengdows.crud.fakeDb;

public class fakeDbParameter : DbParameter, IDbDataParameter
{
    public override bool SourceColumnNullMapping { get; set; }
    public override DbType DbType { get; set; }
    // A new parameter is an Input with a null Value, as DbParameter.Direction and DbParameter.Value document.
    public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
    public override bool IsNullable { get; set; }

    [AllowNull] public override string ParameterName { get; set; } = string.Empty;

    [AllowNull] public override string SourceColumn { get; set; } = string.Empty;
    [AllowNull] public override object Value { get; set; } = null!;

    public override int Size { get; set; }
    public override byte Precision { get; set; }
    public override byte Scale { get; set; }

    // "Resets the DbType property to its original settings": the type a new parameter has, as a real
    // provider's parameter goes back to inferring its type from the value.
    public override void ResetDbType()
    {
        DbType = default;
    }
}