// =============================================================================
// FILE: ColumnReadRules.cs
// PURPOSE: The primary decisions for reading one column into one property, shared by both mappers.
//
// AI SUMMARY:
// - TryBuild tries, in order: the dialect's rule for a column declared with a native type (NativeColumnRead), the
//   statement reads (one GetValue that is also the NULL check), and the quirk reads (a provider's own value type).
// - It returns the assignment to the property and whether that assignment already checks NULL. A caller adds its own
//   NULL guard only when it does not, so the two mappers cannot disagree about NULL.
// - The mappers' remaining decisions (enum, stream, the general conversions) stay in each mapper.
// =============================================================================

using System.Data;
using System.Linq.Expressions;
using pengdows.crud.dialects;

namespace pengdows.crud.@internal;

internal static class ColumnReadRules
{
    /// <summary>
    /// The assignment of the column to <paramref name="property"/> by a shared rule, or null when no shared rule covers it.
    /// <paramref name="selfGuarded"/> is true when the assignment checks for NULL itself (the caller adds no guard).
    /// </summary>
    public static Expression? TryBuild(ParameterExpression readerParam, int ordinal, Type fieldType, Type targetType,
        TypeCoercionOptions? options, bool npgsqlTyped, string? nativeType, Expression property, out bool selfGuarded)
    {
        selfGuarded = false;

        if (nativeType is { } declared && options is not null &&
            DatabaseTraits.For(options.Provider).NativeColumnRead is { } nativeRule &&
            nativeRule(readerParam, Expression.Constant(ordinal), declared, targetType, options) is { } nativeRead)
        {
            return Expression.Assign(property, nativeRead);
        }

        if (ColumnStatementReads.TryBuild(readerParam, ordinal, fieldType, targetType, options, property) is { } statement)
        {
            // Its one GetValue is also the NULL check.
            selfGuarded = true;
            return statement;
        }

        if (ColumnQuirkReads.TryBuild(Expression.Convert(readerParam, typeof(IDataRecord)), ordinal, fieldType, targetType,
                options, npgsqlTyped) is { } quirk)
        {
            return Expression.Assign(property, quirk);
        }

        return null;
    }
}
