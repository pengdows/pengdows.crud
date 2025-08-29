#region

using System.Collections.Concurrent;
using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using pengdows.crud.attributes;
using pengdows.crud.exceptions;

#endregion

namespace pengdows.crud;

public sealed class TypeMapRegistry : ITypeMapRegistry
{
    private readonly ConcurrentDictionary<Type, TableInfo> _typeMap = new();

    public static TypeMapRegistry Instance { get; } = new();

    public TypeMapRegistry()
    {
    }

    public void Clear()
    {
        _typeMap.Clear();
    }

    public ITableInfo GetTableInfo<T>()
    {
        var type = typeof(T);
        return _typeMap.GetOrAdd(type, BuildTableInfo);
    }

    public void Register<T>() => GetTableInfo<T>();

    // ------------------ build pipeline ------------------

    private TableInfo BuildTableInfo(Type entityType)
    {
        var tattr = entityType.GetCustomAttribute<TableAttribute>()
                   ?? throw new InvalidOperationException(
                       $"Type {entityType.Name} does not have a TableAttribute.");

        var tableInfo = new TableInfo
        {
            Name = tattr.Name,
            Schema = tattr.Schema ?? string.Empty
        };

        foreach (var prop in entityType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            ProcessProperty(entityType, prop, tableInfo);
        }

        if (tableInfo.Columns.Count == 0)
        {
            throw new NoColumnsFoundException($"This POCO entity {entityType.Name} has no properties, marked as columns.");
        }

        ValidatePrimaryKeys(entityType, tableInfo);
        tableInfo.HasAuditColumns = HasAuditColumns(tableInfo);
        AssignOrdinals(entityType, tableInfo);

        return tableInfo;
    }

    // ------------------ helpers ------------------

    private static void ProcessProperty(Type entityType, PropertyInfo prop, TableInfo tableInfo)
    {
        var attrs = prop.GetCustomAttributes(inherit: true);

        static TAttr? A<TAttr>(object[] atts) where TAttr : Attribute =>
            (TAttr?)atts.FirstOrDefault(a => a is TAttr);

        var colAttr = A<ColumnAttribute>(attrs);
        if (colAttr == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(colAttr.Name))
        {
            throw new InvalidOperationException(
                $"ColumnAttribute.Name cannot be null/empty on {entityType.FullName}.{prop.Name}");
        }

        var idAttr   = A<IdAttribute>(attrs);
        var pkAttr   = A<PrimaryKeyAttribute>(attrs);
        var enumAttr = A<EnumColumnAttribute>(attrs);
        var jsonAttr = A<JsonAttribute>(attrs);
        var nonIns   = A<NonInsertableAttribute>(attrs);
        var nonUpd   = A<NonUpdateableAttribute>(attrs);
        var verAttr  = A<VersionAttribute>(attrs);
        var cby      = A<CreatedByAttribute>(attrs);
        var con      = A<CreatedOnAttribute>(attrs);
        var lby      = A<LastUpdatedByAttribute>(attrs);
        var lon      = A<LastUpdatedOnAttribute>(attrs);

        var isId = idAttr != null;
        var isIdWritable = idAttr?.Writable ?? true;
        
        
        var ci = new ColumnInfo
        {
            Name               = colAttr.Name,
            PropertyInfo       = prop,
            DbType             = colAttr.Type,
            Ordinal            = colAttr.Ordinal,
            IsId               = isId,
            IsIdIsWritable     = isId && isIdWritable && nonIns == null,
            IsNonInsertable    = nonIns != null || (isId && !isIdWritable),
            IsNonUpdateable    = nonUpd != null || isId,
            IsPrimaryKey       = pkAttr != null,
            PkOrder            = pkAttr?.Order ?? 0,
            IsVersion          = verAttr != null,
            IsCreatedBy        = cby != null,
            IsCreatedOn        = con != null,
            IsLastUpdatedBy    = lby != null,
            IsLastUpdatedOn    = lon != null,
            // Only treat as enum when [EnumColumn] is present; plain enum properties are allowed but not special-cased.
            IsEnum             = enumAttr != null,
            EnumType           = enumAttr?.EnumType,     
            IsJsonType         = jsonAttr != null,
            JsonSerializerOptions = jsonAttr?.SerializerOptions != null
                ? new JsonSerializerOptions(jsonAttr.SerializerOptions)
                : new JsonSerializerOptions()
        };

        ConfigureEnumColumn(entityType, prop, ci);
        AttachAuditReferences(tableInfo, ci);
        AddColumnToMap(entityType, prop, tableInfo, ci);
        CaptureSpecialColumns(entityType, tableInfo, ci);
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
            tableInfo.CreatedBy     = ci;
        }

        if (ci.IsCreatedOn)
        {
            tableInfo.CreatedOn     = ci;
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

        var seen = new HashSet<int>();
        foreach (var pk in pks)
        {
            if (pk.PkOrder <= 0 || !seen.Add(pk.PkOrder))
            {
                throw new InvalidOperationException(
                    $"Type {entityType.FullName} has invalid PrimaryKey order values (must be unique and > 0).");
            }
        }
    }

    private static bool HasAuditColumns(TableInfo t) =>
        t.CreatedBy != null || t.CreatedOn != null || t.LastUpdatedBy != null || t.LastUpdatedOn != null;

    private static void AssignOrdinals(Type entityType, TableInfo tableInfo)
    {
        var colsList = tableInfo.Columns.Values.ToList();

        if (colsList.Any(c => c.Ordinal < 0))
        {
            throw new InvalidOperationException(
                $"Negative ColumnAttribute.Ordinal detected in {entityType.FullName}");
        }

        var allZero = colsList.All(c => c.Ordinal == 0);

        if (allZero)
        {
            for (var i = 0; i < colsList.Count; i++)
            {
                colsList[i].Ordinal = i + 1;
            }

            return;
        }

        var seen = new HashSet<int>();
        foreach (var c in colsList)
        {
            if (c.Ordinal != 0 && !seen.Add(c.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Duplicate ColumnAttribute.Ordinal {c.Ordinal} in {entityType.FullName}");
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsStringDbType(DbType dbType) =>
        dbType is DbType.String or DbType.AnsiString or DbType.StringFixedLength or DbType.AnsiStringFixedLength;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNumericDbType(DbType dbType) =>
        dbType is DbType.Byte or DbType.SByte
            or DbType.Int16 or DbType.Int32 or DbType.Int64
            or DbType.UInt16 or DbType.UInt32 or DbType.UInt64
            or DbType.Decimal or DbType.VarNumeric;
}
