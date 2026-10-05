// =============================================================================
// FILE: InterBaseCharsetPin.cs
// PURPOSE: Pins InterBaseSql.Data.InterBaseClient's NONE charset encoding to UTF-8.
//
// AI SUMMARY:
// - The driver builds its charset table once, in a static initializer: NONE resolves to the system
//   code page when .NET code pages are registered (SqlClient and many libraries register them), else
//   to UTF-8. With a code page, text outside it is stored as '?' with no error (confirmed live: SQL
//   Server then InterBase in one process); with Charset=UTF8 instead, the server can't return
//   non-ASCII text from a NONE column (TYPE-022), so the default charset is the one that works there.
// - PinNoneToUtf8(): sets the NONE charset's encoding to UTF-8 by reflection, so text round-trips
//   whatever was loaded first (maintainer decision 2026-10-03). It changes that driver's process-wide
//   state; when the driver's shape differs it logs and returns false rather than throwing.
// =============================================================================

using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;

namespace pengdows.crud.@internal;

internal static class InterBaseCharsetPin
{
    private const string CharsetTypeName = "InterBaseSql.Data.Common.Charset";

    public static bool PinNoneToUtf8(Assembly driver, ILogger logger)
    {
        try
        {
            var charsetType = driver.GetType(CharsetTypeName, throwOnError: false);
            var getCharset = charsetType?.GetMethod("GetCharset", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                new[] { typeof(string) });
            var none = getCharset?.Invoke(null, new object[] { "NONE" });
            var encodingField = charsetType?.GetField("<Encoding>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            if (none == null || encodingField == null || encodingField.FieldType != typeof(Encoding))
            {
                logger.LogWarning(
                    "Could not pin InterBase's NONE charset to UTF-8 (driver shape changed); text outside the system code page may be stored as '?' when .NET code pages are registered.");
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
            logger.LogWarning(ex, "Could not pin InterBase's NONE charset to UTF-8.");
            return false;
        }
    }
}
