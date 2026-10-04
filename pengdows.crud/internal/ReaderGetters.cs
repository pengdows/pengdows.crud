using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Reflection;

namespace pengdows.crud.@internal;

/// <summary>
/// The reader methods both mappers compile calls to (DRY-008): the typed getter for a column's type and
/// <see cref="DbDataReader.GetFieldValue{T}"/>, found once instead of in each mapper and field reader.
/// </summary>
internal static class ReaderGetters
{
    public static readonly MethodInfo GetFieldValueDefinition = typeof(DbDataReader).GetMethods()
        .First(m => m.Name == nameof(DbDataReader.GetFieldValue) && m.IsGenericMethodDefinition);

    public static readonly MethodInfo GetValue =
        typeof(IDataRecord).GetMethod(nameof(IDataRecord.GetValue), new[] { typeof(int) })!;

    private static readonly Dictionary<Type, string> TypedGetterNames = new()
    {
        [typeof(int)] = nameof(IDataRecord.GetInt32),
        [typeof(long)] = nameof(IDataRecord.GetInt64),
        [typeof(string)] = nameof(IDataRecord.GetString),
        [typeof(DateTime)] = nameof(IDataRecord.GetDateTime),
        [typeof(decimal)] = nameof(IDataRecord.GetDecimal),
        [typeof(bool)] = nameof(IDataRecord.GetBoolean),
        [typeof(short)] = nameof(IDataRecord.GetInt16),
        [typeof(byte)] = nameof(IDataRecord.GetByte),
        [typeof(double)] = nameof(IDataRecord.GetDouble),
        [typeof(float)] = nameof(IDataRecord.GetFloat),
        [typeof(Guid)] = nameof(IDataRecord.GetGuid)
    };

    private static readonly ConcurrentDictionary<(Type Reader, Type Field), MethodInfo?> Typed = new();

    /// <summary><paramref name="readerType"/>'s typed getter for a column of <paramref name="fieldType"/>, or null.</summary>
    public static MethodInfo? TypedGetter(Type readerType, Type fieldType) =>
        Typed.GetOrAdd((readerType, fieldType), static key => TypedGetterNames.TryGetValue(key.Field, out var name)
            ? key.Reader.GetMethod(name, new[] { typeof(int) })
            : null);

    public static MethodInfo GetFieldValueOf(Type type) => GetFieldValueDefinition.MakeGenericMethod(type);
}
