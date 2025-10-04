using System.Runtime.CompilerServices;
using pengdows.crud.dialects;

namespace pengdows.crud;

public partial class EntityHelper<TEntity, TRowID>
{
    private class CachedSqlTemplates
    {
        public string InsertSql = null!;
        public List<IColumnInfo> InsertColumns = null!;
        public List<string> InsertParameterNames = null!;
        public string DeleteSql = null!;
        public string UpdateSql = null!;
        public List<IColumnInfo> UpdateColumns = null!;
    }

    private class CachedContainerTemplates
    {
        public ISqlContainer GetByIdTemplate = null!;
        public ISqlContainer GetByIdsTemplate = null!;
        public ISqlContainer InsertTemplate = null!;
        public ISqlContainer UpdateTemplate = null!;
        public ISqlContainer DeleteByIdTemplate = null!;
        public ISqlContainer BaseRetrieveTemplate = null!;
        public ISqlContainer UpsertTemplate = null!;
    }

    private static TRowID CreateTemplateRowId()
    {
        if (typeof(TRowID).IsValueType)
        {
            return default!;
        }

        if (typeof(TRowID) == typeof(string))
        {
            return (TRowID)(object)string.Empty;
        }

        if (typeof(TRowID).IsArray)
        {
            var elementType = typeof(TRowID).GetElementType() ?? typeof(object);
            var instance = Array.CreateInstance(elementType, 1);
            return (TRowID)(object)instance;
        }

        try
        {
            return (TRowID)Activator.CreateInstance(typeof(TRowID), nonPublic: true)!;
        }
        catch
        {
            try
            {
                return (TRowID)RuntimeHelpers.GetUninitializedObject(typeof(TRowID));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Unable to create a template identifier instance for {typeof(TRowID)}.", ex);
            }
        }
    }

    private static IReadOnlyCollection<TRowID> CreateTemplateRowIds(int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var values = new TRowID[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = CreateTemplateRowId();
        }

        return values;
    }

    /// <summary>
    /// Dialect-Specific SQL Template Caching
    /// 
    /// This optimization caches SQL templates directly for each database dialect,
    /// eliminating runtime token replacement and string transformation overhead.
    /// Templates are built once per dialect and reused for all subsequent operations.
    /// 
    /// Benefits:
    /// 1. Zero transformation cost - templates are ready to use
    /// 2. Reduced memory allocations from string operations
    /// 3. Better cache locality per database type
    /// 4. Simpler code path without regex and string replacements
    /// </summary>

    private CachedSqlTemplates BuildCachedSqlTemplatesForDialect(ISqlDialect dialect)
    {
        var idCol = _tableInfo.Columns.Values.FirstOrDefault(c => c.IsId)
                     ?? throw new InvalidOperationException($"No ID column defined for {typeof(TEntity).Name}");

        var insertColumns = _tableInfo.Columns.Values
            .Where(c => !c.IsNonInsertable && (!c.IsId || c.IsIdIsWritable))
            .Where(c => _auditValueResolver != null || (!c.IsCreatedBy && !c.IsLastUpdatedBy))
            .ToList();

        var wrappedCols = new List<string>();
        for (var i = 0; i < insertColumns.Count; i++)
        {
            wrappedCols.Add(dialect.WrapObjectName(insertColumns[i].Name));
        }

        var paramNames = new List<string>();
        var valuePlaceholders = new List<string>();
        for (var i = 0; i < insertColumns.Count; i++)
        {
            var name = $"i{i}";
            paramNames.Add(name);
            var placeholder = dialect.SupportsNamedParameters 
                ? dialect.ParameterMarker + name
                : dialect.ParameterMarker;
            valuePlaceholders.Add(placeholder);
        }

        var insertSql =
            $"INSERT INTO {BuildWrappedTableName(dialect)} ({string.Join(", ", wrappedCols)}) VALUES ({string.Join(", ", valuePlaceholders)})";
        var deleteSql =
            $"DELETE FROM {BuildWrappedTableName(dialect)} WHERE {dialect.WrapObjectName(idCol.Name)} = {{0}}";

        var updateColumns = _tableInfo.Columns.Values
            .Where(c => !c.IsId && !c.IsVersion && !c.IsNonUpdateable && !c.IsCreatedBy && !c.IsCreatedOn)
            .ToList();

        var updateSql =
            $"UPDATE {BuildWrappedTableName(dialect)} SET {{0}} WHERE {dialect.WrapObjectName(idCol.Name)} = {{1}}";

        return new CachedSqlTemplates
        {
            InsertSql = insertSql,
            InsertColumns = insertColumns,
            InsertParameterNames = paramNames,
            DeleteSql = deleteSql,
            UpdateSql = updateSql,
            UpdateColumns = updateColumns
        };
    }

    private CachedSqlTemplates GetTemplatesForDialect(ISqlDialect dialect)
    {
        return _templatesByDialect
            .GetOrAdd(dialect.DatabaseType, _ => new Lazy<CachedSqlTemplates>(() =>
                BuildCachedSqlTemplatesForDialect(dialect)))
            .Value;
    }

    private CachedContainerTemplates BuildCachedContainerTemplatesForDialect(ISqlDialect dialect, IDatabaseContext context)
    {
        // Build pre-configured containers with parameters for common operations
        var templates = new CachedContainerTemplates();
        
        // GetById - single ID parameter
        var singleIdTemplateCount = dialect.SupportsSetValuedParameters ? 2 : 1;
        templates.GetByIdTemplate = BuildRetrieveInternal(CreateTemplateRowIds(singleIdTemplateCount), "", context, deduplicate: false);

        // GetByIds - array parameter (will be updated with actual IDs)
        templates.GetByIdsTemplate = BuildRetrieveInternal(CreateTemplateRowIds(2), "", context, deduplicate: false);
        
        // BaseRetrieve - no WHERE clause
        templates.BaseRetrieveTemplate = BuildBaseRetrieve("a", context);
        
        // Insert - entity fields as parameters
        var sampleEntity = new TEntity();
        templates.InsertTemplate = BuildCreate(sampleEntity, context);
        
        // Update - entity fields as parameters
        templates.UpdateTemplate = BuildUpdateAsync(sampleEntity, context).Result;
        
        // Delete by ID
        templates.DeleteByIdTemplate = BuildDelete(CreateTemplateRowId(), context);
        
        // Upsert
        templates.UpsertTemplate = BuildUpsert(sampleEntity, context);
        
        return templates;
    }

    private CachedContainerTemplates GetContainerTemplatesForDialect(ISqlDialect dialect, IDatabaseContext context)
    {
        return _containersByDialect
            .GetOrAdd(dialect.DatabaseType, _ => new Lazy<CachedContainerTemplates>(() =>
                BuildCachedContainerTemplatesForDialect(dialect, context)))
            .Value;
    }

}
