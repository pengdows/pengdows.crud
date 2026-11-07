// =============================================================================
// FILE: NullabilityRules.cs
// PURPOSE: Decides whether a property is non-nullable, the one question both mappers ask before a NULL column is read.
//
// AI SUMMARY:
// - A non-nullable value type (not Nullable<T>) is non-nullable.
// - A reference-type property is non-nullable only when its nullability is annotated not-null (NullabilityInfoContext).
//   An oblivious property (no nullable annotations) is treated as nullable, so existing code keeps its behaviour.
// - A NULL into a non-nullable property is an error on every path: NullIntoNonNullableException, defined here, is the
//   one DataMappingException both mappers raise.
// =============================================================================

using System.Data;
using System.Runtime.ExceptionServices;
using System.Reflection;
using pengdows.crud.enums;
using pengdows.crud.exceptions;

namespace pengdows.crud.@internal;

internal static class NullabilityRules
{
    internal static bool IsNonNullable(PropertyInfo property)
    {
        var type = property.PropertyType;
        if (type.IsValueType)
        {
            return Nullable.GetUnderlyingType(type) == null;
        }

        return new NullabilityInfoContext().Create(property).WriteState == NullabilityState.NotNull;
    }

    /// <summary>The one error for a NULL into a non-nullable property, naming the column (both mappers raise it).</summary>
    internal static Exception NullIntoNonNullableException(IDataRecord reader, int ordinal, string property, Type type) =>
        new DataMappingException(
            $"Column '{reader.GetName(ordinal)}' is NULL, but property '{property}' is a non-nullable {type.Name}.",
            SupportedDatabase.Unknown);

    /// <summary>
    /// The NULL check on a failed enum read. A NULL column is the error. A present value returns, so the lenient mode's default
    /// applies. When the check itself cannot be answered, <paramref name="failure"/> is rethrown: the original read failure is
    /// the one that surfaces.
    /// </summary>
    internal static void RejectNullOnFailure(IDataRecord reader, int ordinal, string property, Type type, Exception failure)
    {
        bool isNull;
        try
        {
            isNull = reader.IsDBNull(ordinal);
        }
        catch (Exception)
        {
            // The check failed, so it says nothing about the value. The original read failure is the error to report.
            ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }

        if (isNull)
        {
            throw NullIntoNonNullableException(reader, ordinal, property, type);
        }
    }
}
