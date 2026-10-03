// =============================================================================
// FILE: LargeObjectParameter.cs
// PURPOSE: Materializes Stream/TextReader parameter values for providers that can't stream them.
// =============================================================================

using System.Data;
using System.Data.Common;

namespace pengdows.crud.types.coercion;

internal static class LargeObjectParameter
{
    /// <summary>
    /// Materializes a <see cref="Stream"/> or <see cref="TextReader"/> value into the portable
    /// ADO.NET representation (byte[] / string). Providers without a native LOB parameter
    /// mapping for the value's runtime type (e.g. MemoryStream, StringReader) cannot bind the
    /// raw instance. Returns false for any other value.
    /// </summary>
    internal static bool TryMaterialize(DbParameter parameter, object? value)
    {
        if (value is Stream stream)
        {
            if (stream.CanSeek)
            {
                stream.Seek(0, SeekOrigin.Begin);
            }

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();
            parameter.DbType = DbType.Binary;
            parameter.Value = bytes;
            parameter.Size = bytes.Length;
            return true;
        }

        if (value is TextReader reader)
        {
            var text = reader.ReadToEnd();
            parameter.DbType = DbType.String;
            parameter.Value = text;
            parameter.Size = text.Length;
            return true;
        }

        return false;
    }
}
