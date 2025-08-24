#region

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using pengdows.crud.attributes;
using pengdows.crud.exceptions;

#endregion

namespace pengdows.crud;

public class TypeMapRegistry : ITypeMapRegistry
{
    private readonly ConcurrentDictionary<Type, TableInfo> _typeMap = new();

    public ITableInfo GetTableInfo<T>()
    {
        var type = typeof(T);

        if (_typeMap.TryGetValue(type, out var cached))
        {
            return cached;
        }

        var tattr = type.GetCustomAttribute<TableAttribute>() ??
                    throw new InvalidOperationException($"Type {type.Name} does not have a TableAttribute.");

        var tableInfo = new TableInfo
        {
            Name = tattr.Name,
            Schema = tattr.Schema ?? string.Empty
        };

        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);

        foreach (var prop in properties)
        {
            var attrs = prop.GetCustomAttributes(inherit: true);

            TAttr? A<TAttr>() where TAttr : Attribute
            {
                return (TAttr?)attrs.FirstOrDefault(a => a is TAttr);
            }

            var colAttr = A<ColumnAttribute>();
            if (colAttr == null)
            {
                continue;
            }

            var idAttr = A<IdAttribute>();
            var pkAttr = A<PrimaryKeyAttribute>();
            var enumAttr = A<EnumColumnAttribute>();
            var jsonAttr = A<JsonAttribute>();
            var nonIns = A<NonInsertableAttribute>();
            var nonUpd = A<NonUpdateableAttribute>();
            var verAttr = A<VersionAttribute>();
            var cby = A<CreatedByAttribute>();
            var con = A<CreatedOnAttribute>();
            var lby = A<LastUpdatedByAttribute>();
            var lon = A<LastUpdatedOnAttribute>();

            var isId = idAttr != null;
            var isIdWritable = idAttr?.Writable ?? true;
            var isNonInsertable = nonIns != null || (isId && !isIdWritable);

            var ci = new ColumnInfo
            {
                Name = colAttr.Name,
                PropertyInfo = prop,
                DbType = colAttr.Type,
                IsId = isId,
                IsIdIsWritable = isId && isIdWritable && nonIns == null,
                IsNonInsertable = isNonInsertable,
                IsNonUpdateable = nonUpd != null || isId,
                IsPrimaryKey = pkAttr != null,
                PkOrder = pkAttr?.Order ?? 0,
                IsEnum = enumAttr != null,
                EnumType = enumAttr?.EnumType,
                IsJsonType = jsonAttr != null,
                JsonSerializerOptions = jsonAttr?.SerializerOptions != null
                    ? new JsonSerializerOptions(jsonAttr.SerializerOptions)
                    : JsonSerializerOptions.Default,
                IsVersion = verAttr != null,
                IsCreatedBy = cby != null,
                IsCreatedOn = con != null,
                IsLastUpdatedBy = lby != null,
                IsLastUpdatedOn = lon != null,
                Ordinal = colAttr.Ordinal
            };

            if (ci.IsLastUpdatedBy)
            {
                tableInfo.LastUpdatedBy = ci;
            }

            if (ci.IsLastUpdatedOn)
            {
                tableInfo.LastUpdatedOn = ci;
            }

            if (ci.IsCreatedBy)
            {
                tableInfo.CreatedBy = ci;
            }

            if (ci.IsCreatedOn)
            {
                tableInfo.CreatedOn = ci;
            }

            if (tableInfo.Columns.ContainsKey(ci.Name))
            {
                throw new InvalidOperationException($"Duplicate ColumnAttribute name '{ci.Name}' on type {type.Name}.");
            }

            tableInfo.Columns[ci.Name] = ci;

            if (ci.IsId)
            {
                if (tableInfo.Id != null)
                {
                    throw new TooManyColumns("Only one id is allowed.");
                }

                tableInfo.Id = ci;

                if (ci.IsPrimaryKey)
                {
                    throw new PrimaryKeyOnRowIdColumn("Not allowed to have primary key attribute on id column.");
                }
            }

            if (ci.IsVersion)
            {
                if (tableInfo.Version != null)
                {
                    throw new TooManyColumns("Only one version is allowed.");
                }

                tableInfo.Version = ci;
            }
        }

        if (tableInfo.Columns.Count == 0)
        {
            throw new NoColumnsFoundException("This POCO entity has no properties, marked as columns.");
        }

        var hasId = tableInfo.Id != null;
        var primaryKeys = new List<IColumnInfo>();
        foreach (var col in tableInfo.Columns.Values)
        {
            if (col.IsPrimaryKey)
            {
                primaryKeys.Add(col);
            }
        }

        if (!hasId && primaryKeys.Count == 0)
        {
            throw new InvalidOperationException($"Type {type.Name} must define either [Id] or [PrimaryKey] attributes.");
        }

        if (primaryKeys.Count > 0)
        {
            var seenPkOrders = new HashSet<int>();
            foreach (var pk in primaryKeys)
            {
                if (pk.PkOrder <= 0 || !seenPkOrders.Add(pk.PkOrder))
                {
                    throw new InvalidOperationException($"Type {type.Name} has invalid PrimaryKey order values (must be unique and > 0).");
                }
            }
        }

        foreach (var c in tableInfo.Columns.Values)
        {
            var propType = c.PropertyInfo.PropertyType;
            if (c.IsEnum && !propType.IsEnum)
            {
                throw new InvalidOperationException($"[EnumColumn] on non-enum property {type.Name}.{c.PropertyInfo.Name}");
            }

            if (!c.IsEnum && propType.IsEnum)
            {
                throw new InvalidOperationException($"Enum property {type.Name}.{c.PropertyInfo.Name} must be annotated with [EnumColumn].");
            }

            if (c.IsJsonType && c.DbType != DbType.String)
            {
                throw new InvalidOperationException($"[Json] column {type.Name}.{c.PropertyInfo.Name} must use DbType.String or a JSON-capable DbType.");
            }
        }

        tableInfo.HasAuditColumns = tableInfo.CreatedBy != null ||
                                    tableInfo.CreatedOn != null ||
                                    tableInfo.LastUpdatedBy != null ||
                                    tableInfo.LastUpdatedOn != null;

        var colsList = tableInfo.Columns.Values.ToList();
        var allZero = true;
        for (var i = 0; i < colsList.Count; i++)
        {
            if (colsList[i].Ordinal != 0)
            {
                allZero = false;
                break;
            }
        }

        if (allZero)
        {
            for (var i = 0; i < colsList.Count; i++)
            {
                colsList[i].Ordinal = i + 1;
            }
        }
        else
        {
            var seenOrdinals = new HashSet<int>();
            foreach (var col in colsList)
            {
                if (col.Ordinal != 0)
                {
                    if (!seenOrdinals.Add(col.Ordinal))
                    {
                        throw new InvalidOperationException($"Duplicate ColumnAttribute.Ordinal {col.Ordinal} in {type.Name}");
                    }
                }
            }
        }

        _typeMap[type] = tableInfo;
        return tableInfo;
    }

    public void Register<T>()
    {
        GetTableInfo<T>();
    }
}