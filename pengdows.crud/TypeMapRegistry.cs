#region

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using pengdows.crud.attributes;
using pengdows.crud.exceptions;
using pengdows.crud.types.valueobjects;

#endregion

namespace pengdows.crud;

public sealed class TypeMapRegistry : ITypeMapRegistry
{
    private readonly ConcurrentDictionary<Type, TableInfo> _typeMap = new();

    public static TypeMapRegistry Instance { get; } = new();

    public void Clear()
    {
        _typeMap.Clear();
    }

    public ITableInfo GetTableInfo<T>()
    {
        var type = typeof(T);
        return _typeMap.GetOrAdd(type, BuildTableInfo);
    }

    public void Register<T>()
    {
        GetTableInfo<T>();
    }

    // ------------------ build pipeline ------------------

    private TableInfo BuildTableInfo(Type entityType)
    {
        var tattr = entityType.GetCustomAttribute<TableAttribute>()
                    ?? throw new InvalidOperationException(
                        $"Type {entityType.Name} does not have a TableAttribute.");

        var tableName = (tattr.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new InvalidOperationException($"TableAttribute.Name cannot be empty for {entityType.FullName}.");
        }

        var schemaName = (tattr.Schema ?? string.Empty).Trim();

        var tableInfo = new TableInfo
        {
            Name = tableName,
            Schema = schemaName
        };

        var discoveryOrder = new List<ColumnInfo>();
        foreach (var prop in entityType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .OrderBy(p => p.MetadataToken))
        {
            var column = ProcessProperty(entityType, prop, tableInfo);
            if (column != null)
            {
                discoveryOrder.Add(column);
            }
        }

        if (tableInfo.Columns.Count == 0)
        {
            throw new NoColumnsFoundException(
                $"This POCO entity {entityType.Name} has no properties, marked as columns.");
        }

        ValidatePrimaryKeys(entityType, tableInfo);
        tableInfo.HasAuditColumns = HasAuditColumns(tableInfo);
        AssignOrdinals(entityType, tableInfo, discoveryOrder);
        ValidateAuditAndVersionTypes(entityType, tableInfo);

        return tableInfo;
    }

    // ------------------ helpers ------------------

