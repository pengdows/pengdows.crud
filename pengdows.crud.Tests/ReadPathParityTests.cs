using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// DRY-003: the gateway's compiled mapper, DataReaderMapper and TypeCoercionHelper.Coerce each
/// converted stored values on their own and disagreed (rounding vs truncation, DateTime kind, NaN into
/// bool, enum validation, Guid byte order). Every case reads the same stored value through all three
/// and requires the same result, or a failure from all three.
/// </summary>
// [Collection("TypeRegistry")]: lenient mapping logs through the process-global TypeCoercionHelper.Logger.
[Collection("TypeRegistry")]
public class ReadPathParityTests
{
    [Flags]
    public enum Perm
    {
        Read = 1,
        Write = 2,
        Admin = 4
    }

    public enum Mood
    {
        Sad = 1,
        Ok = 2
    }

    [Table("t")]
    public sealed class G<T>
    {
        [Id] [Column("id", System.Data.DbType.Int32)] public int Id { get; set; }
        [Column("v", System.Data.DbType.Object)] public T V { get; set; } = default!;
    }

    [Table("t")]
    public sealed class NumericEnum<T>
    {
        [Id] [Column("id", System.Data.DbType.Int32)] public int Id { get; set; }
        [Column("v", System.Data.DbType.Int32)] public T V { get; set; } = default!;
    }

    [Table("t")]
    public sealed class StringEnum<T>
    {
        [Id] [Column("id", System.Data.DbType.Int32)] public int Id { get; set; }
        [Column("v", System.Data.DbType.String)] public T V { get; set; } = default!;
    }

    public sealed class Plain<T>
    {
        public int Id { get; set; }
        public T V { get; set; } = default!;
    }

    private static readonly Guid SampleGuid = new("00112233-4455-6677-8899-aabbccddeeff");

