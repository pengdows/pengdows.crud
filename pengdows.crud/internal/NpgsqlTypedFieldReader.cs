// =============================================================================
// FILE: NpgsqlTypedFieldReader.cs
// PURPOSE: Reads a PostgreSQL-family column through Npgsql's own value type, where GetValue() loses
//          information.
//
// AI SUMMARY:
// - Inet: GetValue() returns a plain IPAddress for inet, dropping the netmask ('192.168.1.10/24'::inet
//   reads as 192.168.1.10, confirmed live on Npgsql 9; TYPE-002); reads NpgsqlTypes.NpgsqlInet.
// - Interval: GetValue() returns TimeSpan for interval: positive months throw, negative months are
//   silently dropped and the days/time split is renormalized; reads NpgsqlTypes.NpgsqlInterval.
// - Read(record, ordinal) uses GetFieldValue<that type> for a column of the given data type on an
//   Npgsql reader, otherwise record.GetValue(ordinal). Npgsql is resolved by name (no dependency);
//   the GetFieldValue<T> call is compiled once into a delegate.
// - Used by entity hydration (CompiledMapperFactory, DataReaderMapper) only for Inet and
//   PostgreSqlInterval targets; raw ITrackedReader.GetValue is unchanged.
// =============================================================================

using System.Data;
using System.Data.Common;
using System.Linq.Expressions;

namespace pengdows.crud.@internal;

internal sealed class NpgsqlTypedFieldReader
{
    public static readonly NpgsqlTypedFieldReader Inet = new("NpgsqlTypes.NpgsqlInet, Npgsql", "inet");
    public static readonly NpgsqlTypedFieldReader Interval = new("NpgsqlTypes.NpgsqlInterval, Npgsql", "interval");

    private readonly string _dataTypeName;
    private readonly Lazy<Func<DbDataReader, int, object>?> _read;
    private readonly Lazy<Type?> _valueType;

    private NpgsqlTypedFieldReader(string npgsqlTypeName, string dataTypeName)
    {
        _dataTypeName = dataTypeName;
        _valueType = new Lazy<Type?>(() => Type.GetType(npgsqlTypeName, throwOnError: false));
        _read = new Lazy<Func<DbDataReader, int, object>?>(() => Compile(npgsqlTypeName));
    }

    internal static readonly System.Reflection.MethodInfo ReadTypedMethod =
        typeof(NpgsqlTypedFieldReader).GetMethod(nameof(ReadTyped))!;

    /// <summary>
    /// True when the column is this reader's data type on an Npgsql reader. The type of a column never changes
    /// within a result set, so this is asked once, when a plan is built, and not on every row.
    /// </summary>
    public bool AppliesTo(IDataRecord record, int ordinal)
    {
        var reader = record as DbDataReader ?? (record as IInternalTrackedReader)?.InnerReader;
        return reader != null && _read.Value != null &&
               string.Equals(reader.GetDataTypeName(ordinal), _dataTypeName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The column's typed value; only for a column <see cref="AppliesTo"/> accepted.</summary>
    public object ReadTyped(IDataRecord record, int ordinal) =>
        _read.Value!((record as DbDataReader ?? ((IInternalTrackedReader)record).InnerReader), ordinal);

    private Type? ValueType => _valueType.Value;

    /// <summary>The provider's reader behind a record (the tracked reader's inner reader, or the reader itself).</summary>
    public static DbDataReader Unwrap(IDataRecord record) =>
        record as DbDataReader ?? ((IInternalTrackedReader)record).InnerReader;

    internal static readonly System.Reflection.MethodInfo UnwrapMethod =
        typeof(NpgsqlTypedFieldReader).GetMethod(nameof(Unwrap))!;

    /// <summary>
    /// The whole read as one typed expression, for a column <see cref="AppliesTo"/> accepted: Npgsql's struct is read
    /// with GetFieldValue&lt;T&gt; (no boxing) and the library's value is built from its members. Null for a shape this
    /// does not know, which then takes the general conversion.
    /// </summary>
    public Expression? BuildTypedValue(Expression record, int ordinal)
    {
        var valueType = ValueType;
        if (valueType == null)
        {
            return null;
        }

        var read = Expression.Call(Expression.Call(UnwrapMethod, record), ReaderGetters.GetFieldValueOf(valueType),
            Expression.Constant(ordinal));
        var value = Expression.Variable(valueType, "provider");

        Expression? build = null;
        if (valueType.FullName == "NpgsqlTypes.NpgsqlInterval" &&
            valueType.GetProperty("Months") is { } months && valueType.GetProperty("Days") is { } days &&
            valueType.GetProperty("Time") is { } time)
        {
            var ctor = typeof(types.valueobjects.PostgreSqlInterval).GetConstructor(new[] { typeof(int), typeof(int), typeof(long) })!;
            build = Expression.New(ctor,
                Expression.ConvertChecked(Expression.Property(value, months), typeof(int)),
                Expression.ConvertChecked(Expression.Property(value, days), typeof(int)),
                Expression.ConvertChecked(Expression.Property(value, time), typeof(long)));
        }
        else if (valueType.FullName == "NpgsqlTypes.NpgsqlInet" &&
                 valueType.GetProperty("Address")?.PropertyType == typeof(System.Net.IPAddress) &&
                 valueType.GetProperty("Netmask") is { } netmask)
        {
            var fromProvider = typeof(types.valueobjects.Inet).GetMethod("FromProvider",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static, null, new[] { typeof(System.Net.IPAddress), typeof(byte?) }, null);
            if (fromProvider != null)
            {
                build = Expression.Call(fromProvider, Expression.Property(value, "Address"),
                    Expression.Convert(Expression.ConvertChecked(Expression.Property(value, netmask), typeof(byte)), typeof(byte?)));
            }
        }

        return build == null ? null : Expression.Block(build.Type, new[] { value }, Expression.Assign(value, read), build);
    }

    public object Read(IDataRecord record, int ordinal)
    {
        var reader = record as DbDataReader ?? (record as IInternalTrackedReader)?.InnerReader;
        var read = _read.Value;
        if (reader != null && read != null &&
            string.Equals(reader.GetDataTypeName(ordinal), _dataTypeName, StringComparison.OrdinalIgnoreCase))
        {
            return read(reader, ordinal);
        }

        return record.GetValue(ordinal);
    }

    private static Func<DbDataReader, int, object>? Compile(string npgsqlTypeName)
    {
        var valueType = Type.GetType(npgsqlTypeName, throwOnError: false);
        if (valueType == null)
        {
            return null;
        }

        var getFieldValue = ReaderGetters.GetFieldValueOf(valueType);
        var reader = Expression.Parameter(typeof(DbDataReader), "reader");
        var ordinal = Expression.Parameter(typeof(int), "ordinal");
        var body = Expression.Convert(Expression.Call(reader, getFieldValue, ordinal), typeof(object));
        return Expression.Lambda<Func<DbDataReader, int, object>>(body, reader, ordinal).Compile();
    }
}
