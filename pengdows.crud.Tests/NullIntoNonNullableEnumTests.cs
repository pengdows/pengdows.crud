using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

// A NULL stored in a column the property cannot hold fails loudly for every non-nullable value type (a typed getter on
// NULL throws), and an enum is a value type like any other: on the gateway path it used to be the exception, keeping an
// IsDBNull guard that silently left the property at its default. A nullable enum still takes NULL as null.
public class NullIntoNonNullableEnumTests
{
    public enum Mood { Sad = 0, Ok = 1, Happy = 2 }

    [Table("n_int")] public sealed class IntEntity { [PrimaryKey(1)] [Column("V", DbType.Int32)] public int V { get; set; } }
    [Table("n_enum")] public sealed class EnumEntity { [PrimaryKey(1)] [Column("V", DbType.Int32)] public Mood V { get; set; } }
    [Table("n_enum_s")] public sealed class EnumTextEntity { [PrimaryKey(1)] [Column("V", DbType.String)] public Mood V { get; set; } }
    [Table("n_enum_n")] public sealed class NullableEnumEntity { [PrimaryKey(1)] [Column("V", DbType.Int32)] public Mood? V { get; set; } }

    private static (T? Entity, Exception? Failure) ReadNull<T>(Type column) where T : class, new()
    {
        using var reader = new fakeDbDataReader(new List<Dictionary<string, object>>
        {
            new(StringComparer.OrdinalIgnoreCase) { ["V"] = DBNull.Value }
        }) { Columns = [new fakeDbColumn("V", column, "X")] };
        var columns = new TypeMapRegistry().GetTableInfo<T>().Columns;
        var mapper = CompiledMapperFactory<T>.Create<DbDataReader>(
            reader, columns, EnumParseFailureMode.Throw, ["V"], [column]);
        Assert.True(reader.Read());

        T? entity = null;
        var failure = Record.Exception(() => entity = mapper(reader));
        return (entity, failure);
    }

    [Fact]
    public void NullIntoANonNullableInt_Fails()
    {
        Assert.NotNull(ReadNull<IntEntity>(typeof(int)).Failure);
    }

    [Fact]
    public void NullIntoANonNullableEnum_FailsLikeAnInt()
    {
        var intFailure = ReadNull<IntEntity>(typeof(int)).Failure;
        var enumFailure = ReadNull<EnumEntity>(typeof(int)).Failure;

        Assert.NotNull(enumFailure);
        Assert.Equal(intFailure!.GetType(), enumFailure!.GetType());
    }

    [Fact]
    public void NullIntoANonNullableEnumStoredAsText_Fails()
    {
        Assert.NotNull(ReadNull<EnumTextEntity>(typeof(string)).Failure);
    }

    [Fact]
    public void NullIntoANullableEnum_IsNull()
    {
        var (entity, failure) = ReadNull<NullableEnumEntity>(typeof(int));

        Assert.Null(failure);
        Assert.Null(entity!.V);
    }
}
