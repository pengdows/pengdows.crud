using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.wrappers;

namespace pengdows.crud;

public partial class EntityHelper<TEntity, TRowID>
{
    public TEntity MapReaderToObject(ITrackedReader reader)
    {
        var obj = new TEntity();

        var plan = GetOrBuildRecordsetPlan(reader);
        for (var idx = 0; idx < plan.Length; idx++)
        {
            plan[idx].Apply(reader, obj);
        }

        return obj;
    }

    private ColumnPlan[] GetOrBuildRecordsetPlan(ITrackedReader reader)
    {
        // Compute a stronger hash for the recordset shape: field count + names + types
        var fieldCount = reader.FieldCount;
        var hash = fieldCount * 397;
        for (var i = 0; i < fieldCount; i++)
        {
            var name = reader.GetName(i);
            hash = unchecked(hash * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(name));

            // Include field type to strengthen hash and avoid shape collisions
            var fieldType = reader.GetFieldType(i);
            hash = unchecked(hash * 31 + fieldType.GetHashCode());
        }

        // Try to get existing plan from thread-safe cache
        if (_readerPlans.TryGet(hash, out var existingPlan))
        {
            return existingPlan;
        }

        // Build new plan
        var list = new List<ColumnPlan>(fieldCount);
        for (var i = 0; i < fieldCount; i++)
        {
            var colName = reader.GetName(i);
            if (_columnsByNameCI.TryGetValue(colName, out var column))
            {
                var setter = GetOrCreateSetter(column.PropertyInfo);
                var ordinal = i;
                var fieldType = reader.GetFieldType(ordinal);
                var enumType = column.IsEnum ? column.EnumType : null;
                var enumUnderlying = column.IsEnum && column.EnumType != null ? Enum.GetUnderlyingType(column.EnumType) : null;
                var enumAsString = column.IsEnum && column.DbType == DbType.String;
                var isJson = column.IsJsonType;
                var jsonOpts = column.JsonSerializerOptions ?? new JsonSerializerOptions();
                var targetType = Nullable.GetUnderlyingType(column.PropertyInfo.PropertyType)
                                 ?? column.PropertyInfo.PropertyType;
                var dbType = column.DbType;

                Action<ITrackedReader, object> apply = (r, o) =>
                {
                    object? value;
                    if (r.IsDBNull(ordinal))
                    {
                        value = null;
                    }
                    else
                    {
                        value = Type.GetTypeCode(fieldType) switch
                        {
                            TypeCode.Int32 => r.GetInt32(ordinal),
                            TypeCode.Int64 => r.GetInt64(ordinal),
                            TypeCode.String => r.GetString(ordinal),
                            TypeCode.DateTime => r.GetDateTime(ordinal),
                            TypeCode.Decimal => r.GetDecimal(ordinal),
                            _ when fieldType == typeof(Guid) => r.GetGuid(ordinal),
                            _ when fieldType == typeof(byte[]) => ((DbDataReader)r).GetFieldValue<byte[]>(ordinal),
                            _ => r.GetValue(ordinal)
                        };
                    }

                    if (isJson && value is string json)
                    {
                        try
                        {
                            value = JsonSerializer.Deserialize(json, targetType, jsonOpts);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogDebug(ex, "Failed to deserialize JSON for column {Column}", column.Name);
                            value = null;
                        }
                    }
                    else if (enumType != null)
                    {
                        if (enumAsString)
                        {
                            var s = value?.ToString();
                            try
                            {
                                value = s != null ? Enum.Parse(enumType, s, true) : null;
                            }
                            catch
                            {
                                switch (EnumParseBehavior)
                                {
                                    case EnumParseFailureMode.Throw:
                                        throw;
                                    case EnumParseFailureMode.SetNullAndLog:
                                        Logger.LogWarning("Cannot convert '{Value}' to enum {EnumType}.", s, enumType);
                                        value = null;
                                        break;
                                    case EnumParseFailureMode.SetDefaultValue:
                                        value = Activator.CreateInstance(enumType);
                                        break;
                                    default:
                                        value = null;
                                        break;
                                }
                            }
                        }
                        else
                        {
                            try
                            {
                                var boxed = Convert.ChangeType(value, enumUnderlying!, CultureInfo.InvariantCulture);
                                value = Enum.ToObject(enumType, boxed!);
                            }
                            catch
                            {
                                switch (EnumParseBehavior)
                                {
                                    case EnumParseFailureMode.Throw:
                                        throw;
                                    case EnumParseFailureMode.SetNullAndLog:
                                        Logger.LogWarning("Cannot convert '{Value}' to enum {EnumType}.", value, enumType);
                                        value = null;
                                        break;
                                    case EnumParseFailureMode.SetDefaultValue:
                                        value = Activator.CreateInstance(enumType);
                                        break;
                                    default:
                                        value = null;
                                        break;
                                }
                            }
                        }
                    }
                    else if (value != null && !targetType.IsAssignableFrom(value.GetType()))
                    {
                        try
                        {
                            value = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
                        }
                        catch
                        {
                            switch (dbType)
                            {
                                case DbType.Decimal:
                                case DbType.Currency:
                                case DbType.VarNumeric:
                                    value = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                                    break;
                            }
                        }
                    }

                    try
                    {
                        setter(o, value);
                    }
                    catch (Exception ex)
                    {
                        var name = r.GetName(ordinal);
                        throw new InvalidValueException(
                            $"Unable to set property from value that was stored in the database: {name} :{ex.Message}");
                    }
                };

                list.Add(new ColumnPlan(apply));
            }
        }

        var plan = list.ToArray();

        return _readerPlans.GetOrAdd(hash, _ => plan);
    }

    public Action<object, object?> GetOrCreateSetter(PropertyInfo prop)
    {
        // The generated setter casts directly; a database NULL assigned to a non-nullable property will throw.
        // This fail-fast behavior surfaces unexpected schema mismatches immediately.
        return _propertySetters.GetOrAdd(prop, p =>
        {
            var objParam = Expression.Parameter(typeof(object));
            var valueParam = Expression.Parameter(typeof(object));

            var castObj = Expression.Convert(objParam, p.DeclaringType!);
            var castValue = Expression.Convert(valueParam, p.PropertyType);

            var propertyAccess = Expression.Property(castObj, p);
            var assignment = Expression.Assign(propertyAccess, castValue);

            var lambda = Expression.Lambda<Action<object, object?>>(assignment, objParam, valueParam);
            return lambda.Compile();
        });
    }
}
