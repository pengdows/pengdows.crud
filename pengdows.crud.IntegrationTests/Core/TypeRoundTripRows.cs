using System.Data;
using pengdows.crud.attributes;

namespace pengdows.crud.IntegrationTests.Core;

// One row entity per DbType for TypeRoundTripMatrixTests: the column attribute's DbType is a
// compile-time constant, so the matrix picks the generic row class matching the catalog entry's
// DbType and closes it over the entry's CLR type. Generated; one class per DbType member used.
internal static class TypeRoundTripRows
{
    public static readonly IReadOnlyDictionary<DbType, Type> ByDbType = new Dictionary<DbType, Type>
    {
        [DbType.AnsiString] = typeof(TypeRowAnsiString<>),
        [DbType.AnsiStringFixedLength] = typeof(TypeRowAnsiStringFixedLength<>),
        [DbType.Binary] = typeof(TypeRowBinary<>),
        [DbType.Boolean] = typeof(TypeRowBoolean<>),
        [DbType.Byte] = typeof(TypeRowByte<>),
        [DbType.Currency] = typeof(TypeRowCurrency<>),
        [DbType.Date] = typeof(TypeRowDate<>),
        [DbType.DateTime] = typeof(TypeRowDateTime<>),
        [DbType.DateTime2] = typeof(TypeRowDateTime2<>),
        [DbType.DateTimeOffset] = typeof(TypeRowDateTimeOffset<>),
        [DbType.Decimal] = typeof(TypeRowDecimal<>),
        [DbType.Double] = typeof(TypeRowDouble<>),
        [DbType.Guid] = typeof(TypeRowGuid<>),
        [DbType.Int16] = typeof(TypeRowInt16<>),
        [DbType.Int32] = typeof(TypeRowInt32<>),
        [DbType.Int64] = typeof(TypeRowInt64<>),
        [DbType.Object] = typeof(TypeRowObject<>),
        [DbType.SByte] = typeof(TypeRowSByte<>),
        [DbType.Single] = typeof(TypeRowSingle<>),
        [DbType.String] = typeof(TypeRowString<>),
        [DbType.StringFixedLength] = typeof(TypeRowStringFixedLength<>),
        [DbType.Time] = typeof(TypeRowTime<>),
        [DbType.UInt16] = typeof(TypeRowUInt16<>),
        [DbType.UInt32] = typeof(TypeRowUInt32<>),
        [DbType.UInt64] = typeof(TypeRowUInt64<>),
        [DbType.VarNumeric] = typeof(TypeRowVarNumeric<>),
        [DbType.Xml] = typeof(TypeRowXml<>),
    };
}

[Table("type_rt")]
internal sealed class TypeRowAnsiString<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.AnsiString)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowAnsiStringFixedLength<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.AnsiStringFixedLength)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowBinary<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.Binary)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowBoolean<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.Boolean)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowByte<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.Byte)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowCurrency<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.Currency)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowDate<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.Date)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowDateTime<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.DateTime)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowDateTime2<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.DateTime2)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowDateTimeOffset<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.DateTimeOffset)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowDecimal<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.Decimal)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowDouble<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.Double)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowGuid<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.Guid)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowInt16<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.Int16)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowInt32<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.Int32)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowInt64<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.Int64)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowObject<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.Object)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowSByte<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.SByte)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowSingle<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.Single)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowString<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.String)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowStringFixedLength<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.StringFixedLength)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowTime<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.Time)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowUInt16<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.UInt16)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowUInt32<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.UInt32)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowUInt64<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.UInt64)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowVarNumeric<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.VarNumeric)] public T V { get; set; } = default!;
}

[Table("type_rt")]
internal sealed class TypeRowXml<T>
{
    [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("v", DbType.Xml)] public T V { get; set; } = default!;
}
