using System.Data;
using System.Linq.Expressions;
using System.Reflection;

namespace pengdows.crud.@internal;

/// <summary>
/// The columns read with exactly one call into the reader, whose result is also the null check: text, binary, arrays and
/// Informix collection literals. Each is one statement that assigns the property unless the column is NULL (no separate
/// IsDBNull call). Shared by the gateway's compiled mapper and the plan mapper, which used to build these blocks twice.
/// Null when the column is none of these and the caller's ordinary read applies.
/// </summary>
internal static class ColumnStatementReads
{
    private static readonly MethodInfo GuidFromBinary = typeof(TypeCoercionHelper).GetMethod(
        nameof(TypeCoercionHelper.GuidFromBinary), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <param name="reader">The reader the mapper's lambda holds, any type that has GetValue.</param>
    /// <param name="property">The property being assigned.</param>
    public static Expression? TryBuild(Expression reader, int ordinal, Type fieldType, Type targetType,
        TypeCoercionOptions? options, Expression property)
    {
        var ordinalExpr = Expression.Constant(ordinal);
        var optionsExpr = Expression.Constant(options, typeof(TypeCoercionOptions));

        if (fieldType == typeof(string) && targetType == typeof(string))
        {
            // One GetValue per string column; its DBNull result is the null check. IsDBNull and then GetString cost a
            // second round of driver work per column on some providers (Npgsql: ~0.12 µs each, measured 2026-10-04).
            return FromValue(reader, ordinalExpr, property,
                stored => Expression.Call(SingleReadColumns.StringFromValueMethod, stored, optionsExpr));
        }

        if (fieldType == typeof(byte[]))
        {
            // A binary column is one GetValue whatever it is read into, then the conversion the target needs: a GUID in
            // the dialect's byte order, the bytes themselves, or the registered coercion. No catch-all (COR-004): a
            // value that isn't a Guid (InvalidValueException) is a conversion failure the mapper reports naming the
            // column; anything else surfaces as itself.
            return FromValue(reader, ordinalExpr, property, stored =>
            {
                Expression bytes = Expression.Call(SingleReadColumns.BytesFromValueMethod, stored, optionsExpr);
                var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
                if (underlying == typeof(Guid))
                {
                    Expression guid = Expression.Call(GuidFromBinary, bytes,
                        Expression.Constant(options?.GuidBytesBigEndian ?? false));
                    return targetType != underlying ? Expression.Convert(guid, targetType) : guid;
                }

                return targetType == typeof(byte[]) ? bytes : ColumnConversion.Build(bytes, fieldType, targetType, options);
            });
        }

        if (fieldType == typeof(string) && ProviderValueFieldReader.IsReadableArray(targetType) &&
            (options?.ReadsCollectionLiterals ?? false))
        {
            // Informix returns LIST/SET/MULTISET as literal text; parse it, typed. One GetValue, which is also the null
            // check (null for NULL).
            var read = Expression.Call(
                ProviderValueFieldReader.ReadCollectionLiteralOrNullDefinition.MakeGenericMethod(targetType.GetElementType()!),
                Expression.Convert(reader, typeof(IDataRecord)), ordinalExpr);
            return FromNullable(property, targetType, read);
        }

        if (ProviderValueFieldReader.IsReadableArray(targetType) && ProviderValueFieldReader.IsArrayColumn(fieldType))
        {
            // The array reader's one GetValue is also the null check (null for NULL), so there is no IsDBNull call and
            // the provider quirks it learns once per column (Spanner refusing GetValue) are kept.
            Expression read = ProviderValueFieldReader.BindArrayReadOrNull(targetType.GetElementType()!,
                Expression.Convert(reader, typeof(IDataRecord)), ordinalExpr);
            return FromNullable(property, targetType, read.Type == targetType ? read : Expression.Convert(read, targetType));
        }

        return null;
    }

    /// <summary>One GetValue; unless it is DBNull, the property gets <paramref name="convert"/> of the value.</summary>
    internal static Expression FromValue(Expression reader, Expression ordinalExpr, Expression property,
        Func<ParameterExpression, Expression> convert)
    {
        var stored = Expression.Variable(typeof(object), "value");
        var getValue = reader.Type.GetMethod(nameof(IDataRecord.GetValue), new[] { typeof(int) }) ?? ReaderGetters.GetValue;
        return Expression.Block(
            new[] { stored },
            Expression.Assign(stored, Expression.Call(reader, getValue, ordinalExpr)),
            Expression.IfThen(
                Expression.Not(Expression.TypeIs(stored, typeof(DBNull))),
                Expression.Assign(property, convert(stored))));
    }

    // A read that returns null for NULL: the property is assigned unless that read is null.
    private static Expression FromNullable(Expression property, Type type, Expression read)
    {
        var value = Expression.Variable(type, "read");
        return Expression.Block(
            new[] { value },
            Expression.Assign(value, read),
            Expression.IfThen(
                Expression.NotEqual(value, Expression.Constant(null, type)),
                Expression.Assign(property, value)));
    }
}
