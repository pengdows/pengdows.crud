#region

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

#endregion

namespace pengdows.crud.FakeDb;

public class FakeDbParameter : DbParameter, IDbDataParameter
{
    public override bool SourceColumnNullMapping { get; set; }
    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; }
    public override bool IsNullable { get; set; }

    [AllowNull]
    public override string ParameterName { get; set; }

    [AllowNull]
    public override string SourceColumn { get; set; }
    [AllowNull]
    public override object Value { get; set; }

    public override int Size { get; set; }
    public override byte Precision { get; set; }
    public override byte Scale { get; set; }

    public override void ResetDbType()
    {
        DbType = DbType.Object;
    }
}
