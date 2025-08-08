#region

using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Threading;

#endregion

namespace pengdows.crud;

public static class DataReaderMapper
{
    public static async Task<List<T>> LoadObjectsFromDataReaderAsync<T>(
        IDataReader reader,
        CancellationToken cancellationToken = default
    ) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(reader);
        var rdr = reader as DbDataReader;
        if (reader is not DbDataReader test)
        {
            throw new ArgumentException("reader must be DbDataReader", nameof(reader));
        }
        
        var result = new List<T>();
        var type = typeof(T);

        // Precompute matching columns to properties
        var propertyMap = new List<(int Ordinal, PropertyInfo Property)>();
        var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.SetProperty)
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            if (props.TryGetValue(name, out var prop)) propertyMap.Add((i, prop));
        }

        while (await rdr.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var obj = new T();

            foreach (var (ordinal, prop) in propertyMap)
            {
                if (await rdr.IsDBNullAsync((int)ordinal, (CancellationToken)cancellationToken)
                        .ConfigureAwait(false)) continue;

                try
                {
                    var value = await rdr.GetFieldValueAsync<object>(ordinal, cancellationToken)
                        .ConfigureAwait(false);
                    value = Utils.IsNullOrDbNull(value)
                        ? null
                        : TypeCoercionHelper.Coerce(value, value.GetType(), prop.PropertyType);

                    prop.SetValue(obj, value);
                }
                catch
                {
                    // Swallow assignment issues (type mismatch, etc.)
                }
            }

            result.Add(obj);
        }

        return result;
    }
}