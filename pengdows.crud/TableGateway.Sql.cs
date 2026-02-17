// =============================================================================
// FILE: TableGateway.Sql.cs
// PURPOSE: SQL template generation and caching infrastructure.
//
// AI SUMMARY:
// - Contains cached SQL template classes:
//   * CachedSqlTemplates - Pre-built INSERT, UPDATE, DELETE SQL strings
//   * CachedContainerTemplates - Pre-configured SqlContainer instances
// - SQL templates are cached per dialect (SQL Server, PostgreSQL, etc.).
// - Template building methods:
//   * BuildWrappedTableName() - Schema.Table with proper quoting
//   * GetOrBuildTemplates() - Lazy template initialization
// - Helper methods for column lists and parameter naming.
// - CreateTemplateRowId() - Creates placeholder ID for template building.
// - Performance: Templates built once per dialect, then cloned for use.
// =============================================================================

using System.Runtime.CompilerServices;
using pengdows.crud.dialects;

namespace pengdows.crud;

/// <summary>
/// TableGateway partial: SQL template generation and caching.
/// </summary>
public partial class TableGateway<TEntity, TRowID>
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
        public ISqlContainer? GetByIdTemplate;
        public ISqlContainer? GetByIdsTemplate;
        public ISqlContainer InsertTemplate = null!;
        public ISqlContainer? DeleteByIdTemplate;
        public ISqlContainer BaseRetrieveTemplate = null!;
        // Update and Upsert cannot be cached as templates because NULL inlining
        // in BuildSetClause makes the parameter structure vary per entity instance.
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
            return (TRowID)Activator.CreateInstance(typeof(TRowID), true)!;
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
        var idCol = _tableInfo.Columns.Values.FirstOrDefault(c => c.IsId);

        var insertColumns = _tableInfo.Columns.Values
            .Where(c => !c.IsNonInsertable && (!c.IsId || c.IsIdIsWritable))
            .Where(c => _auditValueResolver != null || (!c.IsCreatedBy && !c.IsLastUpdatedBy))
            .ToList();

        var wrappedCols = new List<string>(insertColumns.Count);
        for (var i = 0; i < insertColumns.Count; i++)
        {
            wrappedCols.Add(dialect.WrapObjectName(insertColumns[i].Name));
        }

        var paramNames = new List<string>(insertColumns.Count);
        var valuePlaceholders = new List<string>(insertColumns.Count);
        for (var i = 0; i < insertColumns.Count; i++)
        {
            var name = $"i{i}";
            paramNames.Add(name);
            var placeholder = dialect.SupportsNamedParameters
                ? dialect.ParameterMarker + name
                : dialect.ParameterMarker;
            if (insertColumns[i].IsJsonType)
            {
                placeholder = dialect.RenderJsonArgument(placeholder, insertColumns[i]);
            }

            valuePlaceholders.Add(placeholder);
        }

        var insertSql =
            $"INSERT INTO {BuildWrappedTableName(dialect)} ({string.Join(", ", wrappedCols)}) VALUES ({string.Join(", ", valuePlaceholders)})";

        // Delete and Update SQL require an ID column; null when entity has no [Id]
        string? deleteSql = null;
        string? updateSql = null;
        List<IColumnInfo>? updateColumns = null;

        if (idCol != null)
        {
            deleteSql =
                $"DELETE FROM {BuildWrappedTableName(dialect)} WHERE {dialect.WrapObjectName(idCol.Name)} = {{0}}";

            updateColumns = _tableInfo.Columns.Values
                .Where(c => !c.IsId && !c.IsVersion && !c.IsNonUpdateable && !c.IsCreatedBy && !c.IsCreatedOn)
                .ToList();

            updateSql =
                $"UPDATE {BuildWrappedTableName(dialect)} SET {{0}} WHERE {dialect.WrapObjectName(idCol.Name)} = {{1}}";
        }

        return new CachedSqlTemplates
        {
            InsertSql = insertSql,
            InsertColumns = insertColumns,
            InsertParameterNames = paramNames,
            DeleteSql = deleteSql!,
            UpdateSql = updateSql!,
            UpdateColumns = updateColumns!
        };
    }

    private CachedSqlTemplates GetTemplatesForDialect(ISqlDialect dialect)
    {
        return _templatesByDialect
            .GetOrAdd(dialect.DatabaseType, _ => new Lazy<CachedSqlTemplates>(() =>
                BuildCachedSqlTemplatesForDialect(dialect)))
            .Value;
    }

    private CachedContainerTemplates BuildCachedContainerTemplatesForDialect(ISqlDialect dialect,
        IDatabaseContext context)
    {
        // Build pre-configured containers with parameters for common operations
        var templates = new CachedContainerTemplates();

        // ID-dependent templates are only built when the entity has an [Id] column
        if (_idColumn != null)
        {
            // GetById - always use single-parameter equality for minimal per-call overhead
            templates.GetByIdTemplate = BuildRetrieveInternal(CreateTemplateRowIds(1), "", context, false);

            // GetByIds - array parameter (will be updated with actual IDs)
            templates.GetByIdsTemplate = BuildRetrieveInternal(CreateTemplateRowIds(2), "", context, false);

            // Delete by ID - build directly to avoid circular dependency with BuildDelete fast path
            templates.DeleteByIdTemplate = BuildDeleteDirect(CreateTemplateRowId(), context);
        }

        // BaseRetrieve - build directly to avoid circular dependency with BuildBaseRetrieve fast path
        templates.BaseRetrieveTemplate = BuildBaseRetrieveDirect("a", context, dialect);

        // Insert - build directly to avoid circular dependency with PrepareInsertContainer fast path.
        // Skip MutateEntityForInsert since we only need the SQL structure and parameter names,
        // not real entity values (those are set via SetParameterValue on each clone).
        var sampleEntity = new TEntity();
        var sqlTemplate = GetTemplatesForDialect(dialect);
        var (insertContainer, _) = BuildInsertContainerDirect(sampleEntity, context, dialect, sqlTemplate);
        insertContainer.Query.Replace(OutputClausePlaceholder, string.Empty);
        insertContainer.Query.Replace(ReturningClausePlaceholder, string.Empty);
        templates.InsertTemplate = insertContainer;

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