    public static IEnumerable<object[]> Cases() => new[]
    {
        // numbers
        Case<long>(5), Case<int>(5L), Case<int>(long.MaxValue), Case<int>(2.0m), Case<int>(2.7m), Case<int>(double.NaN),
        Case<long>(7u), Case<double>(3L), Case<double>(new System.Numerics.BigInteger(ulong.MaxValue) * 3 + 1),
        Case<long>(new System.Numerics.BigInteger(42)), Case<double>(1.25m), Case<decimal>(1.5d), Case<short>(70000),
        // into bool
        Case<bool>(1), Case<bool>(0L), Case<bool>(1m), Case<bool>(0.5d), Case<bool>(double.NaN), Case<bool>(ulong.MaxValue),
        Case<bool>("true"), Case<bool>("0"),
        // Guid
        Case<Guid>(SampleGuid.ToString()), Case<Guid>(SampleGuid.ToByteArray()), Case<Guid>(new byte[15]), Case<Guid>(" "),
        Case<Guid?>(SampleGuid.ToString()),
        // dates and times
        Case<DateTime>(new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Unspecified)),
        Case<DateTimeOffset>(new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Unspecified)),
        Case<DateTime>("2026-10-04T12:00:00Z"), Case<DateTime>("2026-10-04T07:00:00-05:00"),
        Case<DateTimeOffset>("2026-10-04T07:00:00-05:00"), Case<DateTime>("not a date"),
        Case<DateOnly>(new DateTime(2026, 10, 4)), Case<DateOnly>("2026-10-04"),
        Case<TimeOnly>(new TimeSpan(13, 45, 0)), Case<TimeOnly>(TimeSpan.FromHours(25)),
        Case<TimeSpan>(new TimeSpan(1, 2, 3)),
        // text
        Case<string>(42), Case<string>(SampleGuid), Case<char>("x"), Case<char>("xy"),
        // enums stored as numbers
        NumericEnumCase<Mood>(2), NumericEnumCase<Mood>(99), NumericEnumCase<Mood>(2L), NumericEnumCase<Mood>(2.0m),
        NumericEnumCase<Perm>(3), NumericEnumCase<Perm>(8), NumericEnumCase<Mood?>(1),
        // enums stored as names
        StringEnumCase<Mood>("Ok"), StringEnumCase<Mood>("ok"), StringEnumCase<Mood>("Nope"), StringEnumCase<Mood>("99"),
        StringEnumCase<Mood>("2"), StringEnumCase<Perm>("Read, Write")
    };

    private static object[] Case<T>(object stored) => new object[] { typeof(G<T>), typeof(T), stored };
    private static object[] NumericEnumCase<T>(object stored) => new object[] { typeof(NumericEnum<T>), typeof(T), stored };
    private static object[] StringEnumCase<T>(object stored) => new object[] { typeof(StringEnum<T>), typeof(T), stored };

    private sealed record Outcome(bool Failed, object? Value, string Detail)
    {
        public override string ToString() => Failed ? $"fails ({Detail})" : $"{Format(Value)}";

        private static string Format(object? v) => v switch
        {
            null => "null",
            DateTime d => $"{d:O} ({d.Kind})",
            DateTimeOffset o => $"{o:O}",
            _ => $"{v} ({v.GetType().Name})"
        };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EveryReadPath_GivesTheSameResult(Type entityType, Type propertyType, object stored)
    {
        var gateway = await (Task<Outcome>)typeof(ReadPathParityTests).GetMethod(nameof(ViaGateway),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(entityType).Invoke(null, new[] { stored })!;
        var mapper = await (Task<Outcome>)typeof(ReadPathParityTests).GetMethod(nameof(ViaDataReaderMapper),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(propertyType).Invoke(null, new[] { stored })!;
        var coerce = ViaCoerce(stored, propertyType);

        Assert.True(Same(gateway, mapper) && Same(gateway, coerce),
            $"{stored} ({stored.GetType().Name}) into {propertyType.Name}: gateway {gateway}, DataReaderMapper {mapper}, Coerce {coerce}");
    }

    private static bool Same(Outcome a, Outcome b) =>
        a.Failed == b.Failed && (a.Failed || (Equals(a.Value, b.Value) &&
                                              (a.Value is not DateTime x || x.Kind == ((DateTime)b.Value!).Kind) &&
                                              (a.Value is not DateTimeOffset y || y.Offset == ((DateTimeOffset)b.Value!).Offset)));

    private static fakeDbFactory Factory(object stored, out DatabaseContext context)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
        factory.EnqueueReaderResult(new[] { new Dictionary<string, object> { ["id"] = 1, ["v"] = stored } });
        return factory;
    }

    private static async Task<Outcome> ViaGateway<TEntity>(object stored) where TEntity : class, new()
    {
        Factory(stored, out var context);
        await using var _ = context;
        try
        {
            var gateway = new TableGateway<TEntity, int>(context);
            await using var sc = context.CreateSqlContainer("SELECT id, v FROM t");
            var row = await gateway.LoadSingleAsync(sc);
            return new Outcome(false, typeof(TEntity).GetProperty("V")!.GetValue(row), "");
        }
        catch (Exception ex)
        {
            return new Outcome(true, null, ex.GetType().Name);
        }
    }

    private static async Task<Outcome> ViaDataReaderMapper<T>(object stored)
    {
        Factory(stored, out var context);
        await using var _ = context;
        try
        {
            await using var sc = context.CreateSqlContainer("SELECT id, v FROM t");
            await using var reader = await sc.ExecuteReaderAsync();
            var rows = await DataReaderMapper.LoadAsync<Plain<T>>(reader, new MapperOptions(Strict: true));
            return new Outcome(false, rows[0].V, "");
        }
        catch (Exception ex)
        {
            return new Outcome(true, null, ex.GetType().Name);
        }
    }

    private static Outcome ViaCoerce(object stored, Type propertyType)
    {
        try
        {
            // The options a SQLite context reads with (its Guid byte order included).
            Factory(stored, out var context);
            using var _ = context;
            var options = TypeCoercionOptions.For(context.Dialect);
            return new Outcome(false, TypeCoercionHelper.Coerce(stored, stored.GetType(), propertyType, options), "");
        }
        catch (Exception ex)
        {
            return new Outcome(true, null, ex.GetType().Name);
        }
    }
}
