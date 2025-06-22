#region

using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
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
    private readonly IAuditValueResolver? _auditValueResolver;
    private readonly IDatabaseContext _context;

    private readonly IColumnInfo? _idColumn;

    // private readonly IServiceProvider _serviceProvider;
    private readonly ITableInfo _tableInfo;
    private readonly Type? _userFieldType = null;

    private readonly IColumnInfo? _versionColumn;

    public EntityHelper(IDatabaseContext databaseContext,
        EnumParseFailureMode enumParseBehavior = EnumParseFailureMode.Throw
    )
    {
        _auditValueResolver = null;
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
        EnumParseBehavior = enumParseBehavior;
    }

    [Obsolete("Use the constructor without IServiceProvider instead")]
    public EntityHelper(IDatabaseContext databaseContext,
        IAuditValueResolver auditValueResolver,
        EnumParseFailureMode enumParseBehavior = EnumParseFailureMode.Throw
    )
    {
        _auditValueResolver = auditValueResolver;
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
        EnumParseBehavior = enumParseBehavior;
    }

    public string WrappedTableName { get; }

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
        foreach (var column in _tableInfo.Columns.Values)
        {
            if (column.IsId && !column.IsIdIsWritable) continue;

            var value = column.MakeParameterValueFromField(objectToCreate);

            // If no audit resolver is provided and the value is null for an audit column,
            // skip including this column so database defaults will apply.
            if (_auditValueResolver == null &&
                (column.IsCreatedBy || column.IsCreatedOn || column.IsLastUpdatedBy || column.IsLastUpdatedOn) &&
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
        var id = (TRowID)_idColumn.PropertyInfo.GetValue(objectToRetrieve);
        var list = new List<TRowID>() { id };
        var sc = BuildRetrieve(list, null, ctx);
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
        if (Utils.IsNullOrEmpty(listOfObjects) || sc == null)
            throw new ArgumentException("List of objects cannot be null or empty.");

        var listOfPrimaryKeys = _tableInfo.Columns.Values.Where(o => o.IsPrimaryKey).ToList();
        if (listOfPrimaryKeys.Count < 1) throw new Exception($"No primary keys found for type {typeof(TEntity).Name}");

        // Calculate total parameter count to avoid exceeding DB limits
        var pc = sc.ParameterCount;
        var numberOfParametersToBeAdded = listOfObjects?.Count * listOfPrimaryKeys.Count;
        if ((pc + numberOfParametersToBeAdded) > _context.MaxParameterLimit)
            throw new TooManyParametersException("Too many parameters", _context.MaxParameterLimit);

        var sb = new StringBuilder();
        var pp = new List<DbParameter>();

        // Wrap alias if provided
        var wrappedAlias = string.IsNullOrWhiteSpace(alias)
            ? ""
            : WrapObjectName(alias) + _context.CompositeIdentifierSeparator;

        // Construct WHERE clause as series of (pk1 = val AND pk2 = val) OR (...)...
        var i = 0;
        foreach (var entity in listOfObjects)
        {
            if (i > 0) sb.Append(" OR ");

            sb.Append("(");

            for (var j = 0; j < listOfPrimaryKeys.Count; j++)
            {
                if (j > 0) sb.Append(" AND ");

                var pk = listOfPrimaryKeys[j];
                var value = pk.MakeParameterValueFromField(entity);

                // Create parameter with unique and valid name auto-generated by context
                var parameter = _context.CreateDbParameter(pk.DbType, value);

                sb.Append(wrappedAlias);
                sb.Append(WrapObjectName(pk.Name));

                if (Utils.IsNullOrDbNull(value))
                {
                    sb.Append(" IS NULL");
                }
                else
                {
                    sb.Append(" = ");
                    sb.Append(MakeParameterName(parameter));
                    pp.Add(parameter);
                }
            }

            sb.Append(")");
            i++;
        }

        if (sb.Length < 1) return;

        // Add all generated parameters to the container
        sc.AddParameters(pp);

        // Determine how to append WHERE/AND clause
        var query = sc.Query.ToString();
        if (!query.Contains("WHERE ", StringComparison.OrdinalIgnoreCase))
            sc.Query.Append("\n WHERE ");
        else
            sc.Query.Append("\n AND ");

        // Final WHERE clause with grouped filters
        sc.Query.Append(sb);
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
        var setClause = new StringBuilder();
        var parameters = new List<DbParameter>();
        SetAuditFields(objectToUpdate, true);
        var sc = context.CreateSqlContainer();
        var original = null as TEntity;

        if (loadOriginal)
        {
            original = await RetrieveOneAsync(objectToUpdate);
            if (original == null)
                throw new InvalidOperationException("Original record not found for update.");
        }

        foreach (var column in _tableInfo.Columns.Values)
        {
            if (column.IsId
                || column.IsVersion || column.IsNonUpdateable
                || column.IsCreatedBy
                || column.IsCreatedOn)
                //Skip columns that should never be directly updated
                //the version column, rowId and columns we have marked 
                //as non-updateable, also of course we should NEVER update "created" columns
                continue;

            var newValue = column.MakeParameterValueFromField(objectToUpdate);
            var originalValue = loadOriginal ? column.MakeParameterValueFromField(original) : null;

            if (loadOriginal && Equals(newValue, originalValue))
                // Skip unchanged values if original is loaded.
                continue;

            if (setClause.Length > 0) setClause.Append(", ");

            if (newValue == null)
            {
                setClause.Append($"{WrapObjectName(column.Name)} = NULL");
            }
            else
            {
                var param = context.CreateDbParameter(column.DbType, newValue);
                parameters.Add(param);
                setClause.Append($"{WrapObjectName(column.Name)} = {MakeParameterName(param)}");
            }
        }

        if (_versionColumn != null)
            //this should be updated to wrap other patterns.
            setClause.Append(
                $", {WrapObjectName(_versionColumn.Name)} = {WrapObjectName(_versionColumn.Name)} + 1");

        if (setClause.Length == 0)
            throw new InvalidOperationException("No changes detected for update.");

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
            if (versionValue == null)
            {
                sc.Query.Append(" AND ").Append(WrapObjectName(_versionColumn.Name)).Append(" IS NULL");
            }
            else
            {
                var pVersion = context.CreateDbParameter(_versionColumn.DbType, versionValue);
                sc.Query.Append(" AND ").Append(WrapObjectName(_versionColumn.Name))
                    .Append($" = {MakeParameterName(pVersion)}");
                parameters.Add(pVersion);
            }
        }

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
        if (_userFieldType == null || obj == null || _auditValueResolver == null)
            return;

        var auditValues = _auditValueResolver.Resolve();
        if (auditValues == null) return;

        // Always update last-modified
        _tableInfo.LastUpdatedBy?.PropertyInfo?.SetValue(obj, auditValues.UserId);
        _tableInfo.LastUpdatedOn?.PropertyInfo?.SetValue(obj, auditValues.UtcNow);

        if (updateOnly) return;

        // Only set Created fields if they are null or default
        if (_tableInfo.CreatedBy?.PropertyInfo != null)
        {
            var currentValue = _tableInfo.CreatedBy.PropertyInfo.GetValue(obj);
            if (currentValue == null
                || currentValue as string == string.Empty
                || Utils.IsZeroNumeric(currentValue)
                || (currentValue is Guid guid && guid == Guid.Empty))
                _tableInfo.CreatedBy.PropertyInfo.SetValue(obj, auditValues.UserId);
        }

        if (_tableInfo.CreatedOn?.PropertyInfo != null)
        {
            var currentValue = _tableInfo.CreatedOn.PropertyInfo.GetValue(obj) as DateTime?;
            if (currentValue == null || currentValue == default(DateTime))
                _tableInfo.CreatedOn.PropertyInfo.SetValue(obj, auditValues.UtcNow);
        }
    }

    private string WrapObjectName(string objectName)
    {
        return _context.WrapObjectName(objectName);
    }
}