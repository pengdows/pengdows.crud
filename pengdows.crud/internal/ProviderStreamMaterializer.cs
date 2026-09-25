// =============================================================================
// FILE: ProviderStreamMaterializer.cs
// PURPOSE: Keep reader-owned provider streams from escaping into mapped entities.
//
// AI SUMMARY:
// - DuckDB returns BLOB values as an UnmanagedMemoryStream over memory owned by the data
//   reader; once the reader advances or closes, that view reads zeros (confirmed live).
// - Materialize() copies such a stream into a read-only MemoryStream while the reader is
//   still alive, and returns every other stream unchanged.
// - Shared by CompiledMapperFactory, DataReaderMapper, BlobStreamCoercion and
//   BlobStreamConverter so every Stream read path behaves the same.
// =============================================================================

namespace pengdows.crud.@internal;

internal static class ProviderStreamMaterializer
{
    public static Stream Materialize(object value)
    {
        if (value.GetType() != typeof(UnmanagedMemoryStream))
        {
            return (Stream)value;
        }

        var stream = (Stream)value;
        if (stream.CanSeek)
        {
            stream.Seek(0, SeekOrigin.Begin);
        }

        using var copied = new MemoryStream();
        stream.CopyTo(copied);
        return new MemoryStream(copied.ToArray(), false);
    }
}
