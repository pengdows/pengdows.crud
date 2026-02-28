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
using pengdows.crud.@internal;

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
        
        public List<IColumnInfo> UpsertColumns = null!;
        public List<string> UpsertParameterNames = null!;

        public string DeleteSql = null!;

        // UpdateSql split into prefix/suffix to allow direct StringBuilder appends,
        // eliminating the string.Format call and intermediate string allocation per UPDATE.
        public string UpdateSqlPrefix = null!; // "UPDATE tbl SET "
        public string UpdateSqlSuffix = null!; // " WHERE idcol = "

        // Pre-built version increment fragment (", vercol = vercol + 1") or null when not applicable.
        public string? VersionIncrementClause;

        // Pre-built upsert UPDATE SET fragment, e.g. "col = EXCLUDED.col, col2 = EXCLUDED.col2".
        // Null for dialects that don't support upsert or where it could not be pre-built.
        public string? UpsertUpdateFragment;

        public List<IColumnInfo> UpdateColumns = null!;

        // Pre-wrapped column names for UpdateColumns — eliminates per-call ConcurrentDictionary lookups
        // in BuildSetClause. Indexed in parallel with UpdateColumns.
        public string[] UpdateColumnWrappedNames = null!;

        // Pre-built single-ID WHERE body for equality retrieval: e.g., "\"Id\" = @p0"
        // Dialect-specific — eliminates per-call _queryCache lookup and cross-dialect collision.
        // Null when entity has no [Id] column.
        public string? IdEqualityWhereBody;

        // Pre-built single-ID WHERE body when a NULL may also match:
        // e.g., "(\"Id\" = @p0 OR \"Id\" IS NULL)"
        // Null when entity has no [Id] column.
        public string? IdEqualityNullableWhereBody;
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
            .OrderBy(c => c.Ordinal)
            .ToList();

        var wrappedCols = new List<string>(insertColumns.Count);
        for (var i = 0; i < insertColumns.Count; i++)
        {
            wrappedCols.Add(dialect.WrapSimpleName(insertColumns[i].Name));
        }

        var paramNames = new List<string>(insertColumns.Count);
        var valuePlaceholders = new List<string>(insertColumns.Count);
        for (var i = 0; i < insertColumns.Count; i++)
        {
            var name = $"i{i}";
            paramNames.Add(name);
            var placeholder = dialect.MakeParameterName(name);
            if (insertColumns[i].IsJsonType)
            {
                placeholder = dialect.RenderJsonArgument(placeholder, insertColumns[i]);
            }

            valuePlaceholders.Add(placeholder);
        }

        var insertSql =
            $"INSERT INTO {BuildWrappedTableName(dialect)} ({string.Join(", ", wrappedCols)}) VALUES ({string.Join(", ", valuePlaceholders)})";

        // Upsert columns: align with insertable columns (exclude non-insertable, non-writable Id, audit rules)
        var upsertColumns = insertColumns;
        var upsertParamNames = new List<string>(upsertColumns.Count);
        for (var i = 0; i < upsertColumns.Count; i++)
        {
            upsertParamNames.Add($"i{i}");
        }

        // Delete and Update SQL require an ID column; null when entity has no [Id]
        string? deleteSql = null;
        string? updateSqlPrefix = null;
        string? updateSqlSuffix = null;
        List<IColumnInfo>? updateColumns = null;

        if (idCol != null)
        {
            deleteSql =
                $"DELETE FROM {BuildWrappedTableName(dialect)} WHERE {dialect.WrapSimpleName(idCol.Name)} = {{0}}";

            updateColumns = _tableInfo.Columns.Values
                .Where(c => !c.IsId && !c.IsVersion && !c.IsNonUpdateable && !c.IsCreatedBy && !c.IsCreatedOn)
                .OrderBy(c => c.Ordinal)
                .ToList();

            // Split into prefix/suffix for direct StringBuilder appends — eliminates string.Format per call
            updateSqlPrefix = $"UPDATE {BuildWrappedTableName(dialect)} SET ";
            updateSqlSuffix = $" WHERE {dialect.WrapSimpleName(idCol.Name)} = ";
        }

        // Pre-build single-ID equality WHERE body — dialect-specific, stored in CachedSqlTemplates
        // to avoid cross-dialect collision that would occur with a shared _queryCache key.
        string? idEqualityWhereBody = null;
        string? idEqualityNullableWhereBody = null;
        if (idCol != null)
        {
            var wrappedIdName = dialect.WrapSimpleName(idCol.Name);
            var pName = dialect.MakeParameterName("p0");
            idEqualityWhereBody = string.Concat(wrappedIdName, " = ", pName);
            idEqualityNullableWhereBody =
                $"({wrappedIdName} = {pName}{SqlFragments.Or}{wrappedIdName} IS NULL)";
        }

        // Cache version increment clause; null for byte[] (rowversion/timestamp — DB handles increment)
        string? versionIncrementClause = null;
        if (_versionColumn != null && _versionColumn.PropertyInfo.PropertyType != typeof(byte[]))
        {
            versionIncrementClause =
                $", {dialect.WrapSimpleName(_versionColumn.Name)} = {dialect.WrapSimpleName(_versionColumn.Name)} + 1";
        }

        // Upsert should never update conflict key columns (e.g., Oracle MERGE forbids it).
        var upsertKeyColumns = _tableInfo.PrimaryKeys.Count > 0
            ? _tableInfo.PrimaryKeys
            : _idColumn != null
                ? new[] { _idColumn }
                : Array.Empty<IColumnInfo>();
        HashSet<IColumnInfo>? upsertKeySet = upsertKeyColumns.Count > 0
            ? new HashSet<IColumnInfo>(upsertKeyColumns)
            : null;

        // Pre-build the upsert UPDATE SET fragment (deterministic per dialect+entity+auditResolver config).
        // Eliminates the per-call SbLite loop in BuildUpsertOnConflict/OnDuplicate/Merge.
        string? upsertUpdateFragment = null;
        if (updateColumns != null)
        {
            var frag = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
            try
            {
                if (dialect.SupportsMerge)
                {
                    var tp = dialect.MergeUpdateRequiresTargetAlias ? "t." : "";
                    foreach (var col in updateColumns)
                    {
                        if (upsertKeySet?.Contains(col) == true) continue;
                        if (_auditValueResolver == null && col.IsLastUpdatedBy) continue;
                        if (frag.Length > 0) frag.Append(", ");
                        frag.Append(tp);
                        frag.Append(dialect.WrapSimpleName(col.Name));
                        frag.Append(" = s.");
                        frag.Append(dialect.WrapSimpleName(col.Name));
                    }

                    if (_versionColumn != null && _versionColumn.PropertyInfo.PropertyType != typeof(byte[]))
                    {
                        frag.Append(", ");
                        frag.Append(tp);
                        frag.Append(dialect.WrapSimpleName(_versionColumn.Name));
                        frag.Append(" = ");
                        frag.Append(tp);
                        frag.Append(dialect.WrapSimpleName(_versionColumn.Name));
                        frag.Append(" + 1");
                    }
                }
                else
                {
                    // ON CONFLICT (PostgreSQL/CockroachDB) or ON DUPLICATE KEY UPDATE (MySQL/MariaDB)
                    try
                    {
                        foreach (var col in updateColumns)
                        {
                            if (upsertKeySet?.Contains(col) == true) continue;
                            if (_auditValueResolver == null && col.IsLastUpdatedBy) continue;
                            if (frag.Length > 0) frag.Append(", ");
                            frag.Append(dialect.WrapSimpleName(col.Name));
                            frag.Append(" = ");
                            frag.Append(dialect.UpsertIncomingColumn(col.Name));
                        }

                        if (_versionColumn != null && _versionColumn.PropertyInfo.PropertyType != typeof(byte[]))
                        {
                            frag.Append(", ");
                            frag.Append(dialect.WrapSimpleName(_versionColumn.Name));
                            frag.Append(" = ");
                            frag.Append(dialect.WrapSimpleName(_versionColumn.Name));
                            frag.Append(" + 1");
                        }
                    }
                    catch (NotSupportedException)
                    {
                        // Dialect doesn't support upsert (e.g., FakeDb default dialect)
                        frag.Clear();
                    }
                }

                if (frag.Length > 0)
                {
                    upsertUpdateFragment = frag.ToString();
                }
            }
            finally
            {
                frag.Dispose();
            }
        }

        // Pre-wrap UpdateColumn names to eliminate per-call ConcurrentDictionary lookups in BuildSetClause.
        string[] updateColumnWrappedNames;
        if (updateColumns != null)
        {
            updateColumnWrappedNames = new string[updateColumns.Count];
            for (var i = 0; i < updateColumns.Count; i++)
            {
                updateColumnWrappedNames[i] = dialect.WrapSimpleName(updateColumns[i].Name);
            }
        }
        else
        {
            updateColumnWrappedNames = Array.Empty<string>();
        }

        return new CachedSqlTemplates
        {
            InsertSql = insertSql,
            InsertColumns = insertColumns,
            InsertParameterNames = paramNames,
            UpsertColumns = upsertColumns,
            UpsertParameterNames = upsertParamNames,
            DeleteSql = deleteSql!,
            UpdateSqlPrefix = updateSqlPrefix!,
            UpdateSqlSuffix = updateSqlSuffix!,
            VersionIncrementClause = versionIncrementClause,
            UpsertUpdateFragment = upsertUpdateFragment,
            UpdateColumns = updateColumns!,
            UpdateColumnWrappedNames = updateColumnWrappedNames,
            IdEqualityWhereBody = idEqualityWhereBody,
            IdEqualityNullableWhereBody = idEqualityNullableWhereBody
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
