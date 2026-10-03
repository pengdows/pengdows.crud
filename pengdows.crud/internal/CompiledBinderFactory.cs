using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using pengdows.crud.dialects;

namespace pengdows.crud.@internal;

/// <summary>
/// Factory that compiles monolithic, unrolled parameter binders for entities.
/// Eliminates per-column loops and FastGetter overhead.
/// </summary>
internal static class CompiledBinderFactory<TEntity> where TEntity : class, new()
{
    public delegate int Binder(TEntity entity, IList<DbParameter> parameters);

    public static Binder CreateInsertBinder(
        IReadOnlyList<IColumnInfo> columns,
        IReadOnlyList<string> paramNames,
        ISqlDialect dialect)
    {
        var entityParam = Expression.Parameter(typeof(TEntity), "entity");
        var listParam = Expression.Parameter(typeof(IList<DbParameter>), "parameters");
        var countVar = Expression.Variable(typeof(int), "count");

        var expressions = new List<Expression> { Expression.Assign(countVar, Expression.Constant(0)) };

        var createDbParamMethod = ResolveGenericCreateDbParameter();
        var createDbParamGeneric = createDbParamMethod.MakeGenericMethod(typeof(object));
        var listAddMethod = typeof(ICollection<DbParameter>).GetMethod(nameof(ICollection<DbParameter>.Add))!;

        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var paramName = paramNames[i];
            var property = column.PropertyInfo;

            // Extract value from entity
            var propertyAccess = Expression.Property(entityParam, property);
            var boxedValue = Expression.Convert(propertyAccess, typeof(object));

            Expression finalValueExpr = boxedValue;

            if (column.IsJsonType)
            {
                finalValueExpr = Expression.Call(
                    typeof(CompiledBinderFactory<TEntity>).GetMethod(nameof(SerializeJsonValue),
                        BindingFlags.NonPublic | BindingFlags.Static)!,
                    boxedValue, Expression.Constant(column.JsonSerializerOptions));
            }
            else if (column.IsEnum)
            {
                if (column.EnumAsString)
                {
                    var toStringMethod = typeof(object).GetMethod(nameof(object.ToString))!;
                    finalValueExpr = Expression.Call(boxedValue, toStringMethod);
                }
                else
                {
                    finalValueExpr = Expression.Convert(propertyAccess, column.EnumUnderlyingType!);
                    finalValueExpr = Expression.Convert(finalValueExpr, typeof(object));
                }
            }

            // Create DbParameter: dialect.CreateDbParameter(name, dbType, value)
            var createParamCall = Expression.Call(
                Expression.Constant(dialect),
                createDbParamGeneric,
                Expression.Constant(paramName),
                Expression.Constant(column.DbType),
                finalValueExpr);

            // Add to list: parameters.Add(param)
            expressions.Add(Expression.Call(listParam, listAddMethod, MarkJson(createParamCall, column, dialect)));
            expressions.Add(Expression.PostIncrementAssign(countVar));
        }

        expressions.Add(countVar);