    private static ColumnInfo? ProcessProperty(Type entityType, PropertyInfo prop, TableInfo tableInfo)
    {
        var attrs = prop.GetCustomAttributes(true);

        static TAttr? A<TAttr>(object[] atts) where TAttr : Attribute
        {
            return (TAttr?)atts.FirstOrDefault(a => a is TAttr);
        }

        var colAttr = A<ColumnAttribute>(attrs);
        if (colAttr == null)
        {
            return null;
        }

        var columnName = (colAttr.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(columnName))
        {
            throw new InvalidOperationException(
                $"ColumnAttribute.Name cannot be null/empty on {entityType.FullName}.{prop.Name}");
        }

        var idAttr = A<IdAttribute>(attrs);
        var pkAttr = A<PrimaryKeyAttribute>(attrs);
        var enumAttr = A<EnumColumnAttribute>(attrs);
        var jsonAttr = A<JsonAttribute>(attrs);
        var nonIns = A<NonInsertableAttribute>(attrs);
        var nonUpd = A<NonUpdateableAttribute>(attrs);
        var verAttr = A<VersionAttribute>(attrs);
        var cby = A<CreatedByAttribute>(attrs);
        var con = A<CreatedOnAttribute>(attrs);
        var lby = A<LastUpdatedByAttribute>(attrs);
        var lon = A<LastUpdatedOnAttribute>(attrs);

        var isId = idAttr != null;
        var isIdWritable = idAttr?.Writable ?? true;


        var effectivePropertyType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

        var ci = new ColumnInfo
        {
            Name = columnName,
            PropertyInfo = prop,
            DbType = colAttr.Type,
            Ordinal = colAttr.Ordinal,
            IsId = isId,
            IsIdIsWritable = isId && isIdWritable && nonIns == null,
            IsNonInsertable = nonIns != null || (isId && !isIdWritable),
            IsNonUpdateable = nonUpd != null || isId,
            IsPrimaryKey = pkAttr != null,
            PkOrder = pkAttr?.Order ?? 0,
            IsVersion = verAttr != null,
            IsCreatedBy = cby != null,
            IsCreatedOn = con != null,
            IsLastUpdatedBy = lby != null,
            IsLastUpdatedOn = lon != null,
            JsonSerializerOptions = BuildJsonOptions(jsonAttr)
        };

        if (enumAttr != null)
        {
            ci.IsEnum = true;
            ci.EnumType = enumAttr.EnumType;
        }
        else if (effectivePropertyType.IsEnum)
        {
            ci.IsEnum = true;
            ci.EnumType = effectivePropertyType;
        }

        if (jsonAttr != null)
        {
            ci.IsJsonType = true;
        }
        else if (ShouldInferJson(effectivePropertyType))
        {
            ci.IsJsonType = true;
        }

        if (ci.IsJsonType && ci.IsVersion)
        {
            ci.IsJsonType = false;
        }

        if (ci.IsVersion)
        {
            ValidateVersionColumn(prop);
        }

        if (ci.IsLastUpdatedOn)
        {
            ValidateLastUpdatedOnColumn(prop);
        }

        // Compile a fast getter delegate for this column: (object o) => (object)((TEntity)o).Prop
        try
        {
            var objParam = Expression.Parameter(typeof(object), "o");
            var castObj = Expression.Convert(objParam, entityType);
            var propAccess = Expression.Property(castObj, prop);
            var box = Expression.Convert(propAccess, typeof(object));
            var lambda = Expression.Lambda<Func<object, object?>>(box, objParam);
            ci.FastGetter = lambda.Compile();
        }
        catch
        {
            // Fallback to reflection when expression compilation is not possible
            ci.FastGetter = null;
        }

        ConfigureEnumColumn(entityType, prop, ci);
        AttachAuditReferences(tableInfo, ci);
        AddColumnToMap(entityType, prop, tableInfo, ci);
        CaptureSpecialColumns(entityType, tableInfo, ci);

        return ci;
    }

    private static JsonSerializerOptions BuildJsonOptions(JsonAttribute? jsonAttr)
    {
        var options = jsonAttr?.SerializerOptions != null
            ? new JsonSerializerOptions(jsonAttr.SerializerOptions)
            : new JsonSerializerOptions();

        options.PropertyNameCaseInsensitive = true;
        return options;
    }

    private static void ConfigureEnumColumn(Type entityType, PropertyInfo prop, ColumnInfo ci)
    {
        if (!ci.IsEnum || ci.EnumType == null)
        {
            return;
        }

        ci.EnumUnderlyingType = Enum.GetUnderlyingType(ci.EnumType);
        ci.EnumAsString = IsStringDbType(ci.DbType);

        if (!ci.EnumAsString && !IsNumericDbType(ci.DbType))
        {
            throw new InvalidOperationException(
                $"Enum column {entityType.FullName}.{prop.Name} must use string or numeric DbType; found {ci.DbType}.");
        }
    }

    private static void AttachAuditReferences(TableInfo tableInfo, ColumnInfo ci)
    {
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
    }

    private static void AddColumnToMap(Type entityType, PropertyInfo prop, TableInfo tableInfo, ColumnInfo ci)
    {
        if (tableInfo.Columns.ContainsKey(ci.Name))
        {
            throw new InvalidOperationException(
                $"Duplicate [Column(\"{ci.Name}\")] on {entityType.FullName}.{prop.Name}");
        }

        tableInfo.Columns[ci.Name] = ci;
    }

