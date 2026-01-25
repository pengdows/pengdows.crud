using System;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using pengdows.crud.dialects;
using pengdows.crud.@internal;

namespace pengdows.crud;

public partial class EntityHelper<TEntity, TRowID>
{
    public Task<ISqlContainer> BuildUpdateAsync(TEntity objectToUpdate, IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;
        return BuildUpdateAsync(objectToUpdate, _versionColumn != null, ctx, CancellationToken.None);
    }

    public Task<ISqlContainer> BuildUpdateAsync(TEntity objectToUpdate, IDatabaseContext? context,
        CancellationToken cancellationToken)
    {
        var ctx = context ?? _context;
        return BuildUpdateAsync(objectToUpdate, _versionColumn != null, ctx, cancellationToken);
    }

    public Task<ISqlContainer> BuildUpdateAsync(TEntity objectToUpdate, bool loadOriginal,
        IDatabaseContext? context = null)
    {
        return BuildUpdateAsync(objectToUpdate, loadOriginal, context, CancellationToken.None);
    }

    public async Task<ISqlContainer> BuildUpdateAsync(TEntity objectToUpdate, bool loadOriginal,
        IDatabaseContext? context, CancellationToken cancellationToken)
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
        var (setClause, parameters) = BuildSetClause(objectToUpdate, original, dialect, counters);
        if (setClause.Length == 0)
        {
            throw new InvalidOperationException("No changes detected for update.");
        }

        if (_versionColumn != null && _versionColumn.PropertyInfo.PropertyType != typeof(byte[]))
        {
            IncrementVersion(setClause, dialect);
        }

        var idName = counters.NextKey();
        var pId = dialect.CreateDbParameter(idName, _idColumn.DbType,
            _idColumn.PropertyInfo.GetValue(objectToUpdate)!);
        parameters.Add(pId);

        var sql = string.Format(template.UpdateSql, setClause, dialect.MakeParameterName(pId));
        sc.Query.Append(sql);

        if (_versionColumn != null)
        {
            var versionValue = _versionColumn.MakeParameterValueFromField(objectToUpdate);
            var versionParam = AppendVersionCondition(sc, versionValue, dialect, counters);
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
        var idValue = _idColumn!.PropertyInfo.GetValue(objectToUpdate);
        if (IsDefaultId(idValue))
        {
            return null;
        }

        var targetType = typeof(TRowID);
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        try
        {
            var converted = Convert.ChangeType(idValue!, underlying, CultureInfo.InvariantCulture);
            if (converted == null)
            {
                return null;
            }

            return await RetrieveOneAsync((TRowID)converted, ctx, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidCastException ex)
        {
            throw new InvalidOperationException(
                $"Cannot convert ID value '{idValue}' of type {idValue!.GetType().Name} to {targetType.Name}: {ex.Message}",
                ex);
        }
        catch (DbException)
        {
            return null;
        }
    }

    private (StringBuilder clause, List<DbParameter> parameters) BuildSetClause(TEntity updated, TEntity? original,
        ISqlDialect dialect, ClauseCounters counters)
    {
        var clause = new StringBuilder();
        var parameters = new List<DbParameter>();
        var template = GetTemplatesForDialect(dialect);

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
                clause.Append($"{dialect.WrapObjectName(column.Name)} = NULL");
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
                var marker = dialect.MakeParameterName(param);
                if (column.IsJsonType)
                {
                    marker = dialect.RenderJsonArgument(marker, column);
                }

                clause.Append($"{dialect.WrapObjectName(column.Name)} = {marker}");
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

    private static DateTimeOffset NormalizeDateTimeOffset(object value)
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

    private void IncrementVersion(StringBuilder setClause, ISqlDialect dialect)
    {
        setClause.Append(
            $", {dialect.WrapObjectName(_versionColumn!.Name)} = {dialect.WrapObjectName(_versionColumn.Name)} + 1");
    }

    private DbParameter? AppendVersionCondition(ISqlContainer sc, object? versionValue, ISqlDialect dialect,
        ClauseCounters counters)
    {
        if (versionValue == null)
        {
            sc.Query.Append(" AND ").Append(sc.WrapObjectName(_versionColumn!.Name)).Append(" IS NULL");
            return null;
        }

        var name = counters.NextVer();
        var pVersion = dialect.CreateDbParameter(name, _versionColumn!.DbType, versionValue);
        sc.Query.Append(" AND ").Append(sc.WrapObjectName(_versionColumn.Name))
            .Append($" = {dialect.MakeParameterName(pVersion)}");
        return pVersion;
    }
}