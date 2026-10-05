// =============================================================================
// FILE: DriverCharsetPin.cs
// PURPOSE: Pins a driver's NONE charset encoding to UTF-8 (InterBase and Firebird clients).
//
// AI SUMMARY:
// - The driver builds its charset table once, in a static initializer: NONE resolves to the system
//   code page when .NET code pages are registered (SqlClient and many libraries register them), else
//   to UTF-8. With a code page, text outside it is stored as '?' with no error (confirmed live: SQL
//   Server then InterBase in one process); with Charset=UTF8 instead, the server can't return
//   non-ASCII text from a NONE column (TYPE-022), so the default charset is the one that works there.
// - FirebirdClient does the same (Encoding2.Default: the ANSI code page); confirmed live 2026-10-05,
//   Firebird after the other drivers in one integration run read "héllo" back as "h\uFFFDllo".
// - PinNoneToUtf8(driver, charsetTypeName): sets the NONE charset's encoding to UTF-8 by reflection,
//   finding NONE by the table's own lookup (GetCharset, TryGetByName or DefaultCharset), so text
//   round-trips whatever was loaded first (maintainer decision 2026-10-03). It changes that driver's
//   process-wide state; when the driver's shape differs it logs and returns false rather than throwing.
// =============================================================================

using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;

namespace pengdows.crud.@internal;

internal static class DriverCharsetPin
{
    /// <summary>
    /// Sets the NONE charset's encoding in the driver's charset table (<paramref name="charsetTypeName"/>)
    /// to UTF-8. The NONE instance is found by the table's own lookup: GetCharset("NONE") (InterBase),
    /// TryGetByName("NONE", out ...) or DefaultCharset (Firebird). Returns false, and logs, when the
    /// driver's shape differs; it never throws.
    /// </summary>
    public static bool PinNoneToUtf8(Assembly driver, string charsetTypeName, ILogger logger)
    {
        try
        {
            var charsetType = driver.GetType(charsetTypeName, throwOnError: false);
            var none = charsetType == null ? null : FindNone(charsetType);
            var encodingField = charsetType?.GetField("<Encoding>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            if (none == null || encodingField == null || encodingField.FieldType != typeof(Encoding))
            {
                logger.LogWarning(
                    "Could not pin {Driver}'s NONE charset to UTF-8 (driver shape changed); text outside the system code page may be stored wrongly when .NET code pages are registered.",
                    driver.GetName().Name);
                return false;
            }

            if (encodingField.GetValue(none) is Encoding current && current.CodePage == Encoding.UTF8.CodePage)
            {
                return true;
            }

            encodingField.SetValue(none, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not pin {Driver}'s NONE charset to UTF-8.", driver.GetName().Name);
            return false;
        }
    }

    private static object? FindNone(Type charsetType)
    {
        const BindingFlags statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        if (charsetType.GetMethod("GetCharset", statics, new[] { typeof(string) }) is { } getCharset)
        {
            return getCharset.Invoke(null, new object[] { "NONE" });
        }

        if (charsetType.GetMethod("TryGetByName", statics, new[] { typeof(string), charsetType.MakeByRefType() }) is { } tryGet)
        {
            var arguments = new object?[] { "NONE", null };
            return tryGet.Invoke(null, arguments) is true ? arguments[1] : null;
        }

        return charsetType.GetProperty("DefaultCharset", statics)?.GetValue(null);
    }
}