    private static void CaptureSpecialColumns(Type entityType, TableInfo tableInfo, ColumnInfo ci)
    {
        if (ci.IsId)
        {
            if (tableInfo.Id != null)
            {
                throw new TooManyColumns($"Multiple [Id] detected on {entityType.FullName}.");
            }

            tableInfo.Id = ci;

            if (ci.IsPrimaryKey)
            {
                throw new PrimaryKeyOnRowIdColumn(
                    $"[PrimaryKey] is not allowed on Id column {entityType.FullName}.{ci.PropertyInfo.Name}.");
            }
        }

        if (ci.IsVersion)
        {
            if (tableInfo.Version != null)
            {
                throw new TooManyColumns($"Multiple [Version] detected on {entityType.FullName}.");
            }

            tableInfo.Version = ci;
        }
    }

    private static void ValidatePrimaryKeys(Type entityType, TableInfo tableInfo)
    {
        var hasId = tableInfo.Id != null;
        var pks = tableInfo.Columns.Values.Where(c => c.IsPrimaryKey).ToList();

        if (!hasId && pks.Count == 0)
        {
            throw new InvalidOperationException(
                $"Type {entityType.FullName} must define either [Id] or [PrimaryKey].");
        }

        if (pks.Count == 0)
        {
            return;
        }

        var specified = pks.Where(k => k.PkOrder > 0).ToList();
        if (specified.Count > 0 && specified.Count != pks.Count)
        {
            throw new InvalidOperationException(
                $"Type {entityType.FullName} mixes PrimaryKey definitions with and without explicit Order values. Specify Order on all or none.");
        }

        if (specified.Count > 0)
        {
            var seen = new HashSet<int>();
            foreach (var key in specified)
            {
                if (!seen.Add(key.PkOrder))
                {
                    throw new InvalidOperationException(
                        $"Type {entityType.FullName} has duplicate PrimaryKey order value {key.PkOrder}.");
                }
            }

            var expectedCount = pks.Count;
            if (seen.Min() != 1 || seen.Max() != expectedCount || seen.Count != expectedCount)
            {
                throw new InvalidOperationException(
                    $"PrimaryKey orders for {entityType.FullName} must form a contiguous sequence starting at 1.");
            }

            return;
        }

        // No explicit orders supplied: assign sequentially in discovery order
        for (var i = 0; i < pks.Count; i++)
        {
            pks[i].PkOrder = i + 1;
        }
    }

    private static bool HasAuditColumns(TableInfo t)
    {
        return t.CreatedBy != null || t.CreatedOn != null || t.LastUpdatedBy != null || t.LastUpdatedOn != null;
    }

    private static void ValidateVersionColumn(PropertyInfo property)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (type == typeof(RowVersion))
        {
            return;
        }

        if (type == typeof(byte[]))
        {
            return;
        }

