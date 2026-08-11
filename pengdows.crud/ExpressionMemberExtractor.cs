using System.Linq.Expressions;
using System.Reflection;

namespace pengdows.crud;

/// <summary>
/// Extracts a <see cref="PropertyInfo"/> from a simple property-access lambda expression.
/// </summary>
internal static class ExpressionMemberExtractor
{
    /// <summary>
    /// Extracts the <see cref="PropertyInfo"/> from a direct property-access expression.
    /// </summary>
    /// <typeparam name="T">The entity type that declares the property.</typeparam>
    /// <typeparam name="TValue">The property's return type.</typeparam>
    /// <param name="expression">
    /// A lambda of the form <c>x => x.PropertyName</c>.
    /// Value-type properties compiled with a boxing <c>Convert</c> node are handled transparently.
    /// </param>
    /// <returns>The <see cref="PropertyInfo"/> for the accessed property.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The expression is not a direct property access, or the property is not declared on
    /// <typeparamref name="T"/> or one of its base types.
    /// </exception>
    public static PropertyInfo ExtractProperty<T, TValue>(Expression<Func<T, TValue>> expression)
    {
        if (expression is null)
            throw new ArgumentNullException(nameof(expression));

        var body = expression.Body;

        // The C# compiler inserts Convert(x.Property, object) when the lambda is typed as
        // Func<T, object?> but the property is a value type (int, bool, Guid, etc.).
        // Strip it so the check below always sees the underlying MemberExpression.
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } unary)
            body = unary.Operand;

        if (body is MemberExpression { Member: PropertyInfo prop })
        {
            if (prop.DeclaringType is null || !prop.DeclaringType.IsAssignableFrom(typeof(T)))
            {
                throw new ArgumentException(
                    $"Property '{prop.Name}' is not declared on '{typeof(T).Name}' or one of its base types. " +
                    $"Declaring type: '{prop.DeclaringType?.Name ?? "unknown"}'.",
                    nameof(expression));
            }

            return prop;
        }

        var nodeDescription = expression.Body.NodeType switch
        {
            ExpressionType.Call => "method call",
            ExpressionType.New => "constructor call",
            ExpressionType.NewArrayInit or ExpressionType.NewArrayBounds => "array creation",
            ExpressionType.MemberInit => "member initializer (anonymous type or projection)",
            ExpressionType.Conditional => "conditional expression",
            ExpressionType.Coalesce => "null-coalescing expression",
            ExpressionType.MemberAccess => "nested member access (navigation property)",
            _ => $"unsupported expression ({expression.Body.NodeType})"
        };

        throw new ArgumentException(
            $"Expression must be a direct property access (x => x.PropertyName). " +
            $"Got a {nodeDescription}. " +
            $"Computed expressions, method calls, navigation properties, and projections are not supported.",
            nameof(expression));
    }
}
