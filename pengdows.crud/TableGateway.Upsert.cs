// =============================================================================
// FILE: TableGateway.Upsert.cs
// PURPOSE: UPSERT (INSERT or UPDATE) operations with database-specific syntax.
//
// AI SUMMARY:
// - UpsertAsync() - Inserts new or updates existing based on conflict key.
// - BuildUpsert() - Creates database-specific upsert SQL.
// - Conflict key selection:
//   1. [PrimaryKey] columns if any exist
//   2. [Id] column if writable ([Id(true)] or [Id])
//   3. Error if neither available
// - Database-specific syntax:
//   * SQL Server/Oracle/Snowflake: MERGE ... WHEN MATCHED [AND t.ver = s.ver] THEN UPDATE
//   * PostgreSQL/CockroachDB: INSERT ... ON CONFLICT DO UPDATE [WHERE table.ver = EXCLUDED.ver]
//   * MySQL/MariaDB: INSERT ... ON DUPLICATE KEY UPDATE (no version guard possible in this syntax)
//   * Firebird: UPDATE OR INSERT ... MATCHING (...)
// - Optimistic concurrency:
//   * MERGE dialects: WHEN MATCHED AND t.ver = s.ver guard; 0 rows = version mismatch → ConcurrencyConflictException
//   * ON CONFLICT WHERE dialects (PostgreSQL/CockroachDB): DO UPDATE WHERE predicate; 0 rows = DO NOTHING → exception
//   * ON DUPLICATE KEY (MySQL/MariaDB) and Firebird: cannot detect conflicts — no exception thrown
// - Handles audit columns and version columns appropriately.
// - Throws NotSupportedException for fallback/unknown dialects.
// - Returns affected row count (typically 1 for single-entity upsert).
// =============================================================================

using System.Data;
using System.Data.Common;
using pengdows.crud.@internal;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;

namespace pengdows.crud;

/// <summary>
/// TableGateway partial: UPSERT (INSERT or UPDATE) operations.
/// </summary>
public partial class TableGateway<TEntity, TRowID>
{
    /// <inheritdoc/>
    public async ValueTask<int> UpsertAsync(TEntity entity, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);

