using System.Linq.Expressions;

namespace pengdows.crud.@internal;

/// <summary>
/// The expression both compiled mappers (the gateway's and DataReaderMapper's) read a numeric column
/// with into a numeric, enum or bool property, so the two can't drift apart again (DRY-010). Checked:
/// a value the property can't hold throws OverflowException instead of wrapping (TYPE-008, REV-046); a
/// fractional value into an integer must be whole (COR-009); into a bool non-zero is true and NaN
/// fails (NumericTruth, DRY-003). Nothing is boxed.
/// </summary>
internal static class NumericExpressions
{
    public static bool Handles(Type sourceType, Type underlyingTarget) =>
        NumericTypes.IsNumeric(sourceType) &&
        (NumericTypes.IsNumeric(underlyingTarget) || underlyingTarget == typeof(bool));

    /// <summary>
    /// <paramref name="value"/> (typed <paramref name="sourceType"/>, or object holding one) as
    /// <paramref name="underlyingTarget"/>, which <see cref="Handles"/> accepts.
    /// </summary>
    public static Expression Convert(Expression value, Type sourceType, Type underlyingTarget)
    {
        var typed = value.Type != sourceType ? Expression.Convert(value, sourceType) : value;

        if (underlyingTarget == typeof(bool))
        {
            return sourceType == typeof(double)
                ? Expression.Call(typeof(NumericTruth).GetMethod(nameof(NumericTruth.FromDouble))!, typed)
                : sourceType == typeof(float)
                    ? Expression.Call(typeof(NumericTruth).GetMethod(nameof(NumericTruth.FromFloat))!, typed)
                    : Expression.NotEqual(typed, Expression.Default(sourceType));
        }

        // An enum converts through its underlying integer: there is no decimal-to-enum conversion, so a
        // decimal column (Oracle NUMBER) into an enum failed to compile (COR-013).
        var numericTarget = underlyingTarget.IsEnum ? Enum.GetUnderlyingType(underlyingTarget) : underlyingTarget;
        Expression converted = typed;
        if (sourceType == typeof(double) && numericTarget == typeof(decimal))
        {
            // The double's own digits (DecimalCoercion.DecimalFromDoubleOrThrow), not the explicit conversion's 15.
            converted = Expression.Call(typeof(NumericExpressions).GetMethod(nameof(DecimalFromDouble))!, typed);
        }
        else if (sourceType == typeof(float) && numericTarget == typeof(decimal))
        {
            // The float's own digits (DecimalCoercion.DecimalFromFloatOrThrow), not ConvertChecked's 7.
            converted = Expression.Call(typeof(NumericExpressions).GetMethod(nameof(DecimalFromFloat))!, typed);
        }
        else if (sourceType == typeof(decimal) && (numericTarget == typeof(double) || numericTarget == typeof(float)))
        {
            // Reported when the decimal's digits don't survive (TypeCoercionHelper.WarnIfDecimalLosesDigits), never silent.
            converted = Expression.Call(typeof(NumericExpressions).GetMethod(
                numericTarget == typeof(double) ? nameof(DoubleFromDecimal) : nameof(FloatFromDecimal))!, typed);
        }
        else if (sourceType != numericTarget)
        {
            if (WholeNumber.RequireFor(sourceType, numericTarget) is { } requireWhole)
            {
                typed = Expression.Call(requireWhole, typed);
            }

            converted = Expression.ConvertChecked(typed, numericTarget);
        }

        return numericTarget != underlyingTarget ? Expression.Convert(converted, underlyingTarget) : converted;
    }

    public static decimal DecimalFromDouble(double value) => types.coercion.DecimalCoercion.DecimalFromDoubleOrThrow(value);

    public static decimal DecimalFromFloat(float value) => types.coercion.DecimalCoercion.DecimalFromFloatOrThrow(value);

    public static double DoubleFromDecimal(decimal value)
    {
        TypeCoercionHelper.WarnIfDecimalLosesDigits(value, typeof(double));
        return (double)value;
    }

    public static float FloatFromDecimal(decimal value)
    {
        TypeCoercionHelper.WarnIfDecimalLosesDigits(value, typeof(float));
        return (float)value;
    }
}
