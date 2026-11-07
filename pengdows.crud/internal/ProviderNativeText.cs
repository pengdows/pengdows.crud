// =============================================================================
// FILE: ProviderNativeText.cs
// PURPOSE: The text form of a driver's own value for a native column (Npgsql's NpgsqlPoint, NpgsqlTsQuery, ...).
//
// AI SUMMARY:
// - TryText(value, out text): true for a value of a provider's native type, with its ToString() as the text. Npgsql's
//   types are recognised by namespace, because Npgsql is an optional dependency this assembly does not reference.
// - They are not IConvertible, so the general conversions cannot read them as text; this is the one place that does.
// =============================================================================

using System;

namespace pengdows.crud.@internal;

internal static class ProviderNativeText
{
    private const string NpgsqlNamespace = "NpgsqlTypes.";

    public static bool TryText(object value, out string text)
    {
        if (value.GetType().FullName is { } fullName && fullName.StartsWith(NpgsqlNamespace, StringComparison.Ordinal))
        {
            text = value.ToString()!;
            return true;
        }

        text = string.Empty;
        return false;
    }
}