        // BuildUpsert creates a dynamic container - proper disposal required to avoid resource leaks
        // Use async disposal for async operations
        await using var sc = BuildUpsert(entity, ctx);
        var rowsAffected = await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);

        // Optimistic concurrency: throw only when the dialect enforced a version predicate in the SQL.
        // MERGE dialects (SQL Server/Oracle/Snowflake) use WHEN MATCHED AND t.ver=s.ver → 0 rows on mismatch.
        // ON CONFLICT WHERE dialects (PostgreSQL/CockroachDB) use DO UPDATE WHERE → DO NOTHING on mismatch.
        // Firebird UPDATE OR INSERT, MySQL ON DUPLICATE KEY, and non-WHERE ON CONFLICT (SQLite/DuckDB)
        // cannot detect version conflicts — do NOT throw for those dialects.
        if (rowsAffected == 0 && _versionColumn != null)
        {
            var canDetect = dialect.SupportsOnConflictWhere
                || (dialect.SupportsMerge && ctx.DataSourceInfo.Product != SupportedDatabase.Firebird);
            if (canDetect)
            {
                throw new ConcurrencyConflictException(
                    $"Concurrency conflict on {typeof(TEntity).Name}: version mismatch or row deleted.",
                    ctx.Product);
            }
        }

        return rowsAffected;
    }

    /// <inheritdoc/>
    public ISqlContainer BuildUpsert(TEntity entity, IDatabaseContext? context = null)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        var ctx = context ?? _context;
        _ = ResolveUpsertKey();

        if (ctx.DataSourceInfo.IsUsingFallbackDialect)
        {
            throw new NotSupportedException($"Upsert not supported for {ctx.Product}");
        }

        if (ctx.DataSourceInfo.SupportsMerge)
        {
            return BuildUpsertMerge(entity, ctx);
        }

        if (ctx.DataSourceInfo.SupportsInsertOnConflict)
        {
            return BuildUpsertOnConflict(entity, ctx);
        }

        if (ctx.DataSourceInfo.SupportsOnDuplicateKey)
        {
            return BuildUpsertOnDuplicate(entity, ctx);
        }

        throw new NotSupportedException($"Upsert not supported for {ctx.Product}");
    }

    private IReadOnlyList<IColumnInfo> ResolveUpsertKey()
    {
        var keys = _tableInfo.PrimaryKeys;
        if (keys.Count > 0)
        {
            return keys;
        }

        if (_idColumn != null && _idColumn.IsIdWritable)
        {
            return new List<IColumnInfo> { _idColumn };
        }

        throw new NotSupportedException(UpsertNoWritableKeyMessage);
    }

    private void PrepareForInsertOrUpsert(TEntity e)
    {
        if (_auditValueResolver != null)
        {
            SetAuditFields(e, false);
        }

        if (_versionColumn == null || _versionColumn.PropertyInfo.PropertyType == typeof(byte[]))
        {
            return;
        }

        var v = _versionColumn.MakeParameterValueFromField(e);
        if (v == null || Utils.IsZeroNumeric(v))
        {
            var t = Nullable.GetUnderlyingType(_versionColumn.PropertyInfo.PropertyType) ??
                    _versionColumn.PropertyInfo.PropertyType;
            _versionColumn.PropertyInfo.SetValue(e, TypeCoercionHelper.ConvertWithCache(1, t));
        }
    }

    /// <summary>
    /// Batch variant: applies pre-resolved audit values instead of calling Resolve() per entity.
    /// </summary>
    private void PrepareForInsertOrUpsert(TEntity e, IAuditValues? cachedAuditValues)
    {
        if (_auditValueResolver != null)
        {
            SetAuditFields(e, false, cachedAuditValues);
        }

        if (_versionColumn == null || _versionColumn.PropertyInfo.PropertyType == typeof(byte[]))
        {
            return;
        }

        var v = _versionColumn.MakeParameterValueFromField(e);
        if (v == null || Utils.IsZeroNumeric(v))
        {
            var t = Nullable.GetUnderlyingType(_versionColumn.PropertyInfo.PropertyType) ??
                    _versionColumn.PropertyInfo.PropertyType;
            _versionColumn.PropertyInfo.SetValue(e, TypeCoercionHelper.ConvertWithCache(1, t));
        }
    }

    private ISqlContainer BuildUpsertOnConflict(TEntity entity, IDatabaseContext context)
    {
        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);
        PrepareForInsertOrUpsert(entity);

        var template = GetTemplatesForDialect(dialect);
        var binder = GetOrBuildUpsertBinder(dialect, template);

        // Use pre-built column/value lists from template - NO MORE DYNAMIC LOOP
        var colSb = SbLite.Create(stackalloc char[512]);
        var valSb = SbLite.Create(stackalloc char[512]);
        try
        {
            for (var i = 0; i < template.UpsertColumns.Count; i++)
            {
                if (i > 0)
                {
                    colSb.Append(", ");
                    valSb.Append(", ");
                }

                colSb.Append(dialect.WrapSimpleName(template.UpsertColumns[i].Name));

                var pName = template.UpsertParameterNames[i];
                var placeholder = dialect.MakeParameterName(pName);
                if (template.UpsertColumns[i].IsJsonType)
                {
                    placeholder = dialect.RenderJsonArgument(placeholder, template.UpsertColumns[i]);
                }

                valSb.Append(placeholder);
            }

            var parameters = new List<DbParameter>(template.UpsertColumns.Count);
            binder(entity, parameters);

            var conflictCols = ResolveUpsertKey();

            var sc = ctx.CreateSqlContainer();
            sc.Query.Append("INSERT INTO ")
                .Append(BuildWrappedTableName(dialect))
                .Append(" (")
                .Append(colSb.AsSpan())
                .Append(") VALUES (")
                .Append(valSb.AsSpan())
                .Append(") ON CONFLICT (");

            for (var i = 0; i < conflictCols.Count; i++)
            {
                if (i > 0)
                {
                    sc.Query.Append(", ");
                }
                sc.Query.Append(dialect.WrapSimpleName(conflictCols[i].Name));
            }

            sc.Query.Append(") DO UPDATE SET ")
                .Append(template.UpsertUpdateFragment);

            if (_versionColumn != null && dialect.SupportsOnConflictWhere)
            {
                var wrappedVersion = dialect.WrapSimpleName(_versionColumn.Name);
                sc.Query.Append(" WHERE ")
                    .Append(BuildWrappedTableName(dialect))
                    .Append(".")
                    .Append(wrappedVersion)
                    .Append(" = ")
                    .Append("EXCLUDED.")
                    .Append(wrappedVersion);
            }

            sc.AddParameters(parameters);
            return sc;
        }
        finally
        {
            colSb.Dispose();
            valSb.Dispose();
        }
    }

    private ISqlContainer BuildUpsertOnDuplicate(TEntity entity, IDatabaseContext context)
    {
        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);

        PrepareForInsertOrUpsert(entity);

        var template = GetTemplatesForDialect(dialect);
        var binder = GetOrBuildUpsertBinder(dialect, template);

        // Use pre-built column/value lists from template - NO MORE DYNAMIC LOOP
        var colSb = SbLite.Create(stackalloc char[512]);
        var valSb = SbLite.Create(stackalloc char[512]);
        try
        {
            for (var i = 0; i < template.UpsertColumns.Count; i++)
            {
                if (i > 0)
                {
                    colSb.Append(", ");
                    valSb.Append(", ");
                }

                colSb.Append(dialect.WrapSimpleName(template.UpsertColumns[i].Name));

                var pName = template.UpsertParameterNames[i];
                var placeholder = dialect.MakeParameterName(pName);
                if (template.UpsertColumns[i].IsJsonType)
                {
                    placeholder = dialect.RenderJsonArgument(placeholder, template.UpsertColumns[i]);
                }

                valSb.Append(placeholder);
            }

            var parameters = new List<DbParameter>(template.UpsertColumns.Count);
            binder(entity, parameters);

            var sc = ctx.CreateSqlContainer();
            sc.Query.Append("INSERT INTO ")
                .Append(BuildWrappedTableName(dialect))
                .Append(" (")
                .Append(colSb.AsSpan())
                .Append(") VALUES (")
                .Append(valSb.AsSpan())
                .Append(")");

            var incomingAlias = dialect.UpsertIncomingAlias;
            if (!string.IsNullOrEmpty(incomingAlias))
            {
                sc.Query.Append(" AS ").Append(dialect.WrapSimpleName(incomingAlias));
            }

            sc.Query.Append(" ON DUPLICATE KEY UPDATE ")
                .Append(template.UpsertUpdateFragment);

            sc.AddParameters(parameters);
            return sc;
        }
        finally
        {
            colSb.Dispose();
            valSb.Dispose();
        }
    }

    private ISqlContainer BuildUpsertMerge(TEntity entity, IDatabaseContext context)
    {
        var ctx = context ?? _context;
        if (ctx.DataSourceInfo.Product == SupportedDatabase.Firebird)
        {
            return BuildFirebirdMergeUpsert(entity, ctx);
        }

        var dialect = GetDialect(ctx);

        PrepareForInsertOrUpsert(entity);

        var template = GetTemplatesForDialect(dialect);
        var binder = GetOrBuildUpsertBinder(dialect, template);

        var mergeSource = dialect.RenderMergeSource(template.UpsertColumns, template.UpsertParameterNames);

        var parameters = new List<DbParameter>(template.UpsertColumns.Count);
        binder(entity, parameters);

        // WHEN MATCHED arm must include version check — NOT the ON clause.
        // Putting version in ON: stale version makes source row "unmatched" → WHEN NOT MATCHED fires
        // → INSERT fails with PK violation (row already exists). Correct: WHEN MATCHED AND t.ver=s.ver
        // leaves the row untouched → 0 rows → detectable conflict via ConcurrencyConflictException.
        var whenMatchedClause = " WHEN MATCHED THEN UPDATE SET ";
        if (_versionColumn != null && _versionColumn.PropertyInfo.PropertyType != typeof(byte[]))
        {
            var v = dialect.WrapSimpleName(_versionColumn.Name);
            whenMatchedClause = $" WHEN MATCHED AND t.{v} = s.{v} THEN UPDATE SET ";
        }

        // WHEN NOT MATCHED INSERT must use only columns projected into the USING source alias s.
        // template.UpsertColumns is the same filtered set used to build the USING source (respects the
        // audit-resolver filter). GetCachedInsertableColumns() does not apply this filter and may include
        // columns (e.g. created_by) that were excluded from s, producing invalid SQL: "s.created_by".
        var insertColSb = SbLite.Create(stackalloc char[512]);
        var insertValSb = SbLite.Create(stackalloc char[512]);
        var join = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        try
        {
            foreach (var column in template.UpsertColumns)
            {
                if (insertColSb.Length > 0)
                {
                    insertColSb.Append(", ");
                }
                if (insertValSb.Length > 0)
                {
                    insertValSb.Append(", ");
                }
                var wrapped = dialect.WrapSimpleName(column.Name);
                insertColSb.Append(wrapped);
                insertValSb.Append("s.");
                insertValSb.Append(wrapped);
            }

            var joinCols = ResolveUpsertKey();
            for (var i = 0; i < joinCols.Count; i++)
            {
                if (i > 0)
                {
                    join.Append(SqlFragments.And);
                }

                join.Append("t.");
                join.Append(dialect.WrapSimpleName(joinCols[i].Name));
                join.Append(SqlFragments.EqualsOp);
                join.Append("s.");
                join.Append(dialect.WrapSimpleName(joinCols[i].Name));
            }

            var onClause = dialect.RenderMergeOnClause(join.ToString());

            var sc = ctx.CreateSqlContainer();
            sc.Query.Append("MERGE INTO ")
                .Append(BuildWrappedTableName(dialect))
                .Append(" t ")
                .Append(mergeSource)
                .Append(" ON ")
                .Append(onClause)
                .Append(whenMatchedClause)
                .Append(template.UpsertUpdateFragment)
                .Append(" WHEN NOT MATCHED THEN INSERT (")
                .Append(insertColSb.AsSpan())
                .Append(") VALUES (")
                .Append(insertValSb.AsSpan())
                .Append(");");

            sc.AddParameters(parameters);
            return sc;
        }
        finally
        {
            insertColSb.Dispose();
            insertValSb.Dispose();
            join.Dispose();
        }
    }

    private ISqlContainer BuildFirebirdMergeUpsert(TEntity entity, IDatabaseContext context)
    {
        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);

        PrepareForInsertOrUpsert(entity);

        var template = GetTemplatesForDialect(dialect);
        var binder = GetOrBuildUpsertBinder(dialect, template);

        // Use pre-built column/value lists from template - NO MORE DYNAMIC LOOP
        var insertColSb = SbLite.Create(stackalloc char[512]);
        var valSb = SbLite.Create(stackalloc char[512]);
        try
        {
            for (var i = 0; i < template.UpsertColumns.Count; i++)
            {
                if (i > 0)
                {
                    insertColSb.Append(", ");
                    valSb.Append(", ");
                }

                insertColSb.Append(dialect.WrapSimpleName(template.UpsertColumns[i].Name));

                var pName = template.UpsertParameterNames[i];
                var placeholder = dialect.MakeParameterName(pName);
                if (template.UpsertColumns[i].IsJsonType)
                {
                    placeholder = dialect.RenderJsonArgument(placeholder, template.UpsertColumns[i]);
                }

                valSb.Append(placeholder);
            }

            var parameters = new List<DbParameter>(template.UpsertColumns.Count);
            binder(entity, parameters);

            var joinCols = ResolveUpsertKey();

            var sc = ctx.CreateSqlContainer();
            sc.Query.Append("UPDATE OR INSERT INTO ")
                .Append(BuildWrappedTableName(dialect))
                .Append(" (")
                .Append(insertColSb.AsSpan())
                .Append(") VALUES (")
                .Append(valSb.AsSpan())
                .Append(") MATCHING (");
            for (var i = 0; i < joinCols.Count; i++)
            {
                if (i > 0)
                {
                    sc.Query.Append(", ");
                }

                sc.Query.Append(dialect.WrapSimpleName(joinCols[i].Name));
            }

            sc.Query.Append(");");

            sc.AddParameters(parameters);
            return sc;
        }
        finally
        {
            insertColSb.Dispose();
            valSb.Dispose();
        }
    }
}
