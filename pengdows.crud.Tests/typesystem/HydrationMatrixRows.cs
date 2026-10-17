using System;
using System.Collections.Generic;
using System.Data;
using pengdows.crud.attributes;

namespace pengdows.crud.Tests.typesystem;

// One entity per DbType (a [Column] attribute's DbType is a constant), each generic over the property type, so the
// hydration matrix can close it over every CLR type the library reads.
public sealed class MatrixValue<T>
{
    public T V { get; set; } = default!;
}

[Table("matrix_boolean")] public sealed class MatrixBoolean<T> { [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Boolean)] public T V { get; set; } = default!; }
[Table("matrix_byte")] public sealed class MatrixByte<T> { [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Byte)] public T V { get; set; } = default!; }
[Table("matrix_sbyte")] public sealed class MatrixSByte<T> { [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.SByte)] public T V { get; set; } = default!; }
[Table("matrix_int16")] public sealed class MatrixInt16<T> { [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Int16)] public T V { get; set; } = default!; }
[Table("matrix_uint16")] public sealed class MatrixUInt16<T> { [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.UInt16)] public T V { get; set; } = default!; }
[Table("matrix_int32")] public sealed class MatrixInt32<T> { [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Int32)] public T V { get; set; } = default!; }
[Table("matrix_uint32")] public sealed class MatrixUInt32<T> { [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.UInt32)] public T V { get; set; } = default!; }
[Table("matrix_int64")] public sealed class MatrixInt64<T> { [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Int64)] public T V { get; set; } = default!; }
[Table("matrix_uint64")] public sealed class MatrixUInt64<T> { [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.UInt64)] public T V { get; set; } = default!; }
[Table("matrix_single")] public sealed class MatrixSingle<T> { [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Single)] public T V { get; set; } = default!; }
[Table("matrix_double")] public sealed class MatrixDouble<T> { [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Double)] public T V { get; set; } = default!; }
[Table("matrix_decimal")] public sealed class MatrixDecimal<T> { [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Decimal)] public T V { get; set; } = default!; }
[Table("matrix_object")] public sealed class MatrixObject<T> { [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Object)] public T V { get; set; } = default!; }
[Table("matrix_string")] public sealed class MatrixString<T> { [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.String)] public T V { get; set; } = default!; }
[Table("matrix_guid")] public sealed class MatrixGuid<T> { [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Guid)] public T V { get; set; } = default!; }
[Table("matrix_datetime2")] public sealed class MatrixDateTime2<T> { [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.DateTime2)] public T V { get; set; } = default!; }
[Table("matrix_dto")] public sealed class MatrixDateTimeOffset<T> { [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.DateTimeOffset)] public T V { get; set; } = default!; }
[Table("matrix_date")] public sealed class MatrixDate<T> { [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Date)] public T V { get; set; } = default!; }
[Table("matrix_time")] public sealed class MatrixTime<T> { [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Time)] public T V { get; set; } = default!; }
[Table("matrix_binary")] public sealed class MatrixBinary<T> { [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Binary)] public T V { get; set; } = default!; }

internal static class HydrationMatrixRows
{
    public static readonly IReadOnlyDictionary<DbType, Type> ByDbType = new Dictionary<DbType, Type>
    {
        [DbType.Boolean] = typeof(MatrixBoolean<>),
        [DbType.Byte] = typeof(MatrixByte<>),
        [DbType.SByte] = typeof(MatrixSByte<>),
        [DbType.Int16] = typeof(MatrixInt16<>),
        [DbType.UInt16] = typeof(MatrixUInt16<>),
        [DbType.Int32] = typeof(MatrixInt32<>),
        [DbType.UInt32] = typeof(MatrixUInt32<>),
        [DbType.Int64] = typeof(MatrixInt64<>),
        [DbType.UInt64] = typeof(MatrixUInt64<>),
        [DbType.Single] = typeof(MatrixSingle<>),
        [DbType.Double] = typeof(MatrixDouble<>),
        [DbType.Decimal] = typeof(MatrixDecimal<>),
        [DbType.Object] = typeof(MatrixObject<>),
        [DbType.String] = typeof(MatrixString<>),
        [DbType.Guid] = typeof(MatrixGuid<>),
        [DbType.DateTime2] = typeof(MatrixDateTime2<>),
        [DbType.DateTimeOffset] = typeof(MatrixDateTimeOffset<>),
        [DbType.Date] = typeof(MatrixDate<>),
        [DbType.Time] = typeof(MatrixTime<>),
        [DbType.Binary] = typeof(MatrixBinary<>)
    };
}