        var block = Expression.Block(new[] { countVar }, expressions);
        return Expression.Lambda<Binder>(block, entityParam, listParam).Compile();
    }

    public delegate int UpdateBinder(TEntity updated, TEntity? original, IList<DbParameter> parameters);

    public static UpdateBinder CreateUpdateBinder(
        IReadOnlyList<IColumnInfo> columns,
        IReadOnlyList<string> paramNames,
        ISqlDialect dialect)
    {
        var updatedParam = Expression.Parameter(typeof(TEntity), "updated");
        var originalParam = Expression.Parameter(typeof(TEntity), "original");
        var listParam = Expression.Parameter(typeof(IList<DbParameter>), "parameters");
        var countVar = Expression.Variable(typeof(int), "count");

        var expressions = new List<Expression> { Expression.Assign(countVar, Expression.Constant(0)) };

        var createDbParamMethod = ResolveGenericCreateDbParameter();
        var createDbParamGeneric = createDbParamMethod.MakeGenericMethod(typeof(object));
        var listAddMethod = typeof(ICollection<DbParameter>).GetMethod(nameof(ICollection<DbParameter>.Add))!;

        var valuesAreEqualMethod = typeof(TableGateway<TEntity, int>).GetMethod(
            "ValuesAreEqual",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var paramName = paramNames[i];
            var property = column.PropertyInfo;

            // Extract values
            var updatedVal = Expression.Property(updatedParam, property);
            var boxedUpdated = Expression.Convert(updatedVal, typeof(object));

            Expression finalValueExpr = boxedUpdated;
            if (column.IsJsonType)
            {
                finalValueExpr = Expression.Call(
                    typeof(CompiledBinderFactory<TEntity>).GetMethod(nameof(SerializeJsonValue),
                        BindingFlags.NonPublic | BindingFlags.Static)!,
                    boxedUpdated, Expression.Constant(column.JsonSerializerOptions));
            }
            else if (column.IsEnum)
            {
                if (column.EnumAsString)
                {
                    var toStringMethod = typeof(object).GetMethod(nameof(object.ToString))!;
                    finalValueExpr = Expression.Call(boxedUpdated, toStringMethod);
                }
                else
                {
                    finalValueExpr = Expression.Convert(updatedVal, column.EnumUnderlyingType!);
                    finalValueExpr = Expression.Convert(finalValueExpr, typeof(object));
                }
            }

            // Dirty check: if (original == null || !ValuesAreEqual(updatedVal, originalVal, dbType))
            var originalVal = Expression.Property(originalParam, property);
            var boxedOriginal = Expression.Convert(originalVal, typeof(object));

            var checkCall = Expression.Call(valuesAreEqualMethod, boxedUpdated, boxedOriginal, Expression.Constant(column.DbType));
            var isDirty = Expression.OrElse(
                Expression.Equal(originalParam, Expression.Constant(null, typeof(TEntity))),
                Expression.Not(checkCall));

            var createParamCall = Expression.Call(
                Expression.Constant(dialect),
                createDbParamGeneric,
                Expression.Constant(paramName),
                Expression.Constant(column.DbType),
                finalValueExpr);

            var thenBlock = Expression.Block(
                Expression.Call(listParam, listAddMethod, MarkJson(createParamCall, column, dialect)),
                Expression.PostIncrementAssign(countVar)
            );

            expressions.Add(Expression.IfThen(isDirty, thenBlock));
        }

        expressions.Add(countVar);

        var block = Expression.Block(new[] { countVar }, expressions);
        return Expression.Lambda<UpdateBinder>(block, updatedParam, originalParam, listParam).Compile();
    }

    // A JSON or enum column's parameter gets the same dialect marking the uncompiled paths apply
    // (MarkColumnParameter): JSON's text DbType and Npgsql jsonb metadata, an untyped enum on PostgreSQL.
    private static Expression MarkJson(Expression createParamCall, IColumnInfo column, ISqlDialect dialect)
    {
        if (!column.IsJsonType && !column.IsEnum)
        {
            return createParamCall;
        }

        return Expression.Call(
            typeof(CompiledBinderFactory<TEntity>).GetMethod(nameof(MarkJsonParameter),
                BindingFlags.NonPublic | BindingFlags.Static)!,
            Expression.Constant(dialect, typeof(ISqlDialect)), createParamCall,
            Expression.Constant(column, typeof(IColumnInfo)));
    }

    private static DbParameter MarkJsonParameter(ISqlDialect dialect, DbParameter parameter, IColumnInfo column)
    {
        dialect.MarkColumnParameter(parameter, column);
        return parameter;
    }

    private static MethodInfo ResolveGenericCreateDbParameter()
    {
        var methods = typeof(ISqlDialect).GetMethods();
        foreach (var m in methods)
        {
            if (m.Name == nameof(ISqlDialect.CreateDbParameter) &&
                m.IsGenericMethod &&
                m.GetParameters().Length == 3)
            {
                return m;
            }
        }
        throw new InvalidOperationException("Could not find generic CreateDbParameter method on ISqlDialect");
    }

    // JSON-shaped values are written as the JSON they hold: System.Text.Json serializes a
    // JsonValue struct's (nonexistent) public properties as "{}", and a JsonDocument as its
    // properties too. default(JsonElement) and default(JsonValue) hold nothing and are SQL NULL.
    private static string? SerializeJsonValue(object? value, JsonSerializerOptions options)
    {
        // A null reference is SQL NULL, and a [Json] string is JSON-encoded like any other value:
        // both as ColumnInfo writes them for batch and primary-key paths, and as the read side
        // deserializes them (REV-047).
        return value switch
        {
            null => null,
            JsonElement { ValueKind: JsonValueKind.Undefined } => null,
            types.valueobjects.JsonValue { IsDefault: true } => null,
            JsonDocument document => document.RootElement.GetRawText(),
            JsonElement element => element.GetRawText(),
            types.valueobjects.JsonValue jsonValue => jsonValue.AsString(),
            _ => JsonSerializer.Serialize(value, options)
        };
    }
}
