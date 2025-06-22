#region

using System.Collections.Concurrent;
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

        if (!_typeMap.TryGetValue(type, out var tableInfo))
        {
            tableInfo = new TableInfo
            {
                Name = type.GetCustomAttribute<TableAttribute>()?.Name ??
                       throw new InvalidOperationException($"Type {type.Name} does not have a TableAttribute."),
                Schema = type.GetCustomAttribute<TableAttribute>()?.Schema ?? ""
            };

            foreach (var prop in type.GetProperties())
            {
                var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
                if (colAttr != null)
                {
                    var ci = new ColumnInfo
                    {
                        Name = colAttr.Name,
                        PropertyInfo = prop,
                        DbType = colAttr.Type,
                        IsNonUpdateable = prop.GetCustomAttribute<NonUpdateableAttribute>() != null,
                        IsId = prop.GetCustomAttribute<IdAttribute>() != null,
                        IsIdIsWritable = prop.GetCustomAttribute<IdAttribute>()?.Writable ?? true,
                        IsEnum = prop.GetCustomAttribute<EnumColumnAttribute>() != null,
                        EnumType = prop.GetCustomAttribute<EnumColumnAttribute>()?.EnumType,
                        IsJsonType = prop.GetCustomAttribute<JsonAttribute>() != null,
                        JsonSerializerOptions = prop.GetCustomAttribute<JsonAttribute>()?.SerializerOptions ??
                                                JsonSerializerOptions.Default,
                        IsPrimaryKey = prop.GetCustomAttribute<PrimaryKeyAttribute>() != null,
                        IsVersion = prop.GetCustomAttribute<VersionAttribute>() != null,
                        IsCreatedBy = prop.GetCustomAttribute<CreatedByAttribute>() != null,
                        IsCreatedOn = prop.GetCustomAttribute<CreatedOnAttribute>() != null,
                        IsLastUpdatedBy = prop.GetCustomAttribute<LastUpdatedByAttribute>() != null,
                        IsLastUpdatedOn = prop.GetCustomAttribute<LastUpdatedOnAttribute>() != null
                    };
                    if (ci.IsLastUpdatedBy) tableInfo.LastUpdatedBy = ci;

                    if (ci.IsLastUpdatedOn) tableInfo.LastUpdatedOn = ci;

                    if (ci.IsCreatedBy) tableInfo.CreatedBy = ci;

                    if (ci.IsCreatedOn) tableInfo.CreatedOn = ci;

                    tableInfo.Columns[colAttr.Name] = ci;
                    if (ci.IsId)
                    {
                        if (tableInfo.Id != null)
                            throw new TooManyColumns("Only one id is allowed.");

                        tableInfo.Id = ci;
                        if (ci.IsPrimaryKey)
                            throw new PrimaryKeyOnRowIdColumn(
                                "Not allowed to have primary key attribute on id column.");
                    }

                    if (ci.IsVersion)
                    {
                        if (tableInfo.Version != null) throw new TooManyColumns("Only one version is allowed.");

                        tableInfo.Version = ci;
                    }
                }
            }

            _typeMap[type] = tableInfo;
        }

        if (tableInfo.Columns.Count == 0)
            throw new NoColumnsFoundException("This POCO entity has no properties, marked as columns.");
        return tableInfo;
    }

    public void Register<T>()
    {
        GetTableInfo<T>();
    }
}