using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Numerics;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// CreateDbParameter skips its value-type conversions for a T that can never be one of the converted types (a string, an int),
/// decided once per T. A value reaching it as an object, an interface or ValueType must still be converted exactly as the same
/// value passed as its own type is, on every dialect: the gate may only skip work, never a conversion.
/// </summary>
public class ParameterValueGatingTests
{
    private static readonly SupportedDatabase[] Databases =
    {
        SupportedDatabase.SqlServer, SupportedDatabase.Sqlite, SupportedDatabase.PostgreSql, SupportedDatabase.Firebird,
        SupportedDatabase.Oracle, SupportedDatabase.DuckDB, SupportedDatabase.MySql, SupportedDatabase.SingleStore
    };

    private static SqlDialect NewDialect(SupportedDatabase database)
    {
        var factory = new fakeDbFactory(database);
        return database switch
        {
            SupportedDatabase.SqlServer => new SqlServerDialect(factory, NullLogger.Instance),
            SupportedDatabase.Sqlite => new SqliteDialect(factory, NullLogger.Instance),
            SupportedDatabase.PostgreSql => new PostgreSqlDialect(factory, NullLogger.Instance),
            SupportedDatabase.Firebird => new FirebirdDialect(factory, NullLogger.Instance),
            SupportedDatabase.Oracle => new OracleDialect(factory, NullLogger.Instance),
            SupportedDatabase.DuckDB => new DuckDbDialect(factory, NullLogger.Instance),
            SupportedDatabase.MySql => new MySqlDialect(factory, NullLogger.Instance),
            SupportedDatabase.SingleStore => new MySqlDialect(factory, NullLogger.Instance, SupportedDatabase.SingleStore),
            _ => throw new ArgumentOutOfRangeException(nameof(database))
        };
    }

    public static TheoryData<SupportedDatabase, DbType, object> ConvertedValues()
    {
        var data = new TheoryData<SupportedDatabase, DbType, object>();
        foreach (var database in Databases)
        {
            data.Add(database, DbType.Binary, RowVersion.FromBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
            data.Add(database, DbType.Date, new DateOnly(2026, 10, 11));
            data.Add(database, DbType.Time, new TimeOnly(12, 30, 15));
            data.Add(database, DbType.String, 'x');
            data.Add(database, DbType.Object, new float[] { 1f, 2f });
            data.Add(database, DbType.Object, new double[] { 1d, 2d });
            data.Add(database, DbType.String, HierarchyId.Parse("/1/2/"));
            data.Add(database, DbType.Binary, HierarchyId.Parse("/1/2/"));
            data.Add(database, DbType.Object, new Int128(1, 2));
            data.Add(database, DbType.Object, new UInt128(1, 2));
            data.Add(database, DbType.DateTime, new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.FromHours(2)));
            data.Add(database, DbType.DateTimeOffset, new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc));
            data.Add(database, DbType.Binary, Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));
        }

        return data;
    }

    private static DbParameter AsOwnType(SqlDialect dialect, DbType type, object value) =>
        (DbParameter)typeof(ParameterValueGatingTests)
            .GetMethod(nameof(Create), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(value.GetType())
            .Invoke(null, new[] { dialect, type, value })!;

    private static DbParameter Create<T>(SqlDialect dialect, DbType type, T value) => dialect.CreateDbParameter("p", type, value);

    private static void AssertSame(DbParameter expected, DbParameter actual, string because)
    {
        Assert.True(expected.DbType == actual.DbType, $"{because}: DbType {actual.DbType}, expected {expected.DbType}");
        Assert.True(SameValue(expected.Value, actual.Value),
            $"{because}: value {Show(actual.Value)}, expected {Show(expected.Value)}");
    }

    private static string Show(object? value) =>
        value is IEnumerable items and not string ? "[" + string.Join(",", Enumerate(items)) + "]" : $"{value} ({value?.GetType().Name})";

    private static System.Collections.Generic.IEnumerable<object?> Enumerate(IEnumerable items)
    {
        foreach (var item in items)
        {
            yield return item;
        }
    }

    private static bool SameValue(object? a, object? b) =>
        a is byte[] x && b is byte[] y ? x.AsSpan().SequenceEqual(y) : Equals(a, b);

    [Theory]
    [MemberData(nameof(ConvertedValues))]
    public void AValueReachingCreateAsAnObject_IsConvertedAsItsOwnTypeIs(SupportedDatabase database, DbType type, object value)
    {
        var dialect = NewDialect(database);
        var expected = AsOwnType(dialect, type, value);

        AssertSame(expected, dialect.CreateDbParameter<object>("p", type, value), $"{database} {type} {value.GetType().Name} as object");
    }

    [Theory]
    [MemberData(nameof(ConvertedValues))]
    public void AValueReachingCreateAsValueType_IsConvertedAsItsOwnTypeIs(SupportedDatabase database, DbType type, object value)
    {
        if (value is not ValueType valueType)
        {
            return; // arrays are not value types
        }

        var dialect = NewDialect(database);
        var expected = AsOwnType(dialect, type, value);

        AssertSame(expected, dialect.CreateDbParameter<ValueType>("p", type, valueType), $"{database} {type} {value.GetType().Name} as ValueType");
    }

    [Theory]
    [MemberData(nameof(ConvertedValues))]
    public void AValueReachingCreateAsIComparable_IsConvertedAsItsOwnTypeIs(SupportedDatabase database, DbType type, object value)
    {
        if (value is not IComparable comparable)
        {
            return; // RowVersion, HierarchyId and the arrays are not IComparable
        }

        var dialect = NewDialect(database);
        var expected = AsOwnType(dialect, type, value);

        AssertSame(expected, dialect.CreateDbParameter<IComparable>("p", type, comparable), $"{database} {type} {value.GetType().Name} as IComparable");
    }

    [Fact]
    public void AByteBoundAsAnInterface_IsAcceptedByFirebirdAsItWasIn205()
    {
        var dialect = NewDialect(SupportedDatabase.Firebird);

        var parameter = dialect.CreateDbParameter<IComparable>("p", DbType.Byte, (byte)3);

        Assert.Equal((byte)3, Convert.ToByte(parameter.Value));
    }
}