        switch (Type.GetTypeCode(type))
        {
            case TypeCode.Byte:
            case TypeCode.SByte:
            case TypeCode.Int16:
            case TypeCode.Int32:
            case TypeCode.Int64:
            case TypeCode.UInt16:
            case TypeCode.UInt32:
            case TypeCode.UInt64:
                return;
            default:
                throw new InvalidOperationException(
                    $"Property {property.DeclaringType?.FullName}.{property.Name} marked with [Version] must be a byte array or integral type.");
        }
    }

    private static void ValidateLastUpdatedOnColumn(PropertyInfo property)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (type != typeof(DateTime) && type != typeof(DateTimeOffset))
        {
            throw new InvalidOperationException(
                $"Property {property.DeclaringType?.FullName}.{property.Name} marked with [LastUpdatedOn] must be DateTime or DateTimeOffset.");
        }
    }

    private static void AssignOrdinals(Type entityType, TableInfo tableInfo, IReadOnlyList<ColumnInfo> discoveryOrder)
    {
        if (discoveryOrder.Count == 0)
        {
            return;
        }

        foreach (var column in discoveryOrder)
        {
            if (column.Ordinal < 0)
            {
                throw new InvalidOperationException(
                    $"Negative ColumnAttribute.Ordinal detected in {entityType.FullName}");
            }
        }

        var specified = new HashSet<int>();
        foreach (var column in discoveryOrder)
        {
            if (column.Ordinal > 0 && !specified.Add(column.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Duplicate ColumnAttribute.Ordinal {column.Ordinal} in {entityType.FullName}");
            }
        }

        var nextOrdinal = 1;
        foreach (var column in discoveryOrder)
        {
            if (column.Ordinal > 0)
            {
                continue;
            }

            while (specified.Contains(nextOrdinal))
            {
                nextOrdinal++;
            }

            column.Ordinal = nextOrdinal;
            specified.Add(nextOrdinal);
            nextOrdinal++;
        }
    }

    private static void ValidateAuditAndVersionTypes(Type entityType, TableInfo tableInfo)
    {
        ValidateUserColumn(entityType, tableInfo.CreatedBy);
        ValidateUserColumn(entityType, tableInfo.LastUpdatedBy);
        ValidateTimestampColumn(entityType, tableInfo.CreatedOn);
        ValidateTimestampColumn(entityType, tableInfo.LastUpdatedOn);
        ValidateVersionType(entityType, tableInfo.Version);
    }

    private static void ValidateUserColumn(Type entityType, IColumnInfo? column)
    {
        if (column == null)
        {
            return;
        }

        var propertyType = Nullable.GetUnderlyingType(column.PropertyInfo.PropertyType) ??
                           column.PropertyInfo.PropertyType;
        if (propertyType != typeof(string) && propertyType != typeof(Guid))
        {
            throw new InvalidOperationException(
                $"Property {entityType.FullName}.{column.PropertyInfo.Name} must be a string or Guid.");
        }
    }

    private static void ValidateTimestampColumn(Type entityType, IColumnInfo? column)
    {
        if (column == null)
        {
            return;
        }

        var propertyType = Nullable.GetUnderlyingType(column.PropertyInfo.PropertyType) ??
                           column.PropertyInfo.PropertyType;
        if (propertyType != typeof(DateTime) && propertyType != typeof(DateTimeOffset))
        {
            throw new InvalidOperationException(
                $"Property {entityType.FullName}.{column.PropertyInfo.Name} must be DateTime or DateTimeOffset.");
        }
    }

    private static void ValidateVersionType(Type entityType, IColumnInfo? column)
    {
        if (column == null)
        {
            return;
        }

        var propertyType = Nullable.GetUnderlyingType(column.PropertyInfo.PropertyType) ??
                           column.PropertyInfo.PropertyType;
        if (propertyType == typeof(byte[]))
        {
            return;
        }

        switch (Type.GetTypeCode(propertyType))
        {
            case TypeCode.Byte:
            case TypeCode.SByte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
            case TypeCode.Int64:
            case TypeCode.UInt64:
                return;
            default:
                throw new InvalidOperationException(
                    $"Property {entityType.FullName}.{column.PropertyInfo.Name} marked with [Version] must be an integer or byte array.");
        }
    }

    private static bool ShouldInferJson(Type type)
    {
        if (type == typeof(JsonDocument) || type == typeof(JsonElement) || type == typeof(types.valueobjects.JsonValue))
        {
            return true;
        }

        if (typeof(JsonNode).IsAssignableFrom(type))
        {
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsStringDbType(DbType dbType)
    {
        return dbType is DbType.String or DbType.AnsiString or DbType.StringFixedLength or DbType.AnsiStringFixedLength;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNumericDbType(DbType dbType)
    {
        return dbType is DbType.Byte or DbType.SByte
            or DbType.Int16 or DbType.Int32 or DbType.Int64
            or DbType.UInt16 or DbType.UInt32 or DbType.UInt64
            or DbType.Decimal or DbType.VarNumeric;
    }
}