#region

using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.wrappers;

#endregion

namespace pengdows.crud;

public class EntityHelper<TEntity, TRowID> :
    IEntityHelper<TEntity, TRowID> where TEntity : class, new()
{
    // Cache for compiled property setters
    private static readonly ConcurrentDictionary<PropertyInfo, Action<object, object?>> _propertySetters = new();

    private static ILogger _logger = NullLogger.Instance;

    public static ILogger Logger
    {
        get => _logger;
        set => _logger = value ?? NullLogger.Instance;
    }

    static EntityHelper()
    {
        ValidateRowIdType();
    }

    private readonly IAuditValueResolver? _auditValueResolver;
    private IDatabaseContext _context;
    private ISqlDialect _dialect;
    private readonly Guid _rootId;

    private IColumnInfo? _idColumn;

    // private readonly IServiceProvider _serviceProvider;
    private ITableInfo _tableInfo;
    private bool _hasAuditColumns;

    private IColumnInfo? _versionColumn;

    public EntityHelper(IDatabaseContext databaseContext,
        EnumParseFailureMode enumParseBehavior = EnumParseFailureMode.Throw
    )
    {
        _auditValueResolver = null;
        _rootId = ((IContextIdentity)databaseContext).RootId;
        Initialize(databaseContext, enumParseBehavior);
    }

    [Obsolete("Use the constructor without IServiceProvider instead")]
    public EntityHelper(IDatabaseContext databaseContext,
        IAuditValueResolver auditValueResolver,
        EnumParseFailureMode enumParseBehavior = EnumParseFailureMode.Throw
    )
    {
        _auditValueResolver = auditValueResolver;
        _rootId = ((IContextIdentity)databaseContext).RootId;
        Initialize(databaseContext, enumParseBehavior);
    }

    private void Initialize(IDatabaseContext databaseContext, EnumParseFailureMode enumParseBehavior)
    {
        _context = databaseContext;
        _dialect = (databaseContext as ISqlDialectProvider)?.Dialect
            ?? throw new InvalidOperationException(
                "IDatabaseContext must implement ISqlDialectProvider and expose a non-null Dialect.");
        _tableInfo = _context.TypeMapRegistry.GetTableInfo<TEntity>() ??
                     throw new InvalidOperationException($"Type {typeof(TEntity).FullName} is not a table.");
        _hasAuditColumns = _tableInfo.HasAuditColumns;

        WrappedTableName = (!string.IsNullOrEmpty(_tableInfo.Schema)
                               ? WrapObjectName(_tableInfo.Schema) +
                                 _dialect.CompositeIdentifierSeparator
                               : "")
                           + WrapObjectName(_tableInfo.Name);

        _idColumn = _tableInfo.Columns.Values.FirstOrDefault(itm => itm.IsId);
        _versionColumn = _tableInfo.Columns.Values.FirstOrDefault(itm => itm.IsVersion);

        if (_auditValueResolver == null &&
            (_tableInfo.CreatedBy != null ||
             _tableInfo.LastUpdatedBy != null))
        {
            Logger.LogWarning(
                "Audit user columns detected for {EntityType} but no IAuditValueResolver provided. Database defaults will be used for those columns.",
                typeof(TEntity).Name);
        }

        EnumParseBehavior = enumParseBehavior;
    }

    public string WrappedTableName { get; set; }

    public EnumParseFailureMode EnumParseBehavior { get; set; }

    public string QuotePrefix => _dialect.QuotePrefix;

    public string QuoteSuffix => _dialect.QuoteSuffix;

    public string CompositeIdentifierSeparator => _dialect.CompositeIdentifierSeparator;


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateSameRoot(IDatabaseContext? context)
    {
        if (context == null)
        {
            return;
        }

        if (!Equals(((IContextIdentity)context).RootId, _rootId))
        {
            throw new InvalidOperationException(
                "Context mismatch: must be the owning DatabaseContext or its TransactionContext.");
        }
    }

    public string MakeParameterName(DbParameter p)
    {
        return _dialect.MakeParameterName(p);
    }

    public TEntity MapReaderToObject(ITrackedReader reader)
    {
        var obj = new TEntity();

        for (var i = 0; i < reader.FieldCount; i++)
        {
            var colName = reader.GetName(i);
            if (_tableInfo.Columns.TryGetValue(colName, out var column))
            {
                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);

                var dbFieldType = reader.GetFieldType(i);
                value = TypeCoercionHelper.Coerce(value, dbFieldType, column);
                try
                {
                    var setter = GetOrCreateSetter(column.PropertyInfo);
                    setter(obj, value);
                }
                catch (Exception ex)
                {
                    throw new InvalidValueException(
                        $"Unable to set property from value that was stored in the database: {colName} :{ex.Message}");
                }
            }
        }

        return obj;
    }

    public async Task<bool> CreateAsync(TEntity entity, IDatabaseContext context)
    {
        ValidateSameRoot(context);
        var sc = BuildCreate(entity, context);
        return await sc.ExecuteNonQueryAsync() == 1;
    }

    public ISqlContainer BuildCreate(TEntity objectToCreate, IDatabaseContext? context = null)
    {
        if (objectToCreate == null)
            throw new ArgumentNullException(nameof(objectToCreate));
        ValidateSameRoot(context);
        var ctx = context ?? _context;
        var columns = new StringBuilder();
        var values = new StringBuilder();
        var parameters = new List<DbParameter>();

        var sc = ctx.CreateSqlContainer();
        PrepareForInsertOrUpsert(objectToCreate);

        foreach (var column in _tableInfo.Columns.Values)
        {
            if (column.IsNonInsertable)
            {
                continue;
            }

            if (column.IsId && !column.IsIdIsWritable)
            {
                continue;
            }

            var value = column.MakeParameterValueFromField(objectToCreate);

            // If no audit resolver is provided and the value is null for a user audit column,
            // skip including this column so database defaults will apply.
            if (_auditValueResolver == null &&
                (column.IsCreatedBy || column.IsLastUpdatedBy) &&
                Utils.IsNullOrDbNull(value))
            {
                continue;
            }

            if (columns.Length > 0)
            {
                columns.Append(", ");
                values.Append(", ");
            }

            var p = _context.CreateDbParameter(
                column.DbType,
                value
            );

            columns.Append(WrapObjectName(column.Name));
            if (Utils.IsNullOrDbNull(value))
            {
                values.Append("NULL");
            }
            else
            {
                values.Append(MakeParameterName(p));
                parameters.Add(p);
            }
        }

        sc.Query.Append("INSERT INTO ")
            .Append(WrappedTableName)
            .Append(" (")
            .Append(columns)
            .Append(") VALUES (")
            .Append(values)
            .Append(")");

        sc.AddParameters(parameters);
        return sc;
    }


    public ISqlContainer BuildBaseRetrieve(string alias, IDatabaseContext? context = null)
    {
        ValidateSameRoot(context);
        var ctx = context ?? _context;

        var sc = ctx.CreateSqlContainer();
        var hasAlias = !string.IsNullOrWhiteSpace(alias);

        sc.Query.Append("SELECT ");
        var selectList = _tableInfo.Columns.Values
            .OrderBy(c => c.Ordinal)
            .Select(col => (hasAlias
                ? _dialect.WrapObjectName(alias) + _dialect.CompositeIdentifierSeparator
                : string.Empty) + _dialect.WrapObjectName(col.Name));
        sc.Query.Append(string.Join(", ", selectList));

        sc.Query.Append("\nFROM ").Append(WrappedTableName);
        if (hasAlias)
        {
            sc.Query.Append(' ').Append(_dialect.WrapObjectName(alias));
        }

        return sc;
    }

    public ISqlContainer BuildDelete(TRowID id, IDatabaseContext? context = null)
    {
        ValidateSameRoot(context);
        var ctx = context ?? _context;
        var idCol = _idColumn;
        if (idCol == null)
        {
            throw new NotSupportedException(
                "Single-ID operations require a designated Id column; use composite-key helpers.");
        }

        var sc = ctx.CreateSqlContainer();

        var p = _context.CreateDbParameter(idCol.DbType, id);
        sc.AddParameter(p);

        sc.Query.Append("DELETE FROM ")
            .Append(WrappedTableName)
            .Append(" WHERE ")
            .Append(WrapObjectName(idCol.Name));
        if (Utils.IsNullOrDbNull(p.Value))
        {
            sc.Query.Append(" IS NULL ");
        }
        else
        {
            sc.Query.Append(" = ");
            sc.Query.Append(MakeParameterName(p));
        }

        return sc;
    }

    public async Task<int> DeleteAsync(TRowID id, IDatabaseContext? context = null)
    {
        ValidateSameRoot(context);
        var sc = BuildDelete(id, context);
        return await sc.ExecuteNonQueryAsync();
    }

    public async Task<List<TEntity>> RetrieveAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null)
    {
        if (ids == null)
        {
            throw new ArgumentNullException(nameof(ids));
        }

        var list = ids.Distinct().ToList();
        if (list.Count == 0)
        {
            return new List<TEntity>();
        }

        ValidateSameRoot(context);
        var ctx = context ?? _context;
        var sc = BuildRetrieve(list, ctx);
        return await LoadListAsync(sc);
    }

    public async Task<int> DeleteAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null)
    {
        if (ids == null)
        {
            throw new ArgumentNullException(nameof(ids));
        }

        var list = ids.Distinct().ToList();
        if (list.Count == 0)
        {
            return 0;
        }

        ValidateSameRoot(context);
        var ctx = context ?? _context;
        if (_idColumn == null)
        {
            throw new NotSupportedException(
                "Single-ID operations require a designated Id column; use composite-key helpers.");
        }

        var sc = ctx.CreateSqlContainer();
        sc.Query.Append("DELETE FROM ").Append(WrappedTableName);
        BuildWhere(_dialect.WrapObjectName(_idColumn.Name), list, sc);
        return await sc.ExecuteNonQueryAsync();
    }


    public Action<object, object?> GetOrCreateSetter(PropertyInfo prop)
    {
        // The generated setter casts directly; a database NULL assigned to a non-nullable property will throw.
        // This fail-fast behavior surfaces unexpected schema mismatches immediately.
        return _propertySetters.GetOrAdd(prop, p =>
        {
            var objParam = Expression.Parameter(typeof(object));
            var valueParam = Expression.Parameter(typeof(object));

            var castObj = Expression.Convert(objParam, p.DeclaringType!);
            var castValue = Expression.Convert(valueParam, p.PropertyType);

            var propertyAccess = Expression.Property(castObj, p);
            var assignment = Expression.Assign(propertyAccess, castValue);

            var lambda = Expression.Lambda<Action<object, object?>>(assignment, objParam, valueParam);
            return lambda.Compile();
        });
    }


    public Task<TEntity?> RetrieveOneAsync(TEntity objectToRetrieve, IDatabaseContext? context = null)
    {
        ValidateSameRoot(context);
        var ctx = context ?? _context;
        var list = new List<TEntity> { objectToRetrieve };
        var sc = BuildRetrieve(list, string.Empty, ctx);
        return LoadSingleAsync(sc);
    }

    public Task<TEntity?> RetrieveOneAsync(TRowID id, IDatabaseContext? context = null)
    {
        ValidateSameRoot(context);
        var ctx = context ?? _context;
        if (_idColumn == null)
        {
            throw new NotSupportedException(
                "Single-ID operations require a designated Id column; use composite-key helpers.");
        }
        var list = new List<TRowID> { id };
        var sc = BuildRetrieve(list, ctx);
        return LoadSingleAsync(sc);
    }

    public async Task<TEntity?> LoadSingleAsync(ISqlContainer sc)
    {
        await using var reader = await sc.ExecuteReaderAsync().ConfigureAwait(false);
        if (await reader.ReadAsync().ConfigureAwait(false)) return MapReaderToObject(reader);

        return null;
    }

    public async Task<List<TEntity>> LoadListAsync(ISqlContainer sc)
    {
        var list = new List<TEntity>();

        await using var reader = await sc.ExecuteReaderAsync().ConfigureAwait(false);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var obj = MapReaderToObject(reader);
            if (obj != null)
            {
                list.Add(obj);
            }
        }

        return list;
    }

    public ISqlContainer BuildRetrieve(IReadOnlyCollection<TRowID>? listOfIds,
        string alias, IDatabaseContext? context = null)
    {
        ValidateSameRoot(context);
        var ctx = context ?? _context;
        if (_idColumn == null)
        {
            throw new NotSupportedException(
                "Single-ID operations require a designated Id column; use composite-key helpers.");
        }
        var sc = BuildBaseRetrieve(alias, ctx);
        var wrappedAlias = "";
        if (!string.IsNullOrWhiteSpace(alias))
        {
            wrappedAlias = _dialect.WrapObjectName(alias) + _dialect.CompositeIdentifierSeparator;
        }

        var wrappedColumnName = wrappedAlias + _dialect.WrapObjectName(_idColumn.Name);

        if (listOfIds != null && listOfIds.Any(id => Utils.IsNullOrDbNull(id)))
        {
            throw new ArgumentException("IDs cannot be null", nameof(listOfIds));
        }

        BuildWhere(
            wrappedColumnName,
            listOfIds,
            sc
        );

        return sc;
    }

    public ISqlContainer BuildRetrieve(IReadOnlyCollection<TEntity>? listOfObjects,
        string alias, IDatabaseContext? context = null)
    {
        ValidateSameRoot(context);
        var ctx = context ?? _context;
        var sc = BuildBaseRetrieve(alias, ctx);
        BuildWhereByPrimaryKey(
            listOfObjects,
            sc,
            alias);

        return sc;
    }

    public ISqlContainer BuildRetrieve(IReadOnlyCollection<TRowID>? listOfIds, IDatabaseContext? context = null)
    {
        ValidateSameRoot(context);
        return BuildRetrieve(listOfIds, "", context);
    }

    public ISqlContainer BuildRetrieve(IReadOnlyCollection<TEntity>? listOfObjects,
        IDatabaseContext? context = null)
    {
        ValidateSameRoot(context);
        return BuildRetrieve(listOfObjects, string.Empty, context);
    }

    public void BuildWhereByPrimaryKey(IReadOnlyCollection<TEntity>? listOfObjects, ISqlContainer sc, string alias = "")
    {
        ValidateWhereInputs(listOfObjects, sc);

        var keys = GetPrimaryKeys();
        CheckParameterLimit(sc, listOfObjects!.Count * keys.Count);

        var parameters = new List<DbParameter>();
        var wrappedAlias = BuildAliasPrefix(alias);
        var sb = new StringBuilder();
        var index = 0;

        foreach (var entity in listOfObjects!)
        {
            if (index++ > 0)
            {
                sb.Append(" OR ");
            }

            sb.Append(BuildPrimaryKeyClause(entity, keys, wrappedAlias, parameters));
        }

        if (sb.Length == 0)
        {
            return;
        }

        sc.AddParameters(parameters);
        AppendWherePrefix(sc);
        sc.Query.Append(sb);
    }

    private void ValidateWhereInputs(IReadOnlyCollection<TEntity>? listOfObjects, ISqlContainer _)
    {
        if (listOfObjects == null || listOfObjects.Count == 0)
        {
            throw new ArgumentException("List of objects cannot be null or empty.", nameof(listOfObjects));
        }
    }

    private List<IColumnInfo> GetPrimaryKeys()
    {
        var keys = _tableInfo.Columns.Values
            .Where(o => o.IsPrimaryKey)
            .OrderBy(k => k.PkOrder)
            .ToList();
        if (keys.Count < 1)
        {
            throw new Exception($"No primary keys found for type {typeof(TEntity).Name}");
        }

        return keys;
    }

    private void CheckParameterLimit(ISqlContainer sc, int? toAdd)
    {
        var count = sc.ParameterCount + (toAdd ?? 0);
        if (count > _context.MaxParameterLimit)
        {
            // For large batches consider chunking inputs; this method fails fast when exceeding limits.
            throw new TooManyParametersException("Too many parameters", _context.MaxParameterLimit);
        }
    }

    private static string BuildAliasPrefix(string alias) =>
        string.IsNullOrWhiteSpace(alias) ? string.Empty : alias + ".";

    private string BuildPrimaryKeyClause(TEntity entity, IReadOnlyList<IColumnInfo> keys, string alias,
        List<DbParameter> parameters)
    {
        var clause = new StringBuilder("(");
        for (var i = 0; i < keys.Count; i++)
        {
            if (i > 0)
            {
                clause.Append(" AND ");
            }

            var pk = keys[i];
            var value = pk.MakeParameterValueFromField(entity);
            var parameter = _context.CreateDbParameter(pk.DbType, value);

            clause.Append(alias);
            clause.Append(WrapObjectName(pk.Name));

            if (Utils.IsNullOrDbNull(value))
            {
                clause.Append(" IS NULL");
            }
            else
            {
                clause.Append(" = ");
                clause.Append(MakeParameterName(parameter));
                parameters.Add(parameter);
            }
        }

        clause.Append(')');
        return clause.ToString();
    }

    private void AppendWherePrefix(ISqlContainer sc)
    {
        if (!sc.HasWhereAppended)
        {
            sc.Query.Append("\n WHERE ");
            sc.HasWhereAppended = true;
        }
        else
        {
            sc.Query.Append("\n AND ");
        }
    }


    public Task<ISqlContainer> BuildUpdateAsync(TEntity objectToUpdate, IDatabaseContext? context = null)
    {
        ValidateSameRoot(context);
        var ctx = context ?? _context;
        return BuildUpdateAsync(objectToUpdate, _versionColumn != null, ctx);
    }

    public async Task<ISqlContainer> BuildUpdateAsync(TEntity objectToUpdate, bool loadOriginal,
        IDatabaseContext? context = null)
    {
        if (objectToUpdate == null)
        {
            throw new ArgumentNullException(nameof(objectToUpdate));
        }

        ValidateSameRoot(context);
        var ctx = context ?? _context;
        if (_idColumn == null)
        {
            throw new NotSupportedException(
                "Single-ID operations require a designated Id column; use composite-key helpers.");
        }
        var sc = ctx.CreateSqlContainer();

        var original = loadOriginal ? await LoadOriginalAsync(objectToUpdate) : null;
        if (loadOriginal && original == null)
        {
            throw new InvalidOperationException("Original record not found for update.");
        }

        SetAuditFields(objectToUpdate, true);

        var (setClause, parameters) = BuildSetClause(objectToUpdate, original);
        if (setClause.Length == 0)
        {
            throw new InvalidOperationException("No changes detected for update.");
        }

        if (_versionColumn != null && _versionColumn.PropertyInfo.PropertyType != typeof(byte[]))
        {
            IncrementVersion(setClause);
        }

        var pId = _context.CreateDbParameter(_idColumn.DbType,
            _idColumn.PropertyInfo.GetValue(objectToUpdate)!);
        parameters.Add(pId);

        sc.Query.Append("UPDATE ")
            .Append(WrappedTableName)
            .Append(" SET ")
            .Append(setClause)
            .Append(" WHERE ")
            .Append(_dialect.WrapObjectName(_idColumn.Name))
            .Append($" = {MakeParameterName(pId)}");

        if (_versionColumn != null)
        {
            var versionValue = _versionColumn.MakeParameterValueFromField(objectToUpdate);
            AppendVersionCondition(sc, versionValue, parameters);
        }

        sc.AddParameters(parameters);
        return sc;
    }

    public Task<int> UpdateAsync(TEntity objectToUpdate, IDatabaseContext? context = null)
    {
        ValidateSameRoot(context);
        var ctx = context ?? _context;
        return UpdateAsync(objectToUpdate, _versionColumn != null, ctx);
    }

    public async Task<int> UpdateAsync(TEntity objectToUpdate, bool loadOriginal, IDatabaseContext? context = null)
    {
        ValidateSameRoot(context);
        try
        {
            var sc = await BuildUpdateAsync(objectToUpdate, loadOriginal, context);
            return await sc.ExecuteNonQueryAsync();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No changes detected for update."))
        {
            return 0;
        }
    }

    public async Task<int> UpsertAsync(TEntity entity, IDatabaseContext? context = null)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        ValidateSameRoot(context);
        var ctx = context ?? _context;

        try
        {
            var sc = BuildUpsert(entity, ctx);
            return await sc.ExecuteNonQueryAsync();
        }
        catch (NotSupportedException)
        {
            return await UpsertPortableAsync(entity, ctx);
        }
    }

    public ISqlContainer BuildUpsert(TEntity entity, IDatabaseContext? context = null)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        ValidateSameRoot(context);
        var ctx = context ?? _context;
        if (_idColumn == null && !_tableInfo.Columns.Values.Any(c => c.IsPrimaryKey))
        {
            throw new NotSupportedException("Upsert requires an Id or a composite primary key.");
        }

        // Use dialect capabilities instead of hard-coded database switching
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

        var keys = _tableInfo.Columns.Values
            .Where(c => c.IsPrimaryKey)
            .OrderBy(c => c.PkOrder)
            .ToList();

        if (keys.Count > 0)
        {
            return keys;
        }

        throw new NotSupportedException("Upsert requires client-assigned Id or [PrimaryKey] attributes.");
    }

    private (string sql, List<DbParameter> parameters) BuildUpdateByKey(TEntity updated,
        IReadOnlyList<IColumnInfo> keyCols)
    {
        SetAuditFields(updated, true);
        var (setClause, parameters) = BuildSetClause(updated, null);
        if (setClause.Length == 0)
        {
            throw new InvalidOperationException("No changes detected for update.");
        }

        if (_versionColumn != null && _versionColumn.PropertyInfo.PropertyType != typeof(byte[]))
        {
            IncrementVersion(setClause);
        }

        var where = new StringBuilder();
        for (var i = 0; i < keyCols.Count; i++)
        {
            if (i > 0)
            {
                where.Append(" AND ");
            }

            var key = keyCols[i];
            var v = key.MakeParameterValueFromField(updated);
            var p = _context.CreateDbParameter(key.DbType, v);
            parameters.Add(p);
            where.Append($"{WrapObjectName(key.Name)} = {MakeParameterName(p)}");
        }

        var sql = $"UPDATE {WrappedTableName} SET {setClause} WHERE {where}";
        if (_versionColumn != null)
        {
            var vv = _versionColumn.MakeParameterValueFromField(updated);
            var p = _context.CreateDbParameter(_versionColumn.DbType, vv);
            parameters.Add(p);
            sql += $" AND {WrapObjectName(_versionColumn.Name)} = {MakeParameterName(p)}";
        }

        return (sql, parameters);
    }

    private async Task<int> UpsertPortableAsync(TEntity entity, IDatabaseContext ctx)
    {
        var keyCols = ResolveUpsertKey();
        var (updateSql, updateParams) = BuildUpdateByKey(entity, keyCols);
        var scUpdate = ctx.CreateSqlContainer();
        scUpdate.Query.Append(updateSql);
        scUpdate.AddParameters(updateParams);
        var rows = await scUpdate.ExecuteNonQueryAsync().ConfigureAwait(false);
        if (rows > 0)
        {
            return rows;
        }

        var scInsert = BuildCreate(entity, ctx);

        try
        {
            return await scInsert.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (DbException ex) when (_dialect.IsUniqueViolation(ex))
        {
            var scUpdate2 = ctx.CreateSqlContainer();
            scUpdate2.Query.Append(updateSql);
            scUpdate2.AddParameters(updateParams);
            return await scUpdate2.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    private async Task<TEntity?> LoadOriginalAsync(TEntity objectToUpdate)
    {
        var idValue = _idColumn!.PropertyInfo.GetValue(objectToUpdate);
        if (IsDefaultId(idValue))
            return null;

        return await RetrieveOneAsync((TRowID)idValue!);
    }

    private (StringBuilder clause, List<DbParameter> parameters) BuildSetClause(TEntity updated, TEntity? original)
    {
        var clause = new StringBuilder();
        var parameters = new List<DbParameter>();

        foreach (var column in _tableInfo.Columns.Values)
        {
            if (column.IsId || column.IsVersion || column.IsNonUpdateable || column.IsCreatedBy || column.IsCreatedOn)
            {
                continue;
            }

            var newValue = column.MakeParameterValueFromField(updated);
            var originalValue = original != null ? column.MakeParameterValueFromField(original) : null;

            if (_auditValueResolver == null && column.IsLastUpdatedBy && Utils.IsNullOrDbNull(newValue))
            {
                continue;
            }

            if (original != null && ValuesAreEqual(newValue, originalValue, column.DbType))
            {
                continue;
            }

            if (clause.Length > 0)
            {
                clause.Append(", ");
            }

            if (Utils.IsNullOrDbNull(newValue))
            {
                clause.Append($"{WrapObjectName(column.Name)} = NULL");
            }
            else
            {
                var param = _context.CreateDbParameter(column.DbType, newValue);
                parameters.Add(param);
                clause.Append($"{WrapObjectName(column.Name)} = {MakeParameterName(param)}");
            }
        }

        return (clause, parameters);
    }

    private static bool ValuesAreEqual(object? newValue, object? originalValue, DbType dbType)
    {
        if (newValue == null && originalValue == null)
        {
            return true;
        }

        if (newValue == null || originalValue == null)
        {
            return false;
        }

        if (newValue is byte[] a && originalValue is byte[] b)
        {
            return a.SequenceEqual(b);
        }

        switch (dbType)
        {
            case DbType.Decimal:
            case DbType.Currency:
            case DbType.VarNumeric:
                return decimal.Compare(Convert.ToDecimal(newValue), Convert.ToDecimal(originalValue)) == 0;
            case DbType.DateTime:
            case DbType.DateTime2:
            case DbType.DateTimeOffset:
                return DateTime.Compare(Convert.ToDateTime(newValue).ToUniversalTime(),
                    Convert.ToDateTime(originalValue).ToUniversalTime()) == 0;
            default:
                return Equals(newValue, originalValue);
        }
    }

    private void IncrementVersion(StringBuilder setClause)
    {
        setClause.Append($", {WrapObjectName(_versionColumn!.Name)} = {WrapObjectName(_versionColumn.Name)} + 1");
    }

    private void AppendVersionCondition(ISqlContainer sc, object? versionValue, List<DbParameter> parameters)
    {
        if (versionValue == null)
        {
            sc.Query.Append(" AND ").Append(WrapObjectName(_versionColumn!.Name)).Append(" IS NULL");
        }
        else
        {
            var pVersion = _context.CreateDbParameter(_versionColumn!.DbType, versionValue);
            sc.Query.Append(" AND ").Append(WrapObjectName(_versionColumn.Name))
                .Append($" = {MakeParameterName(pVersion)}");
            parameters.Add(pVersion);
        }
    }

    private void PrepareForInsertOrUpsert(TEntity e)
    {
        SetAuditFields(e, updateOnly: false);
        if (_versionColumn == null || _versionColumn.PropertyInfo.PropertyType == typeof(byte[]))
        {
            return;
        }

        var v = _versionColumn.PropertyInfo.GetValue(e);
        if (v == null || Utils.IsZeroNumeric(v))
        {
            var t = Nullable.GetUnderlyingType(_versionColumn.PropertyInfo.PropertyType) ??
                    _versionColumn.PropertyInfo.PropertyType;
            _versionColumn.PropertyInfo.SetValue(e, Convert.ChangeType(1, t));
        }
    }

    private ISqlContainer BuildUpsertOnConflict(TEntity entity, IDatabaseContext context)
    {
        ValidateSameRoot(context);
        var ctx = context ?? _context;
        PrepareForInsertOrUpsert(entity);

        var columns = new List<string>();
        var values = new List<string>();
        var parameters = new List<DbParameter>();

        foreach (var column in _tableInfo.Columns.Values)
        {
            if (column.IsNonInsertable)
            {
                continue;
            }

            if (column.IsId && !column.IsIdIsWritable)
            {
                continue;
            }

            var value = column.MakeParameterValueFromField(entity);

            if (_auditValueResolver == null && (column.IsCreatedBy || column.IsLastUpdatedBy) && Utils.IsNullOrDbNull(value))
            {
                continue;
            }

            columns.Add(WrapObjectName(column.Name));
            if (Utils.IsNullOrDbNull(value))
            {
                values.Add("NULL");
            }
            else
            {
                var p = _context.CreateDbParameter(column.DbType, value);
                parameters.Add(p);
                values.Add(MakeParameterName(p));
            }
        }

        var updateSet = new StringBuilder();
        foreach (var column in _tableInfo.Columns.Values)
        {
            if (column.IsId || column.IsVersion || column.IsNonUpdateable || column.IsCreatedBy || column.IsCreatedOn)
            {
                continue;
            }

            if (_auditValueResolver == null && column.IsLastUpdatedBy)
            {
                // Without a resolver we preserve existing LastUpdatedBy on conflict updates.
                continue;
            }

            if (updateSet.Length > 0)
            {
                updateSet.Append(", ");
            }

            updateSet.Append($"{WrapObjectName(column.Name)} = {_dialect.UpsertIncomingColumn(column.Name)}");
        }

        if (_versionColumn != null && _versionColumn.PropertyInfo.PropertyType != typeof(byte[]))
        {
            updateSet.Append($", {WrapObjectName(_versionColumn.Name)} = {WrapObjectName(_versionColumn.Name)} + 1");
        }

        var keys = _tableInfo.Columns.Values.Where(c => c.IsPrimaryKey).OrderBy(c => c.PkOrder).ToList();
        if (_idColumn == null && keys.Count == 0)
        {
            throw new NotSupportedException("Upsert requires an Id or a composite primary key.");
        }

        var conflictCols = (keys.Count > 0 ? keys : new List<IColumnInfo> { _idColumn! })
            .Select(k => WrapObjectName(k.Name));

        var sc = ctx.CreateSqlContainer();
        sc.Query.Append("INSERT INTO ")
            .Append(WrappedTableName)
            .Append(" (")
            .Append(string.Join(", ", columns))
            .Append(") VALUES (")
            .Append(string.Join(", ", values))
            .Append(") ON CONFLICT (")
            .Append(string.Join(", ", conflictCols))
            .Append(") DO UPDATE SET ")
            .Append(updateSet);

        sc.AddParameters(parameters);
        return sc;
    }

    private ISqlContainer BuildUpsertOnDuplicate(TEntity entity, IDatabaseContext context)
    {
        ValidateSameRoot(context);
        var ctx = context ?? _context;

        PrepareForInsertOrUpsert(entity);

        var columns = new List<string>();
        var values = new List<string>();
        var parameters = new List<DbParameter>();

        foreach (var column in _tableInfo.Columns.Values)
        {
            if (column.IsNonInsertable)
            {
                continue;
            }

            if (column.IsId && !column.IsIdIsWritable)
            {
                continue;
            }

            var value = column.MakeParameterValueFromField(entity);

            columns.Add(WrapObjectName(column.Name));
            if (Utils.IsNullOrDbNull(value))
            {
                values.Add("NULL");
            }
            else
            {
                var p = _context.CreateDbParameter(column.DbType, value);
                parameters.Add(p);
                values.Add(MakeParameterName(p));
            }
        }

        var updateSet = new StringBuilder();
        foreach (var column in _tableInfo.Columns.Values)
        {
            if (column.IsId || column.IsVersion || column.IsNonUpdateable || column.IsCreatedBy || column.IsCreatedOn)
            {
                continue;
            }

            if (_auditValueResolver == null && column.IsLastUpdatedBy)
            {
                // Without a resolver we preserve existing LastUpdatedBy on duplicate updates.
                continue;
            }

            if (updateSet.Length > 0)
            {
                updateSet.Append(", ");
            }

            updateSet.Append($"{WrapObjectName(column.Name)} = {_dialect.UpsertIncomingColumn(column.Name)}");
        }

        if (_versionColumn != null && _versionColumn.PropertyInfo.PropertyType != typeof(byte[]))
        {
            updateSet.Append($", {WrapObjectName(_versionColumn.Name)} = {WrapObjectName(_versionColumn.Name)} + 1");
        }

        var keys = _tableInfo.Columns.Values.Where(c => c.IsPrimaryKey).OrderBy(c => c.PkOrder).ToList();
        if (_idColumn == null && keys.Count == 0)
        {
            throw new NotSupportedException("Upsert requires an Id or a composite primary key.");
        }

        var sc = ctx.CreateSqlContainer();
        sc.Query.Append("INSERT INTO ")
            .Append(WrappedTableName)
            .Append(" (")
            .Append(string.Join(", ", columns))
            .Append(") VALUES (")
            .Append(string.Join(", ", values))
            .Append(") ON DUPLICATE KEY UPDATE ")
            .Append(updateSet);

        sc.AddParameters(parameters);
        return sc;
    }

    private ISqlContainer BuildUpsertMerge(TEntity entity, IDatabaseContext context)
    {
        ValidateSameRoot(context);
        var ctx = context ?? _context;

        PrepareForInsertOrUpsert(entity);

        var srcColumns = new List<string>();
        var insertColumns = new List<string>();
        var values = new List<string>();
        var parameters = new List<DbParameter>();

        foreach (var column in _tableInfo.Columns.Values)
        {
            var value = column.MakeParameterValueFromField(entity);
            string placeholder;
            if (Utils.IsNullOrDbNull(value))
            {
                placeholder = "NULL";
            }
            else
            {
                var p = _context.CreateDbParameter(column.DbType, value);
                parameters.Add(p);
                placeholder = MakeParameterName(p);
            }

            srcColumns.Add(WrapObjectName(column.Name));
            values.Add(placeholder);
            if (!column.IsNonInsertable && (!column.IsId || column.IsIdIsWritable))
            {
                insertColumns.Add(WrapObjectName(column.Name));
            }
        }

        var updateSet = new StringBuilder();
        foreach (var column in _tableInfo.Columns.Values)
        {
            if (column.IsId || column.IsVersion || column.IsNonUpdateable || column.IsCreatedBy || column.IsCreatedOn)
            {
                continue;
            }

            if (_auditValueResolver == null && column.IsLastUpdatedBy)
            {
                // Without a resolver we preserve existing LastUpdatedBy on merge updates.
                continue;
            }

            if (updateSet.Length > 0)
            {
                updateSet.Append(", ");
            }

            updateSet.Append($"t.{WrapObjectName(column.Name)} = s.{WrapObjectName(column.Name)}");
        }

        if (_versionColumn != null && _versionColumn.PropertyInfo.PropertyType != typeof(byte[]))
        {
            updateSet.Append(
                $", t.{WrapObjectName(_versionColumn.Name)} = t.{WrapObjectName(_versionColumn.Name)} + 1");
        }

        var keys = _tableInfo.Columns.Values.Where(c => c.IsPrimaryKey).OrderBy(c => c.PkOrder).ToList();
        if (_idColumn == null && keys.Count == 0)
        {
            throw new NotSupportedException("Upsert requires an Id or a composite primary key.");
        }

        var join = string.Join(" AND ", (keys.Count > 0 ? keys : new List<IColumnInfo> { _idColumn! })
            .Select(k => $"t.{WrapObjectName(k.Name)} = s.{WrapObjectName(k.Name)}"));

        var sc = ctx.CreateSqlContainer();
        sc.Query.Append("MERGE INTO ")
            .Append(WrappedTableName)
            .Append(" AS t USING (VALUES (")
            .Append(string.Join(", ", values))
            .Append(")")
            .Append(" AS s (")
            .Append(string.Join(", ", srcColumns))
            .Append(") ON ")
            .Append(join)
            .Append(" WHEN MATCHED THEN UPDATE SET ")
            .Append(updateSet)
            .Append(" WHEN NOT MATCHED THEN INSERT (")
            .Append(string.Join(", ", insertColumns))
            .Append(") VALUES (")
            .Append(string.Join(", ", insertColumns.Select(c => "s." + c)))
            .Append(");");

        sc.AddParameters(parameters);
        return sc;
    }


    public ISqlContainer BuildWhere(string wrappedColumnName, IEnumerable<TRowID> ids, ISqlContainer sqlContainer)
    {
        var list = ids?.Distinct().ToList();
        if (Utils.IsNullOrEmpty(list))
        {
            return sqlContainer;
        }

        CheckParameterLimit(sqlContainer, list.Count);

        if (list.Any(Utils.IsNullOrDbNull))
        {
            throw new ArgumentException("IDs cannot be null", nameof(ids));
        }

        var inList = new StringBuilder();
        var dbType = _idColumn!.DbType;

        foreach (var id in list)
        {
            if (inList.Length > 0)
            {
                inList.Append(", ");
            }

            var parameter = _context.CreateDbParameter(dbType, id);
            sqlContainer.AddParameter(parameter);
            inList.Append(_dialect.MakeParameterName(parameter));
        }

        AppendWherePrefix(sqlContainer);
        sqlContainer.Query.Append(wrappedColumnName)
            .Append(" IN (")
            .Append(inList)
            .Append(')');

        return sqlContainer;
    }


    private void SetAuditFields(TEntity obj, bool updateOnly)
    {
        if (obj == null)
        {
            return;
        }

        // Skip resolving audit values when no audit columns are present
        if (!_hasAuditColumns)
        {
            return;
        }

        var auditValues = _auditValueResolver?.Resolve();

        var utcNow = auditValues?.UtcNow ?? DateTime.UtcNow;

        // Always update last-modified timestamp
        _tableInfo.LastUpdatedOn?.PropertyInfo?.SetValue(obj, utcNow);
        // If resolver is provided, also set user id
        if (auditValues != null)
        {
            _tableInfo.LastUpdatedBy?.PropertyInfo?.SetValue(obj, auditValues.UserId);
        }

        if (updateOnly)
        {
            return;
        }

        // Only set Created fields if they are null or default
        if (_tableInfo.CreatedOn?.PropertyInfo != null)
        {
            var currentValue = _tableInfo.CreatedOn.PropertyInfo.GetValue(obj) as DateTime?;
            if (currentValue == null || currentValue == default(DateTime))
            {
                _tableInfo.CreatedOn.PropertyInfo.SetValue(obj, utcNow);
            }
        }

        if (auditValues != null && _tableInfo.CreatedBy?.PropertyInfo != null)
        {
            var currentValue = _tableInfo.CreatedBy.PropertyInfo.GetValue(obj);
            if (currentValue == null
                || currentValue as string == string.Empty
                || Utils.IsZeroNumeric(currentValue)
                || (currentValue is Guid guid && guid == Guid.Empty))
            {
                _tableInfo.CreatedBy.PropertyInfo.SetValue(obj, auditValues.UserId);
            }
        }
    }

    private string WrapObjectName(string objectName)
    {
        return _dialect.WrapObjectName(objectName);
    }

    private static bool IsDefaultId(object? value)
    {
        if (Utils.IsNullOrDbNull(value))
            return true;

        var type = typeof(TRowID);
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(string))
            return value as string == string.Empty;

        if (underlying == typeof(Guid))
            return value is Guid g && g == Guid.Empty;

        if (Utils.IsZeroNumeric(value!))
            return true;

        return EqualityComparer<TRowID>.Default.Equals((TRowID)value!, default!);
    }

    private static void ValidateRowIdType()
    {
        var type = typeof(TRowID);
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        bool isValid = underlying == typeof(string) || underlying == typeof(Guid);
        if (!isValid)
        {
            switch (Type.GetTypeCode(underlying))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    isValid = true;
                    break;
                default:
                    isValid = false;
                    break;
            }
        }

        if (!isValid)
            throw new NotSupportedException(
                $"TRowID type '{type.FullName}' is not supported. Use string, Guid, or integer types.");
    }
}