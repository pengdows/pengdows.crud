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
//   * SQL Server/Oracle: MERGE statement
//   * PostgreSQL: INSERT ... ON CONFLICT (key) DO UPDATE
//   * MySQL/MariaDB: INSERT ... ON DUPLICATE KEY UPDATE
// - Handles audit columns and version columns appropriately.
// - Throws NotSupportedException for fallback/unknown dialects.
// - Returns affected row count (typically 1 for single-entity upsert).
// =============================================================================

using System.Data;
using System.Data.Common;
using pengdows.crud.@internal;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud;

/// <summary>
/// TableGateway partial: UPSERT (INSERT or UPDATE) operations.
/// </summary>
public partial class TableGateway<TEntity, TRowID>
{
    /// <inheritdoc/>
    public async Task<int> UpsertAsync(TEntity entity, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        var ctx = context ?? _context;
        // BuildUpsert creates a dynamic container - proper disposal required to avoid resource leaks
        // Use async disposal for async operations
        await using var sc = BuildUpsert(entity, ctx);
        return await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ISqlContainer BuildUpsert(TEntity entity, IDatabaseContext? context = null)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        var ctx = context ?? _context;
        if (_idColumn == null && _tableInfo.PrimaryKeys.Count == 0)
        {
            throw new NotSupportedException(UpsertNoKeyMessage);
        }

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
        if (_idColumn != null && _idColumn.IsIdIsWritable)
        {
            return new List<IColumnInfo> { _idColumn! };
        }

        var keys = _tableInfo.PrimaryKeys;
        if (keys.Count > 0)
        {
            return keys;
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

            var keys = _tableInfo.PrimaryKeys;
            if (_idColumn == null && keys.Count == 0)
            {
                throw new NotSupportedException(UpsertNoKeyMessage);
            }

            var conflictCols = keys.Count > 0 ? keys : new List<IColumnInfo> { _idColumn! };

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
                if (i > 0) sc.Query.Append(", ");
                sc.Query.Append(dialect.WrapSimpleName(conflictCols[i].Name));
            }

            sc.Query.Append(") DO UPDATE SET ")
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

    private static string FormatFirebirdValueExpression(string placeholder, IColumnInfo column)
    {
        var typeName = GetFirebirdDataType(column);
        if (string.Equals(placeholder, "NULL", StringComparison.OrdinalIgnoreCase))
        {
            return $"CAST(NULL AS {typeName})";
        }

        return $"CAST({placeholder} AS {typeName})";
    }

    private static string GetFirebirdDataType(IColumnInfo column)
    {
        return column.DbType switch
        {
            DbType.Boolean => "SMALLINT",
            DbType.Byte => "SMALLINT",
            DbType.SByte => "SMALLINT",
            DbType.Int16 => "SMALLINT",
            DbType.UInt16 => "SMALLINT",
            DbType.Int32 => "INTEGER",
            DbType.UInt32 => "BIGINT",
            DbType.Int64 => "BIGINT",
            DbType.UInt64 => "BIGINT",
            DbType.Decimal => "DECIMAL(18,2)",
            DbType.Double => "DOUBLE PRECISION",
            DbType.Single => "DOUBLE PRECISION",
            DbType.Date => "DATE",
            DbType.DateTime => "TIMESTAMP",
            DbType.AnsiStringFixedLength => "VARCHAR(255)",
            DbType.AnsiString => "VARCHAR(255)",
            DbType.String => "VARCHAR(255)",
            DbType.StringFixedLength => "VARCHAR(255)",
            DbType.Guid => "CHAR(36)",
            DbType.Binary => "BLOB",
            _ => "VARCHAR(255)"
        };
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

            var keys = _tableInfo.PrimaryKeys;
            if (_idColumn == null && keys.Count == 0)
            {
                throw new NotSupportedException(UpsertNoKeyMessage);
            }

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

        var insertableColumns = GetCachedInsertableColumns();

        // Use SbLites for insert columns and their s.col aliases — eliminates two List<string> allocations.
        var insertColSb = SbLite.Create(stackalloc char[512]);
        var insertValSb = SbLite.Create(stackalloc char[512]);
        var join = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        try
        {
            foreach (var column in insertableColumns)
            {
                if (insertColSb.Length > 0) insertColSb.Append(", ");
                if (insertValSb.Length > 0) insertValSb.Append(", ");
                var wrapped = dialect.WrapSimpleName(column.Name);
                insertColSb.Append(wrapped);
                insertValSb.Append("s.");
                insertValSb.Append(wrapped);
            }

            var keys = _tableInfo.PrimaryKeys;
            if (_idColumn == null && keys.Count == 0)
            {
                throw new NotSupportedException(UpsertNoKeyMessage);
            }

            var joinCols = keys.Count > 0 ? keys : new List<IColumnInfo> { _idColumn! };
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
                .Append(" WHEN MATCHED THEN UPDATE SET ")
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

            var keys = _tableInfo.PrimaryKeys;
            if (_idColumn == null && keys.Count == 0)
            {
                throw new NotSupportedException(UpsertNoKeyMessage);
            }

            var joinCols = keys.Count > 0 ? keys : new List<IColumnInfo> { _idColumn! };

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
