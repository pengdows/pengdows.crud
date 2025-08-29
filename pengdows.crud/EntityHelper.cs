#region

using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

    private readonly Lazy<CachedSqlTemplates> _cachedSqlTemplates;

    private class CachedSqlTemplates
    {
        public string InsertSql = null!;
        public List<IColumnInfo> InsertColumns = null!;
        public List<string> InsertParameterNames = null!;
        public string DeleteSql = null!;
        public string UpdateSql = null!;
        public List<IColumnInfo> UpdateColumns = null!;
    }

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

    private IColumnInfo? _idColumn;

    // private readonly IServiceProvider _serviceProvider;
    private ITableInfo _tableInfo;
    private bool _hasAuditColumns;

    private IColumnInfo? _versionColumn;

    private readonly ConcurrentDictionary<IColumnInfo, Func<object?, object?>> _readerConverters = new();

    private readonly ConcurrentDictionary<string, IReadOnlyList<IColumnInfo>> _columnListCache = new();

    private readonly ConcurrentDictionary<string, string> _queryCache = new();

    private readonly ConcurrentDictionary<string, string[]> _whereParameterNames = new();

    private const int MaxCacheSize = 100;

    private static void TryAddWithLimit<TKey, TValue>(ConcurrentDictionary<TKey, TValue> cache, TKey key,
        TValue value)
    {
        cache.TryAdd(key, value);
        if (cache.Count > MaxCacheSize)
        {
            cache.Clear();
            cache.TryAdd(key, value);
        }
    }

    public void ClearCaches()
    {
        _readerConverters.Clear();
        _columnListCache.Clear();
        _queryCache.Clear();
        _whereParameterNames.Clear();
    }

    public EntityHelper(IDatabaseContext databaseContext,
        EnumParseFailureMode enumParseBehavior = EnumParseFailureMode.Throw
    )
    {
        _auditValueResolver = null;
        Initialize(databaseContext, enumParseBehavior);
        _cachedSqlTemplates = new Lazy<CachedSqlTemplates>(BuildCachedSqlTemplates);
    }

    public EntityHelper(IDatabaseContext databaseContext,
        IAuditValueResolver auditValueResolver,
        EnumParseFailureMode enumParseBehavior = EnumParseFailureMode.Throw
    )
    {
        _auditValueResolver = auditValueResolver;
        Initialize(databaseContext, enumParseBehavior);
        _cachedSqlTemplates = new Lazy<CachedSqlTemplates>(BuildCachedSqlTemplates);
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

        // Note: Validation for missing audit resolver with user fields is now handled 
        // in SetAuditFields method where it throws InvalidOperationException

        EnumParseBehavior = enumParseBehavior;
    }

    public string WrappedTableName { get; set; }

    public EnumParseFailureMode EnumParseBehavior { get; set; }

    public string QuotePrefix => _dialect.QuotePrefix;

    public string QuoteSuffix => _dialect.QuoteSuffix;

    public string CompositeIdentifierSeparator => _dialect.CompositeIdentifierSeparator;

    public string MakeParameterName(DbParameter p)
    {
        return _dialect.MakeParameterName(p);
    }

    public string ReplaceDialectTokens(string sql, string quotePrefix, string quoteSuffix, string parameterMarker)
    {
        if (sql == null)
        {
            throw new ArgumentNullException(nameof(sql));
        }

        var dialectPrefix = _dialect.QuotePrefix;
        var dialectSuffix = _dialect.QuoteSuffix;
        var dialectMarker = _dialect.ParameterMarker;

        var sb = new StringBuilder(sql.Length);
        var inQuote = false;

        for (var i = 0; i < sql.Length; i++)
        {
            if (!inQuote && sql.AsSpan(i).StartsWith(dialectPrefix))
            {
                sb.Append(quotePrefix);
                i += dialectPrefix.Length - 1;
                inQuote = true;
                continue;
            }

            if (inQuote && sql.AsSpan(i).StartsWith(dialectSuffix))
            {
                sb.Append(quoteSuffix);
                i += dialectSuffix.Length - 1;
                inQuote = false;
                continue;
            }

            if (sql.AsSpan(i).StartsWith(dialectMarker))
            {
                sb.Append(parameterMarker);
                i += dialectMarker.Length - 1;
                continue;
            }

            sb.Append(sql[i]);
        }

        return sb.ToString();
    }

    private string GetCachedQuery(string key, Func<string> factory)
    {
        if (_queryCache.TryGetValue(key, out var sql))
        {
            return sql;
        }

        sql = factory();
        TryAddWithLimit(_queryCache, key, sql);
        return sql;
    }

    public TEntity MapReaderToObject(ITrackedReader reader)
    {
        var obj = new TEntity();

        for (var i = 0; i < reader.FieldCount; i++)
        {
            var colName = reader.GetName(i);
            if (_tableInfo.Columns.TryGetValue(colName, out var column))
            {
                var raw = reader.IsDBNull(i) ? null : reader.GetValue(i);
                var converter = GetOrCreateReaderConverter(column);
                var coerced = converter(raw);
                try
                {
                    var setter = GetOrCreateSetter(column.PropertyInfo);
                    setter(obj, coerced);
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

    private Func<object?, object?> GetOrCreateReaderConverter(IColumnInfo column)
    {
        if (_readerConverters.TryGetValue(column, out var existing))
        {
            return existing;
        }

        Func<object?, object?> converter;

        if (column.IsEnum && column.EnumType != null)
        {
            var enumType = column.EnumType;
            var enumAsString = column.DbType == DbType.String;
            var underlying = Enum.GetUnderlyingType(enumType);
            if (enumAsString)
            {
                converter = value =>
                {
                    if (value == null || value is DBNull)
                    {
                        return null;
                    }

                    var s = value as string ?? value.ToString();
                    try
                    {
                        return Enum.Parse(enumType, s!, false);
                    }
                    catch
                    {
                        switch (EnumParseBehavior)
                        {
                            case EnumParseFailureMode.Throw:
                                throw;
                            case EnumParseFailureMode.SetNullAndLog:
                                Logger.LogWarning("Cannot convert '{Value}' to enum {EnumType}.", s, enumType);
                                return null;
                            case EnumParseFailureMode.SetDefaultValue:
                                return Activator.CreateInstance(enumType);
                            default:
                                return null;
                        }
                    }
                };
            }
            else
            {
                converter = value =>
                {
                    if (value == null || value is DBNull)
                    {
                        return null;
                    }

                    try
                    {
                        var boxed = Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
                        return Enum.ToObject(enumType, boxed!);
                    }
                    catch
                    {
                        switch (EnumParseBehavior)
                        {
                            case EnumParseFailureMode.Throw:
                                throw;
                            case EnumParseFailureMode.SetNullAndLog:
                                Logger.LogWarning("Cannot convert '{Value}' to enum {EnumType}.", value, enumType);
                                return null;
                            case EnumParseFailureMode.SetDefaultValue:
                                return Activator.CreateInstance(enumType);
                            default:
                                return null;
                        }
                    }
                };
            }

            TryAddWithLimit(_readerConverters, column, converter);
            return converter;
        }

        if (column.IsJsonType)
        {
            var propType = column.PropertyInfo.PropertyType;
            var opts = column.JsonSerializerOptions ?? new JsonSerializerOptions();
            converter = value =>
            {
                if (value == null || value is DBNull)
                {
                    return null;
                }

                var s = value as string ?? value.ToString();
                return JsonSerializer.Deserialize(s!, propType, opts);
            };

            TryAddWithLimit(_readerConverters, column, converter);
            return converter;
        }

        converter = value =>
        {
            if (value == null || value is DBNull)
            {
                return null;
            }

            var targetType = column.PropertyInfo.PropertyType;
            var sourceType = value.GetType();

            if (targetType.IsAssignableFrom(sourceType))
            {
                return value;
            }

            var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

            try
            {
                return Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
            }
            catch
            {
                switch (column.DbType)
                {
                    case DbType.Decimal:
                    case DbType.Currency:
                    case DbType.VarNumeric:
                        return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    default:
                        return value;
                }
            }
        };

        TryAddWithLimit(_readerConverters, column, converter);
        return converter;
    }

    public async Task<bool> CreateAsync(TEntity entity, IDatabaseContext context)
    {
        var sc = BuildCreate(entity, context);
        var rowsAffected = await sc.ExecuteNonQueryAsync();
        
        // For non-writable ID columns, retrieve the generated ID and populate it on the entity
        if (rowsAffected == 1 && _idColumn != null && !_idColumn.IsIdIsWritable)
        {
            await PopulateGeneratedIdAsync(entity, context);
        }
        
        return rowsAffected == 1;
    }

    private async Task PopulateGeneratedIdAsync(TEntity entity, IDatabaseContext context)
    {
        if (_idColumn == null) return;
        
        // Get the database-specific query for retrieving the last inserted ID
        var lastIdQuery = GetLastInsertedIdQuery(context);
        if (string.IsNullOrEmpty(lastIdQuery)) return;
        
        var sc = context.CreateSqlContainer(lastIdQuery);
        var generatedId = await sc.ExecuteScalarAsync<object>();
        
        if (generatedId != null)
        {
            // Convert the ID to the appropriate type and set it on the entity
            var convertedId = Convert.ChangeType(generatedId, _idColumn.PropertyInfo.PropertyType);
            _idColumn.PropertyInfo.SetValue(entity, convertedId);
        }
    }

    private string GetLastInsertedIdQuery(IDatabaseContext context)
    {
        // Return database-specific query to get the last inserted ID
        return context.Product switch
        {
            SupportedDatabase.Sqlite => "SELECT last_insert_rowid()",
            SupportedDatabase.SqlServer => "SELECT SCOPE_IDENTITY()",
            SupportedDatabase.MySql => "SELECT LAST_INSERT_ID()",
            SupportedDatabase.PostgreSql => "SELECT lastval()",
            SupportedDatabase.Oracle => "SELECT @@IDENTITY",
            _ => string.Empty
        };
    }

    public ISqlContainer BuildCreate(TEntity entity, IDatabaseContext? context = null)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        var ctx = context ?? _context;
        var sc = ctx.CreateSqlContainer();
        
        // Always attempt to set audit fields if they exist - validation will happen inside SetAuditFields
        if (_hasAuditColumns)
        {
            SetAuditFields(entity, false);
        }

        if (_versionColumn != null)
        {
            var current = _versionColumn.PropertyInfo.GetValue(entity);
            if (current == null || Utils.IsZeroNumeric(current))
            {
                var target = Nullable.GetUnderlyingType(_versionColumn.PropertyInfo.PropertyType) ?? _versionColumn.PropertyInfo.PropertyType;
                if (Utils.IsZeroNumeric(Convert.ChangeType(0, target)))
                {
                    var one = Convert.ChangeType(1, target);
                    _versionColumn.PropertyInfo.SetValue(entity, one);
                }
            }
        }

        var template = _cachedSqlTemplates.Value;
        var columns = new List<string>();
        var placeholders = new List<string>();

        for (var i = 0; i < template.InsertColumns.Count; i++)
        {
            var column = template.InsertColumns[i];
            var value = column.MakeParameterValueFromField(entity);

            var paramName = template.InsertParameterNames[i];
            var param = ctx.CreateDbParameter(paramName, column.DbType, value);
            sc.AddParameter(param);
            columns.Add(WrapObjectName(column.Name));
            placeholders.Add(ctx.MakeParameterName(param));
        }

        sc.Query.Append("INSERT INTO ")
            .Append(WrappedTableName)
            .Append(" (")
            .Append(string.Join(", ", columns))
            .Append(") VALUES (")
            .Append(string.Join(", ", placeholders))
            .Append(")");

        return sc;
    }


    public ISqlContainer BuildBaseRetrieve(string alias, IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;

        var sc = ctx.CreateSqlContainer();
        var sql = GetCachedQuery($"BaseRetrieve:{alias}", () =>
        {
            var hasAlias = !string.IsNullOrWhiteSpace(alias);
            var selectList = _tableInfo.OrderedColumns
                .Select(col => (hasAlias
                    ? _dialect.WrapObjectName(alias) + _dialect.CompositeIdentifierSeparator
                    : string.Empty) + _dialect.WrapObjectName(col.Name));
            var sb = new StringBuilder();
            sb.Append("SELECT ")
                .Append(string.Join(", ", selectList))
                .Append("\nFROM ")
                .Append(WrappedTableName);
            if (hasAlias)
            {
                sb.Append(' ').Append(_dialect.WrapObjectName(alias));
            }

            return sb.ToString();
        });

        sc.Query.Append(sql);
        return sc;
    }

    public ISqlContainer BuildDelete(TRowID id, IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;
        var sc = ctx.CreateSqlContainer();

        if (_idColumn == null)
        {
            throw new InvalidOperationException($"row identity column for table {WrappedTableName} not found");
        }

        var param = ctx.CreateDbParameter(_idColumn.DbType, id);
        sc.AddParameter(param);

        var sql = GetCachedQuery("DeleteById", () => string.Format(_cachedSqlTemplates.Value.DeleteSql, MakeParameterName(param)));
        sc.Query.Append(sql);
        return sc;
    }

    public async Task<int> DeleteAsync(TRowID id, IDatabaseContext? context = null)
    {
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
        var ctx = context ?? _context;
        var list = new List<TEntity> { objectToRetrieve };
        var sc = BuildRetrieve(list, string.Empty, ctx);
        return LoadSingleAsync(sc);
    }

    public Task<TEntity?> RetrieveOneAsync(TRowID id, IDatabaseContext? context = null)
    {
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
        if (await reader.ReadAsync().ConfigureAwait(false))
        {
            return MapReaderToObject(reader);
        }

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
        return BuildRetrieve(listOfIds, "", context);
    }

    public ISqlContainer BuildRetrieve(IReadOnlyCollection<TEntity>? listOfObjects,
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

    private IReadOnlyList<IColumnInfo> GetPrimaryKeys()
    {
        var keys = _tableInfo.PrimaryKeys;
        if (keys.Count < 1)
        {
            throw new Exception($"No primary keys found for type {typeof(TEntity).Name}");
        }

        return keys;
    }

    private IReadOnlyList<IColumnInfo> GetCachedInsertableColumns()
    {
        if (_columnListCache.TryGetValue("Insertable", out var cached))
        {
            return cached;
        }

        var insertable = _tableInfo.OrderedColumns
            .Where(c => !c.IsNonInsertable && (!c.IsId || c.IsIdIsWritable))
            .ToList();

        TryAddWithLimit(_columnListCache, "Insertable", insertable);
        return insertable;
    }

    private IReadOnlyList<IColumnInfo> GetCachedUpdatableColumns()
    {
        if (_columnListCache.TryGetValue("Updatable", out var cached))
        {
            return cached;
        }

        var updatable = _tableInfo.OrderedColumns
            .Where(c => !(c.IsId || c.IsVersion || c.IsNonUpdateable || c.IsCreatedBy || c.IsCreatedOn))
            .ToList();

        TryAddWithLimit(_columnListCache, "Updatable", updatable);
        return updatable;
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

        var template = _cachedSqlTemplates.Value;

        // Always attempt to set audit fields if they exist - validation will happen inside SetAuditFields
        if (_hasAuditColumns)
        {
            SetAuditFields(objectToUpdate, true);
        }

        var (setClause, parameters) = BuildSetClause(objectToUpdate, original);
        if (setClause.Length == 0)
        {
            throw new InvalidOperationException("No changes detected for update.");
        }

        if (_versionColumn != null && _versionColumn.PropertyInfo.PropertyType != typeof(byte[]))
        {
            IncrementVersion(setClause);
        }

        var pId = ctx.CreateDbParameter(_idColumn.DbType,
            _idColumn.PropertyInfo.GetValue(objectToUpdate)!);
        parameters.Add(pId);

        var sql = string.Format(template.UpdateSql, setClause, MakeParameterName(pId));
        sc.Query.Append(sql);

        if (_versionColumn != null)
        {
            var versionValue = _versionColumn.MakeParameterValueFromField(objectToUpdate);
            var versionParam = AppendVersionCondition(sc, versionValue, ctx);
            if (versionParam != null)
            {
                parameters.Add(versionParam);
            }
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
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

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

        var ctx = context ?? _context;
        if (_idColumn == null && _tableInfo.PrimaryKeys.Count == 0)
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

        var keys = _tableInfo.PrimaryKeys;
        if (keys.Count > 0)
        {
            return keys;
        }

        throw new NotSupportedException("Upsert requires client-assigned Id or [PrimaryKey] attributes.");
    }

    private (string sql, List<DbParameter> parameters) BuildUpdateByKey(TEntity updated,
        IReadOnlyList<IColumnInfo> keyCols)
    {
        // Always attempt to set audit fields if they exist - validation will happen inside SetAuditFields
        if (_hasAuditColumns)
        {
            SetAuditFields(updated, true);
        }
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
        {
            return null;
        }

        return await RetrieveOneAsync((TRowID)idValue!);
    }

    private (StringBuilder clause, List<DbParameter> parameters) BuildSetClause(TEntity updated, TEntity? original)
    {
        var clause = new StringBuilder();
        var parameters = new List<DbParameter>();
        var template = _cachedSqlTemplates.Value;

        for (var i = 0; i < template.UpdateColumns.Count; i++)
        {
            var column = template.UpdateColumns[i];
            var newValue = column.MakeParameterValueFromField(updated);
            var originalValue = original != null ? column.MakeParameterValueFromField(original) : null;

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

    private DbParameter? AppendVersionCondition(ISqlContainer sc, object? versionValue, IDatabaseContext context)
    {
        if (versionValue == null)
        {
            sc.Query.Append(" AND ").Append(WrapObjectName(_versionColumn!.Name)).Append(" IS NULL");
            return null;
        }

        var pVersion = context.CreateDbParameter(_versionColumn!.DbType, versionValue);
        sc.Query.Append(" AND ").Append(WrapObjectName(_versionColumn.Name))
            .Append($" = {MakeParameterName(pVersion)}");
        return pVersion;
    }

    private void PrepareForInsertOrUpsert(TEntity e)
    {
        if (_auditValueResolver != null)
        {
            SetAuditFields(e, updateOnly: false);
        }
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
        var ctx = context ?? _context;
        PrepareForInsertOrUpsert(entity);

        var columns = new List<string>();
        var values = new List<string>();
        var parameters = new List<DbParameter>();

        foreach (var column in GetCachedInsertableColumns())
        {
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
        foreach (var column in GetCachedUpdatableColumns())
        {
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

        var keys = _tableInfo.PrimaryKeys;
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
        var ctx = context ?? _context;

        PrepareForInsertOrUpsert(entity);

        var columns = new List<string>();
        var values = new List<string>();
        var parameters = new List<DbParameter>();

        foreach (var column in GetCachedInsertableColumns())
        {
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
        foreach (var column in GetCachedUpdatableColumns())
        {
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

        var keys = _tableInfo.PrimaryKeys;
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
        var ctx = context ?? _context;

        PrepareForInsertOrUpsert(entity);

        var srcColumns = new List<string>();
        var values = new List<string>();
        var parameters = new List<DbParameter>();

        foreach (var column in _tableInfo.OrderedColumns)
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
        }

        var insertColumns = GetCachedInsertableColumns()
            .Select(c => WrapObjectName(c.Name))
            .ToList();

        var updateSet = new StringBuilder();
        foreach (var column in GetCachedUpdatableColumns())
        {
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

        var keys = _tableInfo.PrimaryKeys;
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

        var key = $"Where:{wrappedColumnName}:{list.Count}";
        var sql = GetCachedQuery(key, () =>
        {
            var names = new string[list.Count];
            for (var i = 0; i < names.Length; i++)
            {
                names[i] = _dialect.MakeParameterName($"p{i}");
            }

            TryAddWithLimit(_whereParameterNames, key, names);
            return string.Concat(wrappedColumnName, " IN (", string.Join(", ", names), ")");
        });

        AppendWherePrefix(sqlContainer);
        sqlContainer.Query.Append(sql);

        var dbType = _idColumn!.DbType;
        var names = _whereParameterNames[key];
        for (var i = 0; i < list.Count; i++)
        {
            var name = names[i];
            try
            {
                sqlContainer.SetParameterValue(name, list[i]);
            }
            catch (KeyNotFoundException)
            {
                var parameter = _context.CreateDbParameter(name, dbType, list[i]);
                sqlContainer.AddParameter(parameter);
            }
        }

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

   
        
        // Check if we have user-based audit fields (non-time fields)
        var hasUserAuditFields = _tableInfo.CreatedBy != null || _tableInfo.LastUpdatedBy != null;
        var hasTimeAuditFields = _tableInfo.CreatedOn != null || _tableInfo.LastUpdatedOn != null;
        var auditValues = _auditValueResolver?.Resolve();
        if (auditValues == null && hasUserAuditFields)
        {
            throw new InvalidOperationException("No AuditValues could be found by the resolver.");
        }
        // Use resolved time or UTC now for time fields
        var utcNow = auditValues?.UtcNow ?? DateTime.UtcNow;

        // Handle LastUpdated fields
        if (_tableInfo.LastUpdatedOn?.PropertyInfo != null)
        {
            _tableInfo.LastUpdatedOn.PropertyInfo.SetValue(obj, utcNow);
        }
        
        if (_tableInfo.LastUpdatedBy?.PropertyInfo != null && auditValues != null)
        {
            // We know auditValues is not null because we validated above
            _tableInfo.LastUpdatedBy.PropertyInfo.SetValue(obj, auditValues!.UserId);
        }
        else if (_tableInfo.LastUpdatedBy?.PropertyInfo != null)
        {
            var current = _tableInfo.LastUpdatedBy.PropertyInfo.GetValue(obj) as string;
            if (string.IsNullOrEmpty(current))
            {
                _tableInfo.LastUpdatedBy.PropertyInfo.SetValue(obj, "system");
            }
        }

        if (updateOnly)
        {
            return;
        }

        // Handle Created fields (only for new entities)
        if (_tableInfo.CreatedOn?.PropertyInfo != null)
        {
            var currentValue = _tableInfo.CreatedOn.PropertyInfo.GetValue(obj) as DateTime?;
            if (currentValue == null || currentValue == default(DateTime))
            {
                _tableInfo.CreatedOn.PropertyInfo.SetValue(obj, utcNow);
            }
        }

        if (_tableInfo.CreatedBy?.PropertyInfo != null)
        {
            var currentValue = _tableInfo.CreatedBy.PropertyInfo.GetValue(obj);
            if (currentValue == null
                || currentValue as string == string.Empty
                || Utils.IsZeroNumeric(currentValue)
                || (currentValue is Guid guid && guid == Guid.Empty))
            {
                // We know auditValues is not null because we validated above
                _tableInfo.CreatedBy.PropertyInfo.SetValue(obj, auditValues!.UserId);
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
        {
            return true;
        }

        var type = typeof(TRowID);
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(string))
        {
            return value as string == string.Empty;
        }

        if (underlying == typeof(Guid))
        {
            return value is Guid g && g == Guid.Empty;
        }

        if (Utils.IsZeroNumeric(value!))
        {
            return true;
        }

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

    private CachedSqlTemplates BuildCachedSqlTemplates()
    {
        var idCol = _tableInfo.Columns.Values.FirstOrDefault(c => c.IsId)
                     ?? throw new InvalidOperationException($"No ID column defined for {typeof(TEntity).Name}");

        var insertColumns = _tableInfo.Columns.Values
            .Where(c => !c.IsNonInsertable && (!c.IsId || c.IsIdIsWritable))
            .Where(c => _auditValueResolver != null || (!c.IsCreatedBy && !c.IsLastUpdatedBy))
            .Cast<IColumnInfo>()
            .ToList();

        var wrappedCols = new List<string>();
        for (var i = 0; i < insertColumns.Count; i++)
        {
            wrappedCols.Add(_dialect.WrapObjectName(insertColumns[i].Name));
        }

        var paramNames = new List<string>();
        var valuePlaceholders = new List<string>();
        for (var i = 0; i < insertColumns.Count; i++)
        {
            var name = $"p{i}";
            paramNames.Add(name);
            valuePlaceholders.Add(_dialect.MakeParameterName(name));
        }

        var insertSql =
            $"INSERT INTO {BuildWrappedTableName(_tableInfo)} ({string.Join(", ", wrappedCols)}) VALUES ({string.Join(", ", valuePlaceholders)})";
        var deleteSql =
            $"DELETE FROM {BuildWrappedTableName(_tableInfo)} WHERE {_dialect.WrapObjectName(idCol.Name)} = {{0}}";

        var updateColumns = _tableInfo.Columns.Values
            .Where(c => !c.IsId && !c.IsVersion && !c.IsNonUpdateable && !c.IsCreatedBy && !c.IsCreatedOn)
            .Cast<IColumnInfo>()
            .ToList();

        var updateSql =
            $"UPDATE {BuildWrappedTableName(_tableInfo)} SET {{0}} WHERE {_dialect.WrapObjectName(idCol.Name)} = {{1}}";

        return new CachedSqlTemplates
        {
            InsertSql = insertSql,
            InsertColumns = insertColumns,
            InsertParameterNames = paramNames,
            DeleteSql = deleteSql,
            UpdateSql = updateSql,
            UpdateColumns = updateColumns
        };

        string BuildWrappedTableName(ITableInfo info)
        {
            if (string.IsNullOrWhiteSpace(info.Schema))
            {
                return _dialect.WrapObjectName(info.Name);
            }

            var sb = new StringBuilder();
            sb.Append(_dialect.WrapObjectName(info.Schema));
            sb.Append(_dialect.CompositeIdentifierSeparator);
            sb.Append(_dialect.WrapObjectName(info.Name));
            return sb.ToString();
        }
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
        {
            throw new NotSupportedException(
                $"TRowID type '{type.FullName}' is not supported. Use string, Guid, or integer types.");
        }
    }
}
