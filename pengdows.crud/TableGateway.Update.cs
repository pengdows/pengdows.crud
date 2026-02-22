// =============================================================================
// FILE: TableGateway.Update.cs
// PURPOSE: UPDATE statement building and execution.
//
// AI SUMMARY:
// - BuildUpdateAsync() - Creates UPDATE statement with parameters.
// - UpdateAsync() - Executes UPDATE and returns rows affected.
// - Handles:
//   * Optimistic concurrency via [Version] column
//   * Audit field updates (LastUpdatedBy/On)
//   * Non-updateable columns (excluded from SET)
//   * Original value loading for concurrency checks
// - loadOriginal parameter: If true, loads current DB values for version check.
// - Version column behavior:
//   * SET version = version + 1
//   * WHERE version = @currentVersion
//   * Returns 0 if version mismatch (concurrent modification)
// - Requires [Id] column for WHERE clause.
// =============================================================================

using System.Data;
using System.Data.Common;
using System.Globalization;
using pengdows.crud.dialects;
using pengdows.crud.@internal;

namespace pengdows.crud;

/// <summary>
/// TableGateway partial: UPDATE statement building and execution.
/// </summary>
public partial class TableGateway<TEntity, TRowID>
{
    /// <inheritdoc/>
    public Task<ISqlContainer> BuildUpdateAsync(TEntity objectToUpdate, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = context ?? _context;
        return BuildUpdateAsync(objectToUpdate, _versionColumn != null, ctx, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ISqlContainer> BuildUpdateAsync(TEntity objectToUpdate, bool loadOriginal,
        IDatabaseContext? context = null, CancellationToken cancellationToken = default)
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
        var dialect = GetDialect(ctx);

        var original = loadOriginal
            ? await LoadOriginalAsync(objectToUpdate, ctx, cancellationToken).ConfigureAwait(false)
            : null;
        if (loadOriginal && original == null)
        {
            throw new InvalidOperationException("Original record not found for update.");
        }

        var template = GetTemplatesForDialect(dialect);

        if (_hasAuditColumns)
        {
            SetAuditFields(objectToUpdate, true);
        }

        var counters = new ClauseCounters();
        sc.Query.Append(template.UpdateSqlPrefix);
        var (columnsAdded, parameters) = BuildSetClause(objectToUpdate, original, dialect, ref counters, sc.Query);
        if (columnsAdded == 0)
        {
            throw new InvalidOperationException("No changes detected for update.");
        }

        // Append version increment directly from cached clause (no string alloc)
        if (template.VersionIncrementClause != null)
        {
            sc.Query.Append(template.VersionIncrementClause);
        }

        var idName = counters.NextKey();
        var pId = dialect.CreateDbParameter(idName, _idColumn.DbType,
            _idColumn.MakeParameterValueFromField(objectToUpdate));
        parameters.Add(pId);

        sc.Query.Append(template.UpdateSqlSuffix);
        if (dialect.SupportsNamedParameters) { sc.Query.Append(dialect.ParameterMarker); sc.Query.Append(idName); }
        else { sc.Query.Append('?'); }

        if (_versionColumn != null)
        {
            var versionValue = _versionColumn.MakeParameterValueFromField(objectToUpdate);
            var versionParam = AppendVersionCondition(sc, versionValue, dialect, ref counters);
            if (versionParam != null)
            {
                parameters.Add(versionParam);
            }
        }

        sc.AddParameters(parameters);
        return sc;
    }

    private Task<TEntity?> LoadOriginalAsync(TEntity objectToUpdate, IDatabaseContext? context = null)
    {
        return LoadOriginalAsync(objectToUpdate, context, CancellationToken.None);
    }

    private async Task<TEntity?> LoadOriginalAsync(TEntity objectToUpdate, IDatabaseContext? context,
        CancellationToken cancellationToken)
    {
        var ctx = context ?? _context;
        var idValue = _idColumn!.MakeParameterValueFromField(objectToUpdate);
        if (IsDefaultId(idValue))
        {
            return null;
        }

        try
        {
            if (idValue is TRowID typedId)
            {
                return await RetrieveOneAsync(typedId, ctx, cancellationToken).ConfigureAwait(false);
            }

            var targetType = typeof(TRowID);
            var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
            var converted = underlying switch
            {
                _ when underlying == typeof(Guid) => ConvertToGuid(idValue),
                _ => TypeCoercionHelper.ConvertWithCache(idValue!, underlying)
            };

            if (converted == null)
            {
                return null;
            }

            return await RetrieveOneAsync((TRowID)converted, ctx, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException)
        {
            throw new InvalidOperationException(
                $"Cannot convert ID value '{idValue}' of type {idValue!.GetType().Name} to {typeof(TRowID).Name}: {ex.Message}",
                ex);
        }
    }

    private static Guid ConvertToGuid(object? value)
    {
        return value switch
        {
            Guid guid => guid,
            string text => Guid.Parse(text),
            _ => Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)
        };
    }

    // Writes SET clause items directly into queryTarget (sc.Query) to avoid the
    // intermediate SbLite.ToString() + string.Format allocations.
    // Returns the count of columns written (0 = no changes detected).
    private (int columnsAdded, List<DbParameter> parameters) BuildSetClause(TEntity updated, TEntity? original,
        ISqlDialect dialect, ref ClauseCounters counters, ISqlQueryBuilder queryTarget)
    {
        var template = GetTemplatesForDialect(dialect);

        // Hoist dialect properties outside loop — constant per dialect, avoid N virtual calls
        var supportsNamed = dialect.SupportsNamedParameters;
        var paramMarker = dialect.ParameterMarker;

        // Pre-size parameters list based on updatable column count
        var parameters = new List<DbParameter>(template.UpdateColumns.Count);
        var columnsAdded = 0;

        for (var i = 0; i < template.UpdateColumns.Count; i++)
        {
            var column = template.UpdateColumns[i];
            var newValue = column.MakeParameterValueFromField(updated);
            var originalValue = original != null ? column.MakeParameterValueFromField(original) : null;

            if (original != null && ValuesAreEqual(newValue, originalValue, column.DbType))
            {
                continue;
            }

            if (columnsAdded > 0)
            {
                queryTarget.Append(SqlFragments.Comma);
            }

            columnsAdded++;

            if (Utils.IsNullOrDbNull(newValue))
            {
                queryTarget.Append(template.UpdateColumnWrappedNames[i]);
                queryTarget.Append(" = NULL");
            }
            else
            {
                var name = counters.NextSet();
                var param = dialect.CreateDbParameter(name, column.DbType, newValue);
                if (column.IsJsonType)
                {
                    dialect.TryMarkJsonParameter(param, column);
                }

                parameters.Add(param);

                queryTarget.Append(template.UpdateColumnWrappedNames[i]);
                queryTarget.Append(SqlFragments.EqualsOp);
                if (column.IsJsonType)
                {
                    // JSON columns need the full marker string for wrapping — rare path
                    queryTarget.Append(dialect.RenderJsonArgument(dialect.MakeParameterName(name), column));
                }
                else if (supportsNamed)
                {
                    // Direct append — eliminates MakeParameterName's Replace+Concat per column
                    queryTarget.Append(paramMarker);
                    queryTarget.Append(name);
                }
                else
                {
                    queryTarget.Append('?');
                }
            }
        }

        return (columnsAdded, parameters);
    }

    internal static bool ValuesAreEqual(object? newValue, object? originalValue, DbType dbType)
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
                return decimal.Compare(Convert.ToDecimal(newValue, CultureInfo.InvariantCulture),
                    Convert.ToDecimal(originalValue, CultureInfo.InvariantCulture)) == 0;
            case DbType.DateTime:
            case DbType.DateTime2:
                return NormalizeDateTime(Convert.ToDateTime(newValue, CultureInfo.InvariantCulture)) ==
                       NormalizeDateTime(Convert.ToDateTime(originalValue, CultureInfo.InvariantCulture));
            case DbType.DateTimeOffset:
                return NormalizeDateTimeOffset(newValue).UtcDateTime ==
                       NormalizeDateTimeOffset(originalValue).UtcDateTime;
            default:
                return Equals(newValue, originalValue);
        }
    }

    private static DateTime NormalizeDateTime(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    internal static DateTimeOffset NormalizeDateTimeOffset(object value)
    {
        switch (value)
        {
            case DateTimeOffset dto:
                return dto;
            case DateTime dt:
                return dt.Kind switch
                {
                    DateTimeKind.Utc => new DateTimeOffset(dt, TimeSpan.Zero),
                    DateTimeKind.Local => new DateTimeOffset(dt),
                    _ => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero)
                };
            case string s:
                return NormalizeStringDateTimeOffset(s);
            default:
                var converted = Convert.ToDateTime(value, CultureInfo.InvariantCulture);
                return new DateTimeOffset(NormalizeDateTime(converted), TimeSpan.Zero);
        }
    }

    private static DateTimeOffset NormalizeStringDateTimeOffset(string raw)
    {
        var value = raw.Trim();
        if (value.Length == 0)
        {
            throw new FormatException("Value cannot be empty.");
        }

        if (HasExplicitOffset(value))
        {
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                    out var parsedWithOffset))
            {
                return parsedWithOffset;
            }
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                out var parsedDateTime))
        {
            return new DateTimeOffset(NormalizeDateTime(parsedDateTime), TimeSpan.Zero);
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                out var fallback))
        {
            return fallback;
        }

        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static bool HasExplicitOffset(string value)
    {
        if (value.IndexOf('Z', StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        var separatorIndex = value.LastIndexOf('T');
        if (separatorIndex < 0)
        {
            separatorIndex = value.LastIndexOf('t');
        }

        if (separatorIndex < 0)
        {
            separatorIndex = value.LastIndexOf(' ');
        }

        if (separatorIndex < 0)
        {
            return false;
        }

        for (var i = separatorIndex + 1; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '+' || c == '-')
            {
                return true;
            }
        }

        return false;
    }

    // String-returning overload used by BuildUpdateByKey (upsert-by-key path in Core.cs).
    // Uses SbLite to avoid heap allocation for the intermediate SET clause string.
    private (string clause, List<DbParameter> parameters) BuildSetClause(TEntity updated, TEntity? original,
        ISqlDialect dialect, ref ClauseCounters counters)
    {
        var clause = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        var template = GetTemplatesForDialect(dialect);

        // Hoist dialect properties outside loop — constant per dialect, avoid N virtual calls
        var supportsNamed = dialect.SupportsNamedParameters;
        var paramMarker = dialect.ParameterMarker;

        var parameters = new List<DbParameter>(template.UpdateColumns.Count);

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
                clause.Append(SqlFragments.Comma);
            }

            if (Utils.IsNullOrDbNull(newValue))
            {
                clause.Append(template.UpdateColumnWrappedNames[i]);
                clause.Append(" = NULL");
            }
            else
            {
                var name = counters.NextSet();
                var param = dialect.CreateDbParameter(name, column.DbType, newValue);
                if (column.IsJsonType)
                {
                    dialect.TryMarkJsonParameter(param, column);
                }

                parameters.Add(param);

                clause.Append(template.UpdateColumnWrappedNames[i]);
                clause.Append(SqlFragments.EqualsOp);
                if (column.IsJsonType)
                {
                    clause.Append(dialect.RenderJsonArgument(dialect.MakeParameterName(name), column));
                }
                else if (supportsNamed)
                {
                    // Direct append — eliminates MakeParameterName's Replace+Concat per column
                    clause.Append(paramMarker);
                    clause.Append(name);
                }
                else
                {
                    clause.Append('?');
                }
            }
        }

        return (clause.ToString(), parameters);
    }

    // Used by BuildUpdateByKey (upsert-by-key path in Core.cs).
    private string GetVersionIncrementClause(ISqlDialect dialect)
    {
        return $", {dialect.WrapObjectName(_versionColumn!.Name)} = {dialect.WrapObjectName(_versionColumn.Name)} + 1";
    }

    private DbParameter? AppendVersionCondition(ISqlContainer sc, object? versionValue, ISqlDialect dialect,
        ref ClauseCounters counters)
    {
        if (versionValue == null)
        {
            sc.Query.Append(SqlFragments.And).Append(sc.WrapObjectName(_versionColumn!.Name)).Append(" IS NULL");
            return null;
        }

        var name = counters.NextVer();
        var pVersion = dialect.CreateDbParameter(name, _versionColumn!.DbType, versionValue);
        sc.Query.Append(SqlFragments.And).Append(sc.WrapObjectName(_versionColumn.Name)).Append(" = ");
        if (dialect.SupportsNamedParameters) { sc.Query.Append(dialect.ParameterMarker); sc.Query.Append(name); }
        else { sc.Query.Append('?'); }
        return pVersion;
    }
}
