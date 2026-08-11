using System.Linq.Expressions;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;

namespace pengdows.crud.dialects;

/// <summary>
/// Expression-based column name resolution helpers for <see cref="ISqlDialect"/>.
/// </summary>
internal static class SqlDialectExtensions
{
    /// <summary>
    /// Returns the mapped database column name for the property identified by
    /// <paramref name="expression"/>.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <typeparam name="TValue">The property's CLR type.</typeparam>
    /// <param name="dialect">The active SQL dialect.</param>
    /// <param name="registry">The type-map registry for the owning context.</param>
    /// <param name="expression">A direct property-access lambda: <c>x => x.PropertyName</c>.</param>
    /// <returns>The database column name as declared in <c>[Column("name", ...)]</c>.</returns>
    /// <exception cref="ArgumentNullException">Any parameter is null.</exception>
    /// <exception cref="ArgumentException">The expression is not a direct property access.</exception>
    /// <exception cref="SqlGenerationException">The property has no <c>[Column]</c> mapping.</exception>
    public static string ColumnName<T, TValue>(
        this ISqlDialect dialect,
        ITypeMapRegistry registry,
        Expression<Func<T, TValue>> expression)
    {
        if (dialect is null) throw new ArgumentNullException(nameof(dialect));
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        if (expression is null) throw new ArgumentNullException(nameof(expression));

        var prop = ExpressionMemberExtractor.ExtractProperty(expression);
        var tableInfo = registry.GetTableInfo<T>();

        if (!tableInfo.ColumnsByPropertyName.TryGetValue(prop.Name, out var column))
        {
            throw new SqlGenerationException(
                $"Property '{typeof(T).Name}.{prop.Name}' has no [Column] mapping. " +
                $"Ensure the property is decorated with [Column(\"column_name\", DbType.xxx)].",
                SupportedDatabase.Unknown);
        }

        return column.Name;
    }

    /// <summary>
    /// Returns the mapped database column name for the property identified by
    /// <paramref name="expression"/>, using an <c>object?</c>-typed lambda.
    /// </summary>
    /// <remarks>
    /// Value-type properties (int, bool, Guid, etc.) are boxed into a Convert node
    /// by the compiler when the lambda return type is <c>object?</c>.
    /// <see cref="ExpressionMemberExtractor"/> strips this node transparently.
    /// </remarks>
    public static string ColumnName<T>(
        this ISqlDialect dialect,
        ITypeMapRegistry registry,
        Expression<Func<T, object?>> expression)
        => ColumnName<T, object?>(dialect, registry, expression);

    /// <summary>
    /// Returns the dialect-quoted database column name for the property identified by
    /// <paramref name="expression"/>.
    /// </summary>
    public static string WrappedColumnName<T, TValue>(
        this ISqlDialect dialect,
        ITypeMapRegistry registry,
        Expression<Func<T, TValue>> expression)
        => dialect.WrapObjectName(ColumnName(dialect, registry, expression));

    /// <summary>
    /// Returns the dialect-quoted database column name for the property identified by
    /// <paramref name="expression"/>, using an <c>object?</c>-typed lambda.
    /// </summary>
    public static string WrappedColumnName<T>(
        this ISqlDialect dialect,
        ITypeMapRegistry registry,
        Expression<Func<T, object?>> expression)
        => dialect.WrapObjectName(ColumnName(dialect, registry, expression));
}
