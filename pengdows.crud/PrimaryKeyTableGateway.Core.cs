// =============================================================================
// FILE: PrimaryKeyTableGateway.Core.cs
// PURPOSE: Concrete gateway for entities with [PrimaryKey] columns and no surrogate [Id].
//          Constructor, shared template infrastructure, CREATE, RETRIEVE.
// =============================================================================

using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.@internal;

namespace pengdows.crud;

/// <summary>
/// Concrete gateway for entities identified solely by <c>[PrimaryKey]</c> columns —
/// no surrogate <c>[Id]</c> column required.
/// </summary>
/// <typeparam name="TEntity">Entity type mapped to the table. Must have a parameterless constructor.</typeparam>
public partial class PrimaryKeyTableGateway<TEntity> :
    BaseTableGateway<TEntity>,
    IPrimaryKeyTableGateway<TEntity>
    where TEntity : class, new()
{
    // =========================================================================
    // Per-dialect template cache
    // =========================================================================

    private readonly ConcurrentDictionary<SupportedDatabase, Lazy<PkTemplates>> _pkTemplatesByDialect = new();

    /// <summary>Cached SQL fragments specific to PK-based operations.</summary>
    private sealed class PkTemplates
    {
        /// <summary>Columns included in UPDATE SET clause (excludes PK, version, created* columns).</summary>
        public List<IColumnInfo> UpdateColumns = null!;

        /// <summary>Pre-wrapped names for UpdateColumns (parallel indexed).</summary>
        public string[] UpdateColumnWrappedNames = null!;

        /// <summary>"UPDATE {table} SET " prefix.</summary>
        public string UpdateSqlPrefix = null!;

        /// <summary>", version = version + 1" or null when no version column.</summary>
        public string? VersionIncrementClause;

        /// <summary>ON CONFLICT / MERGE / ON DUPLICATE KEY UPDATE fragment, or null when upsert not applicable.</summary>
        public string? UpsertUpdateFragment;

        /// <summary>"AND t.\"ver\" = s.\"ver\"" appended to WHEN MATCHED arm; null when no [Version] column.</summary>
        public string? UpsertMergeVersionCondition;

        /// <summary>"WHERE \"table\".\"ver\" = EXCLUDED.\"ver\"" for ON CONFLICT WHERE; null when not applicable.</summary>
        public string? UpsertOnConflictVersionWhere;
    }

    // =========================================================================
    // Constructor
    // =========================================================================

    /// <summary>
    /// Creates a gateway for an entity that carries only <c>[PrimaryKey]</c> columns.
    /// Throws <see cref="InvalidOperationException"/> when the entity has no <c>[PrimaryKey]</c>.
    /// </summary>
    public PrimaryKeyTableGateway(
        IDatabaseContext databaseContext,
        IAuditValueResolver? auditValueResolver = null,
        EnumParseFailureMode enumParseBehavior = EnumParseFailureMode.Throw,
        ILogger? logger = null)
        : base(databaseContext, auditValueResolver, enumParseBehavior, logger)
    {
        if (_tableInfo.PrimaryKeys.Count == 0)
        {
            throw new InvalidOperationException(
                $"Entity {typeof(TEntity).Name} has no [PrimaryKey] columns. " +
                "Use TableGateway<TEntity, TRowID> for entities with an [Id] column.");
        }
    }

    // =========================================================================
    // Template infrastructure
    // =========================================================================

    private PkTemplates GetPkTemplatesForDialect(ISqlDialect dialect) =>
        _pkTemplatesByDialect
            .GetOrAdd(dialect.DatabaseType, _ => new Lazy<PkTemplates>(() => BuildPkTemplates(dialect)))
            .Value;

    private PkTemplates BuildPkTemplates(ISqlDialect dialect)
    {
        var pkSet = new HashSet<IColumnInfo>(_tableInfo.PrimaryKeys);

        var updateColumns = _tableInfo.OrderedColumns
            .Where(c => !pkSet.Contains(c) && !c.IsVersion && !c.IsNonUpdateable && !c.IsCreatedBy && !c.IsCreatedOn)
            .ToList();

        var updateColumnWrappedNames = new string[updateColumns.Count];
        for (var i = 0; i < updateColumns.Count; i++)
        {
            updateColumnWrappedNames[i] = dialect.WrapSimpleName(updateColumns[i].Name);
        }

        var updateSqlPrefix = $"UPDATE {BuildWrappedTableName(dialect)} SET ";

        string? versionIncrementClause = null;
        if (_versionColumn != null && _versionColumn.PropertyInfo.PropertyType != typeof(byte[]))
        {
            versionIncrementClause =
                $", {dialect.WrapSimpleName(_versionColumn.Name)} = {dialect.WrapSimpleName(_versionColumn.Name)} + 1";
        }

        string? upsertUpdateFragment = BuildUpsertUpdateFragment(dialect, updateColumns);

        string? upsertMergeVersionCondition = null;
        string? upsertOnConflictVersionWhere = null;
        if (_versionColumn != null && _versionColumn.PropertyInfo.PropertyType != typeof(byte[]))
        {
            var wrappedVer = dialect.WrapSimpleName(_versionColumn.Name);
            upsertMergeVersionCondition = $"AND t.{wrappedVer} = s.{wrappedVer}";
            if (dialect.SupportsOnConflictWhere)
            {
                upsertOnConflictVersionWhere =
                    $"WHERE {BuildWrappedTableName(dialect)}.{wrappedVer} = EXCLUDED.{wrappedVer}";
            }
        }

        return new PkTemplates
        {
            UpdateColumns = updateColumns,
            UpdateColumnWrappedNames = updateColumnWrappedNames,
            UpdateSqlPrefix = updateSqlPrefix,
            VersionIncrementClause = versionIncrementClause,
            UpsertUpdateFragment = upsertUpdateFragment,
            UpsertMergeVersionCondition = upsertMergeVersionCondition,
            UpsertOnConflictVersionWhere = upsertOnConflictVersionWhere
        };
    }

    private string? BuildUpsertUpdateFragment(ISqlDialect dialect, List<IColumnInfo> updateColumns)
    {
        var frag = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        try
        {
            if (dialect.SupportsMerge)
            {
                var tp = dialect.MergeUpdateRequiresTargetAlias ? "t." : "";
                foreach (var col in updateColumns)
                {
                    if (_auditValueResolver == null && col.IsLastUpdatedBy)
                    {
                        continue;
                    }

                    if (frag.Length > 0)
                    {
                        frag.Append(", ");
                    }

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
                try
                {
                    foreach (var col in updateColumns)
                    {
                        if (_auditValueResolver == null && col.IsLastUpdatedBy)
                        {
                            continue;
                        }

                        if (frag.Length > 0)
                        {
                            frag.Append(", ");
                        }

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
                    frag.Clear();
                }
            }

            return frag.Length > 0 ? frag.ToString() : null;
        }
        finally
        {
            frag.Dispose();
        }
    }

    // =========================================================================
    // CREATE
    // =========================================================================

    /// <inheritdoc/>
    public ISqlContainer BuildCreate(TEntity entity, IDatabaseContext? context = null)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);

        if (_hasAuditColumns)
        {
            SetAuditFields(entity, false);
        }

        PrepareVersionForCreate(entity);

        var insertableColumns = GetCachedInsertableColumns();
        var sc = ctx.CreateSqlContainer();
        var parameters = AppendInsertIntoColumnsAndValues(sc, dialect, insertableColumns, entity);
        sc.AddParameters(parameters);
        return sc;
    }

    /// <inheritdoc/>
    public ValueTask<bool> CreateAsync(TEntity entity) => CreateAsync(entity, null, CancellationToken.None);

    /// <inheritdoc/>
    public async ValueTask<bool> CreateAsync(TEntity entity, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        var ctx = context ?? _context;
        await using var sc = BuildCreate(entity, ctx);
        return await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false) == 1;
    }

    // =========================================================================
    // RETRIEVE
    // =========================================================================

    /// <inheritdoc/>
    public async ValueTask<TEntity?> RetrieveOneAsync(TEntity objectToRetrieve, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (objectToRetrieve == null)
        {
            throw new ArgumentNullException(nameof(objectToRetrieve));
        }

        var ctx = context ?? _context;
        await using var sc = BuildRetrieve(new[] { objectToRetrieve }, string.Empty, ctx);
        return await LoadSingleAsync(sc, cancellationToken).ConfigureAwait(false);
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    private void PrepareVersionForCreate(TEntity entity)
    {
        if (_versionColumn == null)
        {
            return;
        }

        var current = _versionColumn.MakeParameterValueFromField(entity);
        if (current == null || Utils.IsZeroNumeric(current))
        {
            var target = Nullable.GetUnderlyingType(_versionColumn.PropertyInfo.PropertyType) ??
                         _versionColumn.PropertyInfo.PropertyType;
            if (Utils.IsZeroNumeric(TypeCoercionHelper.ConvertWithCache(0, target)))
            {
                _versionColumn.PropertyInfo.SetValue(entity, TypeCoercionHelper.ConvertWithCache(1, target));
            }
        }
    }

    /// <summary>
    /// Appends "INSERT INTO {table} ({cols}) VALUES ({params})" to <paramref name="sc"/>
    /// and returns the created parameters. Used by BuildCreate and single-entity upsert builders.
    /// </summary>
    private List<DbParameter> AppendInsertIntoColumnsAndValues(
        ISqlContainer sc,
        ISqlDialect dialect,
        IReadOnlyList<IColumnInfo> insertableColumns,
        TEntity entity)
    {
        var parameters = new List<DbParameter>(insertableColumns.Count);

        sc.Query.Append("INSERT INTO ")
            .Append(BuildWrappedTableName(dialect))
            .Append(" (");

        for (var i = 0; i < insertableColumns.Count; i++)
        {
            if (i > 0)
            {
                sc.Query.Append(", ");
            }

            sc.Query.Append(dialect.WrapSimpleName(insertableColumns[i].Name));
        }

        sc.Query.Append(") VALUES (");

        for (var i = 0; i < insertableColumns.Count; i++)
        {
            if (i > 0)
            {
                sc.Query.Append(", ");
            }

            var col = insertableColumns[i];
            var pName = $"i{i}";
            var value = col.MakeParameterValueFromField(entity);
            var param = dialect.CreateDbParameter(pName, col.DbType, value);

            if (col.IsJsonType)
            {
                dialect.TryMarkJsonParameter(param, col);
                sc.Query.Append(dialect.RenderJsonArgument(dialect.MakeParameterName(pName), col));
            }
            else
            {
                sc.Query.Append(dialect.MakeParameterName(pName));
            }

            parameters.Add(param);
        }

        sc.Query.Append(')');
        return parameters;
    }
}
