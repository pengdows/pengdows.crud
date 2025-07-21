#region

using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
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

    private IColumnInfo? _idColumn;

    // private readonly IServiceProvider _serviceProvider;
    private ITableInfo _tableInfo;
    private Type? _userFieldType = null;

    private IColumnInfo? _versionColumn;

    public EntityHelper(IDatabaseContext databaseContext,
        EnumParseFailureMode enumParseBehavior = EnumParseFailureMode.Throw
    )
    {
        _auditValueResolver = null;
        Initialize(databaseContext, enumParseBehavior);
    }

    [Obsolete("Use the constructor without IServiceProvider instead")]
    public EntityHelper(IDatabaseContext databaseContext,
        IAuditValueResolver auditValueResolver,
        EnumParseFailureMode enumParseBehavior = EnumParseFailureMode.Throw
    )
    {
        _auditValueResolver = auditValueResolver;
        Initialize(databaseContext, enumParseBehavior);
    }

    private void Initialize(IDatabaseContext databaseContext, EnumParseFailureMode enumParseBehavior)
    {
        _context = databaseContext;
        _tableInfo = _context.TypeMapRegistry.GetTableInfo<TEntity>() ??
                     throw new InvalidOperationException($"Type {typeof(TEntity).FullName} is not a table.");

        var propertyInfoPropertyType = _tableInfo.Columns
            .Values
            .FirstOrDefault(c =>
                c.PropertyInfo.GetCustomAttribute<CreatedByAttribute>() != null ||
                c.PropertyInfo.GetCustomAttribute<LastUpdatedByAttribute>() != null
            )?.PropertyInfo.PropertyType;

        if (propertyInfoPropertyType != null) _userFieldType = propertyInfoPropertyType;

        WrappedTableName = (!string.IsNullOrEmpty(_tableInfo.Schema)
                               ? WrapObjectName(_tableInfo.Schema) +
                                 _context.CompositeIdentifierSeparator
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


    public string MakeParameterName(DbParameter p)
    {
        return _context.MakeParameterName(p);
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
        var sc = BuildCreate(entity, context);
        return await sc.ExecuteNonQueryAsync() == 1;
    }
    
    public ISqlContainer BuildCreate(TEntity objectToCreate, IDatabaseContext? context = null)
    {
        if (objectToCreate == null)
            throw new ArgumentNullException(nameof(objectToCreate));

        var ctx = context ?? _context;
        var columns = new StringBuilder();
        var values = new StringBuilder();
        var parameters = new List<DbParameter>();

        var sc = ctx.CreateSqlContainer();
        SetAuditFields(objectToCreate, false);

        // Initialize version to 1 if a version column exists and the current value is unset
        if (_versionColumn != null)
        {
            var current = _versionColumn.PropertyInfo.GetValue(objectToCreate);
            if (current == null || Utils.IsZeroNumeric(current))
            {
                var target = Nullable.GetUnderlyingType(_versionColumn.PropertyInfo.PropertyType) ??
                             _versionColumn.PropertyInfo.PropertyType;

                // Only set numeric version columns
                if (Utils.IsZeroNumeric(Convert.ChangeType(0, target)))
                {
                    var one = Convert.ChangeType(1, target);
                    _versionColumn.PropertyInfo.SetValue(objectToCreate, one);
                }
            }
        }
        foreach (var column in _tableInfo.Columns.Values)
        {
            if (column.IsNonInsertable) continue;
            if (column.IsId && !column.IsIdIsWritable) continue;

            var value = column.MakeParameterValueFromField(objectToCreate);

            // If no audit resolver is provided and the value is null for a user audit column,
            // skip including this column so database defaults will apply.
            if (_auditValueResolver == null &&
                (column.IsCreatedBy || column.IsLastUpdatedBy) &&
                Utils.IsNullOrDbNull(value))
                continue;

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
        var wrappedAlias = "";
        if (!string.IsNullOrWhiteSpace(alias))
            wrappedAlias = WrapObjectName(alias) +
                           _context.CompositeIdentifierSeparator;
        var ctx = context ?? _context;
        var sc = ctx.CreateSqlContainer();
        var sb = sc.Query;
        sb.Append("SELECT ");
        sb.Append(string.Join(", ", _tableInfo.Columns.Values.Select(col => string.Format("{0}{1}",
            wrappedAlias,
            WrapObjectName(col.Name)))));
        sb.Append("\nFROM ").Append(WrappedTableName);
        if (wrappedAlias.Length > 0) sb.Append($" {wrappedAlias.Substring(0, wrappedAlias.Length - 1)}");

        return sc;
    }

    public ISqlContainer BuildDelete(TRowID id, IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;
        var sc = ctx.CreateSqlContainer();

        var idCol = _idColumn;
        if (idCol == null)
            throw new InvalidOperationException($"row identity column for table {WrappedTableName} not found");

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
        var sc = BuildDelete(id, context);
        return await sc.ExecuteNonQueryAsync();
    }

    public async Task<List<TEntity>> RetrieveAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null)
    {
        if (ids == null) throw new ArgumentNullException(nameof(ids));

        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new List<TEntity>();

        var ctx = context ?? _context;
        var sc = BuildRetrieve(list, ctx);
        return await LoadListAsync(sc);
    }

    public async Task<int> DeleteAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null)
    {
        if (ids == null) throw new ArgumentNullException(nameof(ids));

        var list = ids.Distinct().ToList();
        if (list.Count == 0) return 0;

        var ctx = context ?? _context;
        var sc = ctx.CreateSqlContainer();
        sc.Query.Append("DELETE FROM ").Append(WrappedTableName);
        BuildWhere(WrapObjectName(_idColumn!.Name), list, sc);
        return await sc.ExecuteNonQueryAsync();
    }


    public Action<object, object?> GetOrCreateSetter(PropertyInfo prop)
    {
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
        var ctx = context ?? _context;
        //var id = (TRowID)_idColumn.PropertyInfo.GetValue(objectToRetrieve);
        var list = new List<TEntity>() { objectToRetrieve };
        var sc = BuildRetrieve(list, null, ctx);
        return LoadSingleAsync(sc);
    }

    public Task<TEntity?> RetrieveOneAsync(TRowID id, IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;
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
            if (obj != null) list.Add(obj);
        }

        return list;
    }

    public ISqlContainer BuildRetrieve(IReadOnlyCollection<TRowID>? listOfIds = null,
        string alias = "a", IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;
        var sc = BuildBaseRetrieve(alias, ctx);
        var wrappedAlias = "";
        if (!string.IsNullOrWhiteSpace(alias))
            wrappedAlias = WrapObjectName(alias) +
                           _context.CompositeIdentifierSeparator;

        var wrappedColumnName = wrappedAlias +
                                WrapObjectName(_idColumn.Name);

        if (listOfIds != null && listOfIds.Any(id => Utils.IsNullOrDbNull(id)))
            throw new ArgumentException("IDs cannot be null", nameof(listOfIds));
        BuildWhere(
            wrappedColumnName,
            listOfIds,
            sc
        );

        return sc;
    }

    public ISqlContainer BuildRetrieve(IReadOnlyCollection<TEntity>? listOfObjects = null,
        string alias = "a", IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;
        var sc = BuildBaseRetrieve(alias, ctx);
        var wrappedAlias = "";
        if (!string.IsNullOrWhiteSpace(alias))
            wrappedAlias = WrapObjectName(alias) +
                           _context.CompositeIdentifierSeparator;

        var wrappedColumnName = wrappedAlias +
                                WrapObjectName(_idColumn.Name);
        BuildWhereByPrimaryKey(
            listOfObjects,
            sc);

        return sc;
    }

    public ISqlContainer BuildRetrieve(IReadOnlyCollection<TRowID>? listOfIds = null, IDatabaseContext? context = null)
    {
        return BuildRetrieve(listOfIds, null, context);
    }

    public ISqlContainer BuildRetrieve(IReadOnlyCollection<TEntity>? listOfObjects = null,
        IDatabaseContext? context = null)
    {
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
            if (index++ > 0) sb.Append(" OR ");
            sb.Append(BuildPrimaryKeyClause(entity, keys, wrappedAlias, parameters));
        }

        if (sb.Length == 0) return;

        sc.AddParameters(parameters);
        AppendWherePrefix(sc);
        sc.Query.Append(sb);
    }

    private void ValidateWhereInputs(IReadOnlyCollection<TEntity>? listOfObjects, ISqlContainer sc)
    {
        if (Utils.IsNullOrEmpty(listOfObjects) || sc == null)
            throw new ArgumentException("List of objects cannot be null or empty.");
    }

    private List<IColumnInfo> GetPrimaryKeys()
    {
        var keys = _tableInfo.Columns.Values.Where(o => o.IsPrimaryKey).ToList();
        if (keys.Count < 1) throw new Exception($"No primary keys found for type {typeof(TEntity).Name}");
        return keys;
    }

    private void CheckParameterLimit(ISqlContainer sc, int? toAdd)
    {
        var count = sc.ParameterCount + (toAdd ?? 0);
        if (count > _context.MaxParameterLimit)
            throw new TooManyParametersException("Too many parameters", _context.MaxParameterLimit);
    }

    private static string BuildAliasPrefix(string alias) =>
        string.IsNullOrWhiteSpace(alias) ? string.Empty : alias + ".";

    private string BuildPrimaryKeyClause(TEntity entity, IReadOnlyList<IColumnInfo> keys, string alias, List<DbParameter> parameters)
    {
        var clause = new StringBuilder("(");
        for (var i = 0; i < keys.Count; i++)
        {
            if (i > 0) clause.Append(" AND ");

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
        var query = sc.Query.ToString();
        if (!query.Contains("WHERE ", StringComparison.OrdinalIgnoreCase))
            sc.Query.Append("\n WHERE ");
        else
            sc.Query.Append("\n AND ");
    }


    public Task<ISqlContainer> BuildUpdateAsync(TEntity objectToUpdate, IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;
        return BuildUpdateAsync(objectToUpdate, _versionColumn != null, ctx);
    }

    public async Task<ISqlContainer> BuildUpdateAsync(TEntity objectToUpdate, bool loadOriginal,
        IDatabaseContext? context = null)
    {
        if (objectToUpdate == null)
            throw new ArgumentNullException(nameof(objectToUpdate));

        context ??= _context;
        var sc = context.CreateSqlContainer();

        var original = loadOriginal ? await LoadOriginalAsync(objectToUpdate) : null;
        if (loadOriginal && original == null)
            throw new InvalidOperationException("Original record not found for update.");

        // Determine if any non-audit fields have changed before modifying audit values
        var (preClause, _) = BuildSetClause(objectToUpdate, original, context);
        if (preClause.Length == 0)
            throw new InvalidOperationException("No changes detected for update.");

        // Apply audit field changes now that we know an update is required
        SetAuditFields(objectToUpdate, true);

        var (setClause, parameters) = BuildSetClause(objectToUpdate, original, context);

        if (_versionColumn != null) IncrementVersion(setClause);

        var pId = context.CreateDbParameter(_idColumn!.DbType,
            _idColumn.PropertyInfo.GetValue(objectToUpdate)!);
        parameters.Add(pId);

        sc.Query.Append("UPDATE ")
            .Append(WrappedTableName)
            .Append(" SET ")
            .Append(setClause)
            .Append(" WHERE ")
            .Append(WrapObjectName(_idColumn.Name))
            .Append($" = {MakeParameterName(pId)}");

        if (_versionColumn != null)
        {
            var versionValue = _versionColumn.MakeParameterValueFromField(objectToUpdate);
            AppendVersionCondition(sc, versionValue, context, parameters);
        }

        sc.AddParameters(parameters);
        return sc;
    }

    public Task<int> UpdateAsync(TEntity objectToUpdate, IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;
        return UpdateAsync(objectToUpdate, _versionColumn != null, ctx);
    }

    public async Task<int> UpdateAsync(TEntity objectToUpdate, bool loadOriginal, IDatabaseContext? context = null)
    {
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
        if (entity == null) throw new ArgumentNullException(nameof(entity));

        context ??= _context;

        var idValue = _idColumn!.PropertyInfo.GetValue(entity);

        if (IsDefaultId(idValue))
            return await CreateAsync(entity, context) ? 1 : 0;

        if (_idColumn.IsIdIsWritable)
        {
            try
            {
                var sc = BuildUpsert(entity, context);
                return await sc.ExecuteNonQueryAsync();
            }
            catch (NotSupportedException)
            {
                // fall back to update
            }
        }

        return await UpdateAsync(entity, context);
    }

    public ISqlContainer BuildUpsert(TEntity entity, IDatabaseContext? context = null)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity));

        context ??= _context;

        if (context.Product == SupportedDatabase.PostgreSql)
        {
            if (TryParseMajorVersion(context.DataSourceInfo.DatabaseProductVersion, out var major) && major > 14)
                return BuildUpsertMerge(entity, context);

            return BuildUpsertOnConflict(entity, context);
        }

        return context.Product switch
        {
            SupportedDatabase.CockroachDb or SupportedDatabase.Sqlite => BuildUpsertOnConflict(entity, context),
            SupportedDatabase.MySql or SupportedDatabase.MariaDb => BuildUpsertOnDuplicate(entity, context),
            SupportedDatabase.SqlServer => BuildUpsertMerge(entity, context),
            _ => throw new NotSupportedException($"Upsert not supported for {context.Product}")
        };
    }

    private async Task<TEntity?> LoadOriginalAsync(TEntity objectToUpdate)
    {
        var idValue = _idColumn!.PropertyInfo.GetValue(objectToUpdate);
        if (IsDefaultId(idValue))
            return null;

        return await RetrieveOneAsync((TRowID)idValue!);
    }

    private (StringBuilder clause, List<DbParameter> parameters) BuildSetClause(TEntity updated, TEntity? original, IDatabaseContext context)
    {
        var clause = new StringBuilder();
        var parameters = new List<DbParameter>();

        foreach (var column in _tableInfo.Columns.Values)
        {
            if (column.IsId || column.IsVersion || column.IsNonUpdateable || column.IsCreatedBy || column.IsCreatedOn)
                continue;

            var newValue = column.MakeParameterValueFromField(updated);
            var originalValue = original != null ? column.MakeParameterValueFromField(original) : null;

            if (original != null && Equals(newValue, originalValue))
                continue;

            if (clause.Length > 0) clause.Append(", ");

            if (newValue == null)
            {
                clause.Append($"{WrapObjectName(column.Name)} = NULL");
            }
            else
            {
                var param = context.CreateDbParameter(column.DbType, newValue);
                parameters.Add(param);
                clause.Append($"{WrapObjectName(column.Name)} = {MakeParameterName(param)}");
            }
        }

        return (clause, parameters);
    }

    private void IncrementVersion(StringBuilder setClause)
    {
        setClause.Append($", {WrapObjectName(_versionColumn!.Name)} = {WrapObjectName(_versionColumn.Name)} + 1");
    }

    private void AppendVersionCondition(ISqlContainer sc, object? versionValue, IDatabaseContext context, List<DbParameter> parameters)
    {
        if (versionValue == null)
        {
            sc.Query.Append(" AND ").Append(WrapObjectName(_versionColumn!.Name)).Append(" IS NULL");
        }
        else
        {
            var pVersion = context.CreateDbParameter(_versionColumn!.DbType, versionValue);
            sc.Query.Append(" AND ").Append(WrapObjectName(_versionColumn.Name))
                .Append($" = {MakeParameterName(pVersion)}");
            parameters.Add(pVersion);
        }
    }

    private ISqlContainer BuildUpsertOnConflict(TEntity entity, IDatabaseContext context)
    {
        SetAuditFields(entity, false);
        if (_versionColumn != null)
        {
            var current = _versionColumn.PropertyInfo.GetValue(entity);
            if (current == null || Utils.IsZeroNumeric(current))
            {
                var target = Nullable.GetUnderlyingType(_versionColumn.PropertyInfo.PropertyType) ??
                             _versionColumn.PropertyInfo.PropertyType;
                if (Utils.IsZeroNumeric(Convert.ChangeType(0, target)))
                {
                    var one = Convert.ChangeType(1, target);
                    _versionColumn.PropertyInfo.SetValue(entity, one);
                }
            }
        }

        var columns = new List<string>();
        var values = new List<string>();
        var parameters = new List<DbParameter>();

        foreach (var column in _tableInfo.Columns.Values)
        {
            if (column.IsNonInsertable) continue;
            if (column.IsId && !column.IsIdIsWritable) continue;

            var value = column.MakeParameterValueFromField(entity);

            columns.Add(WrapObjectName(column.Name));
            if (Utils.IsNullOrDbNull(value))
            {
                values.Add("NULL");
            }
            else
            {
                var p = context.CreateDbParameter(column.DbType, value);
                parameters.Add(p);
                values.Add(MakeParameterName(p));
            }
        }

        var updateSet = new StringBuilder();
        foreach (var column in _tableInfo.Columns.Values)
        {
            if (column.IsId || column.IsVersion || column.IsNonUpdateable || column.IsCreatedBy || column.IsCreatedOn)
                continue;

            if (updateSet.Length > 0) updateSet.Append(", ");
            updateSet.Append($"{WrapObjectName(column.Name)} = EXCLUDED.{WrapObjectName(column.Name)}");
        }

        if (_versionColumn != null)
            updateSet.Append($", {WrapObjectName(_versionColumn.Name)} = {WrapObjectName(_versionColumn.Name)} + 1");

        var sc = context.CreateSqlContainer();
        sc.Query.Append("INSERT INTO ")
            .Append(WrappedTableName)
            .Append(" (")
            .Append(string.Join(", ", columns))
            .Append(") VALUES (")
            .Append(string.Join(", ", values))
            .Append(") ON CONFLICT (")
            .Append(WrapObjectName(_idColumn!.Name))
            .Append(") DO UPDATE SET ")
            .Append(updateSet);

        sc.AddParameters(parameters);
        return sc;
    }

    private ISqlContainer BuildUpsertOnDuplicate(TEntity entity, IDatabaseContext context)
    {
        SetAuditFields(entity, false);
        if (_versionColumn != null)
        {
            var current = _versionColumn.PropertyInfo.GetValue(entity);
            if (current == null || Utils.IsZeroNumeric(current))
            {
                var target = Nullable.GetUnderlyingType(_versionColumn.PropertyInfo.PropertyType) ??
                             _versionColumn.PropertyInfo.PropertyType;
                if (Utils.IsZeroNumeric(Convert.ChangeType(0, target)))
                {
                    var one = Convert.ChangeType(1, target);
                    _versionColumn.PropertyInfo.SetValue(entity, one);
                }
            }
        }

        var columns = new List<string>();
        var values = new List<string>();
        var parameters = new List<DbParameter>();

        foreach (var column in _tableInfo.Columns.Values)
        {
            if (column.IsNonInsertable) continue;
            if (column.IsId && !column.IsIdIsWritable) continue;

            var value = column.MakeParameterValueFromField(entity);

            columns.Add(WrapObjectName(column.Name));
            if (Utils.IsNullOrDbNull(value))
            {
                values.Add("NULL");
            }
            else
            {
                var p = context.CreateDbParameter(column.DbType, value);
                parameters.Add(p);
                values.Add(MakeParameterName(p));
            }
        }

        var updateSet = new StringBuilder();
        foreach (var column in _tableInfo.Columns.Values)
        {
            if (column.IsId || column.IsVersion || column.IsNonUpdateable || column.IsCreatedBy || column.IsCreatedOn)
                continue;

            if (updateSet.Length > 0) updateSet.Append(", ");
            updateSet.Append($"{WrapObjectName(column.Name)} = VALUES({WrapObjectName(column.Name)})");
        }

        if (_versionColumn != null)
            updateSet.Append($", {WrapObjectName(_versionColumn.Name)} = {WrapObjectName(_versionColumn.Name)} + 1");

        var sc = context.CreateSqlContainer();
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
        SetAuditFields(entity, false);
        if (_versionColumn != null)
        {
            var current = _versionColumn.PropertyInfo.GetValue(entity);
            if (current == null || Utils.IsZeroNumeric(current))
            {
                var target = Nullable.GetUnderlyingType(_versionColumn.PropertyInfo.PropertyType) ??
                             _versionColumn.PropertyInfo.PropertyType;
                if (Utils.IsZeroNumeric(Convert.ChangeType(0, target)))
                {
                    var one = Convert.ChangeType(1, target);
                    _versionColumn.PropertyInfo.SetValue(entity, one);
                }
            }
        }

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
                var p = context.CreateDbParameter(column.DbType, value);
                parameters.Add(p);
                placeholder = MakeParameterName(p);
            }

            srcColumns.Add(WrapObjectName(column.Name));
            values.Add(placeholder);
            if (!column.IsNonInsertable && (!column.IsId || column.IsIdIsWritable))
                insertColumns.Add(WrapObjectName(column.Name));
        }

        var updateSet = new StringBuilder();
        foreach (var column in _tableInfo.Columns.Values)
        {
            if (column.IsId || column.IsVersion || column.IsNonUpdateable || column.IsCreatedBy || column.IsCreatedOn)
                continue;

            if (updateSet.Length > 0) updateSet.Append(", ");
            updateSet.Append($"t.{WrapObjectName(column.Name)} = s.{WrapObjectName(column.Name)}");
        }

        if (_versionColumn != null)
            updateSet.Append($", t.{WrapObjectName(_versionColumn.Name)} = t.{WrapObjectName(_versionColumn.Name)} + 1");

        var sc = context.CreateSqlContainer();
        sc.Query.Append("MERGE INTO ")
            .Append(WrappedTableName)
            .Append(" AS t USING (VALUES (")
            .Append(string.Join(", ", values))
            .Append(")")
            .Append(" AS s (")
            .Append(string.Join(", ", srcColumns))
            .Append(") ON t.")
            .Append(WrapObjectName(_idColumn!.Name))
            .Append(" = s.")
            .Append(WrapObjectName(_idColumn.Name))
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
        var enumerable = ids?.Distinct().ToList();
        if (Utils.IsNullOrEmpty(enumerable)) return sqlContainer;


        var hasNull = enumerable.Any(v => Utils.IsNullOrDbNull(v));
        var sb = new StringBuilder();
        var dbType = _idColumn!.DbType;
        foreach (var id in enumerable)
            if (!hasNull || !Utils.IsNullOrDbNull(id))
            {
                if (sb.Length > 0) sb.Append(", ");

                var p = sqlContainer.AddParameterWithValue(dbType, id);
                var name = MakeParameterName(p);
                sb.Append(name);
            }

        if (sb.Length > 0)
        {
            sb.Insert(0, wrappedColumnName + " IN (");
            sb.Append(")  ");
        }

        if (hasNull)
        {
            if (sb.Length > 0) sb.Append("\nOR ");
            sb.Append(wrappedColumnName + " IS NULL");
        }

        sb.Insert(0, "\nWHERE ");
        sqlContainer.Query.Append(sb);
        return sqlContainer;
    }


    private void SetAuditFields(TEntity obj, bool updateOnly)
    {
        if (obj == null)
            return;

        // Skip resolving audit values when no audit columns are present
        if (_tableInfo.CreatedBy == null &&
            _tableInfo.CreatedOn == null &&
            _tableInfo.LastUpdatedBy == null &&
            _tableInfo.LastUpdatedOn == null)
            return;

        var auditValues = _auditValueResolver?.Resolve();

        var utcNow = auditValues?.UtcNow ?? DateTime.UtcNow;

        // Always update last-modified timestamp
        _tableInfo.LastUpdatedOn?.PropertyInfo?.SetValue(obj, utcNow);
        // If resolver is provided, also set user id
        if (auditValues != null)
            _tableInfo.LastUpdatedBy?.PropertyInfo?.SetValue(obj, auditValues.UserId);

        if (updateOnly) return;

        // Only set Created fields if they are null or default
        if (_tableInfo.CreatedOn?.PropertyInfo != null)
        {
            var currentValue = _tableInfo.CreatedOn.PropertyInfo.GetValue(obj) as DateTime?;
            if (currentValue == null || currentValue == default(DateTime))
                _tableInfo.CreatedOn.PropertyInfo.SetValue(obj, utcNow);
        }

        if (auditValues != null && _tableInfo.CreatedBy?.PropertyInfo != null)
        {
            var currentValue = _tableInfo.CreatedBy.PropertyInfo.GetValue(obj);
            if (currentValue == null
                || currentValue as string == string.Empty
                || Utils.IsZeroNumeric(currentValue)
                || (currentValue is Guid guid && guid == Guid.Empty))
                _tableInfo.CreatedBy.PropertyInfo.SetValue(obj, auditValues.UserId);
        }
    }

    private string WrapObjectName(string objectName)
    {
        return _context.WrapObjectName(objectName);
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

    private static bool TryParseMajorVersion(string version, out int major)
    {
        major = 0;
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var match = Regex.Match(version, "(\\d+)");
        if (!match.Success)
        {
            return false;
        }
        return int.TryParse(match.Groups[1].Value, out major);
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