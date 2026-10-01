using System;
using System.Collections.Generic;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// The gateways build their SQL templates from a default-constructed entity, so every column
/// type must survive being written at its default value. A value object whose default holds
/// nothing (an Inet with no address) is SQL NULL, never an exception.
/// </summary>
public class DefaultValueObjectColumnCreateTests
{
    [Table("vo_rows")]
    public sealed class Row<T>
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.Object)] public T Value { get; set; } = default!;
    }

    public static IEnumerable<object[]> Cases()
    {
        var types = new[]
        {
            typeof(Inet), typeof(Cidr), typeof(MacAddress), typeof(JsonValue), typeof(HStore),
            typeof(PostgreSqlInterval), typeof(IntervalYearMonth), typeof(IntervalDaySecond),
            typeof(RowVersion), typeof(HierarchyId), typeof(Range<int>), typeof(Range<long>),
            typeof(Range<DateTime>), typeof(Range<decimal>)
        };
        foreach (var product in new[] { SupportedDatabase.PostgreSql, SupportedDatabase.SqlServer,
                     SupportedDatabase.Oracle, SupportedDatabase.MySql, SupportedDatabase.Sqlite })
        {
            foreach (var type in types)
            {
                yield return new object[] { product, type.Name.Replace("`1", "<" + (type.IsGenericType ? type.GetGenericArguments()[0].Name : "") + ">") };
            }
        }
    }

    private static readonly Dictionary<string, Type> TypesByName = new()
    {
        ["Inet"] = typeof(Inet), ["Cidr"] = typeof(Cidr), ["MacAddress"] = typeof(MacAddress),
        ["JsonValue"] = typeof(JsonValue), ["HStore"] = typeof(HStore),
        ["PostgreSqlInterval"] = typeof(PostgreSqlInterval), ["IntervalYearMonth"] = typeof(IntervalYearMonth),
        ["IntervalDaySecond"] = typeof(IntervalDaySecond), ["RowVersion"] = typeof(RowVersion),
        ["HierarchyId"] = typeof(HierarchyId), ["Range<Int32>"] = typeof(Range<int>),
        ["Range<Int64>"] = typeof(Range<long>), ["Range<DateTime>"] = typeof(Range<DateTime>),
        ["Range<Decimal>"] = typeof(Range<decimal>)
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void BuildCreate_DefaultValueObjectColumn_BuildsTheInsert(SupportedDatabase product, string typeName)
    {
        var method = typeof(DefaultValueObjectColumnCreateTests).GetMethod(nameof(BuildCreateFor),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        method.MakeGenericMethod(TypesByName[typeName]).Invoke(null, new object[] { product });
    }

    private static void BuildCreateFor<T>(SupportedDatabase product)
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", new fakeDbFactory(product));
        var gateway = new TableGateway<Row<T>, int>(context);

        using var sc = gateway.BuildCreate(new Row<T> { Id = 1 });

        Assert.Contains("INSERT", sc.Query.ToString());
    }
}
