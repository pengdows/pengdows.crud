// =============================================================================
// FILE: BinaryCoercion.cs
// PURPOSE: Binary column values: the bytes of a column, and a GUID read from its 16 bytes in the dialect's byte order.
//
// AI SUMMARY:
// - ReadBytes reads a binary column's whole value (a heap buffer for a long one), so a provider's length limit doesn't cap it.
// - ReadGuidFromBytes / GuidFromBinary / GuidBytes: the GUID of 16 bytes, mixed-endian (.NET) or big-endian (a dialect that
//   stores GUID bytes that way), and the reverse. A value that is not 16 bytes is an InvalidValueException (COR-003).
// - TryDecodeGuidBytes: the GUID of a 16-byte binary value in a coercion.
// =============================================================================

using System;
using System.Data;
using System.Globalization;
using pengdows.crud.exceptions;

namespace pengdows.crud.types.coercion;

internal static class BinaryCoercion
{
    /// <summary>A Guid from its 16 stored bytes in the given byte order.</summary>
    internal static Guid GuidFromBytes(byte[] bytes, bool bigEndian) => new(bytes, bigEndian);

    // A 16-byte value read into a Guid is decoded in the dialect's Guid byte order (TYPE-002);
    // the registered Guid coercion only knows .NET's mixed-endian order.
    // The 16 bytes of a Guid in the dialect's byte order (the inverse of TryDecodeGuidBytes).
    internal static byte[] GuidBytes(Guid guid, bool bigEndian)
    {
        var bytes = new byte[16];
        guid.TryWriteBytes(bytes, bigEndian, out _);
        return bytes;
    }

    internal static bool TryDecodeGuidBytes(object value, bool bigEndian, out object guid)
    {
        switch (value)
        {
            case byte[] { Length: 16 } bytes:
                guid = new Guid(bytes, bigEndian);
                return true;
            case ReadOnlyMemory<byte> { Length: 16 } memory:
                guid = new Guid(memory.Span, bigEndian);
                return true;
            case ArraySegment<byte> { Count: 16 } segment:
                guid = new Guid(segment.AsSpan(), bigEndian);
                return true;
            default:
                guid = null!;
                return false;
        }
    }

    /// <summary>
    /// Byte reader for compiled mappers: the value is read straight into an array of the length the
    /// provider reports. GetBytes may return fewer bytes than requested, so it reads until the value
    /// is complete.
    /// </summary>
    internal static byte[] ReadBytes(IDataRecord reader, int ordinal)
    {
        var length = reader.GetBytes(ordinal, 0, null, 0, 0);
        if (length <= 0)
        {
            return Array.Empty<byte>();
        }

        // The reported length sizes the result: read straight into it (a rented buffer for small
        // values cost a second copy and allocated the same array).
        var heapBuffer = new byte[length];
        var total = ReadAllBytes(reader, ordinal, heapBuffer, (int)length);
        if (total < heapBuffer.Length)
        {
            Array.Resize(ref heapBuffer, total);
        }

        return heapBuffer;
    }

    private static int ReadAllBytes(IDataRecord reader, int ordinal, byte[] buffer, int length)
    {
        var total = 0;
        while (total < length)
        {
            var read = (int)reader.GetBytes(ordinal, total, buffer, total, length - total);
            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    /// <summary>
    /// Reads a GUID from a binary column without allocating a byte array.
    /// Optimized for SQLite and other providers that store GUIDs as BLOBs.
    /// </summary>
    internal static Guid ReadGuidFromBytes(IDataRecord reader, int ordinal) => ReadGuidFromBytes(reader, ordinal, false);

    /// <summary>
    /// Reads a GUID from a binary column in the given byte order (RFC 4122 big-endian or .NET's
    /// mixed-endian order; <c>SqlDialect.StoresGuidBytesBigEndian</c>).
    /// </summary>
    private static exceptions.InvalidValueException NotAGuid(long length) =>
        new($"Binary column holds {length.ToString(CultureInfo.InvariantCulture)} bytes; a GUID needs exactly 16.");

    /// <summary>
    /// A GUID from a binary value already read (one GetValue), in the given byte order. Exactly 16 bytes, as
    /// <see cref="ReadGuidFromBytes(IDataRecord, int, bool)"/> requires (COR-003).
    /// </summary>
    internal static Guid GuidFromBinary(byte[] bytes, bool bigEndian) =>
        bytes.Length == 16 ? new Guid(bytes, bigEndian) : throw NotAGuid(bytes.Length);

    internal static Guid ReadGuidFromBytes(IDataRecord reader, int ordinal, bool bigEndian)
    {
        // Exactly 16 bytes: a longer value was read as its first 16, a silently wrong Guid (COR-003).
        // The length comes from the provider; asking for a 17th byte to prove there is none threw on
        // MySql.Data, which refuses a read at the end of a value (COR-014).
        var length = reader.GetBytes(ordinal, 0, null, 0, 0);
        if (length != 16)
        {
            throw NotAGuid(length);
        }

        var temp = System.Buffers.ArrayPool<byte>.Shared.Rent(16);
        try
        {
            var read = ReadAllBytes(reader, ordinal, temp, 16);
            if (read != 16)
            {
                throw NotAGuid(read);
            }

            return new Guid(temp.AsSpan(0, 16), bigEndian);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(temp);
        }
    }
}
