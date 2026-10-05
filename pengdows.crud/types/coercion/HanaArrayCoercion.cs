// =============================================================================
// FILE: HanaArrayCoercion.cs
// PURPOSE: Reads a SAP HANA ARRAY column from the bytes Sap.Data.Hana.Net returns for it (TYPE-020).
//
// AI SUMMARY:
// - Sap.Data.Hana.Net returns an ARRAY column as VARBINARY holding HANA's wire encoding (confirmed
//   live, HANA Express 2.00.088): an int32 element count, then each element as the protocol sends a
//   value of the element type —
//   * INT / SMALLINT / BIGINT: a null indicator byte (00 NULL, 01 present) and the little-endian value;
//   * DOUBLE / REAL: the raw little-endian value, all 0xFF bytes for NULL;
//   * (N)VARCHAR: a length indicator (0-245 the length, F6 + uint16, F7 + int32, FF NULL) and CESU-8 bytes.
// - The driver reports the column only as varbinary, so the element type is the property's: an
//   int[] reads an INTEGER ARRAY, a long[] a BIGINT ARRAY. A value that doesn't decode exactly as that
//   type (another element width, a NULL into a non-nullable element) throws rather than misreads.
// =============================================================================

using System.Buffers.Binary;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace pengdows.crud.types.coercion;

internal static class HanaArrayCoercion
{
    public static void RegisterAll(CoercionRegistry registry, enums.SupportedDatabase database)
    {
        registry.Register(database, new Fixed<int>(4, true, static s => BinaryPrimitives.ReadInt32LittleEndian(s)));
        registry.Register(database, new NullableElements<int>(4, true, static s => BinaryPrimitives.ReadInt32LittleEndian(s)));
        registry.Register(database, new Fixed<long>(8, true, static s => BinaryPrimitives.ReadInt64LittleEndian(s)));
        registry.Register(database, new NullableElements<long>(8, true, static s => BinaryPrimitives.ReadInt64LittleEndian(s)));
        registry.Register(database, new Fixed<short>(2, true, static s => BinaryPrimitives.ReadInt16LittleEndian(s)));
        registry.Register(database, new NullableElements<short>(2, true, static s => BinaryPrimitives.ReadInt16LittleEndian(s)));
        registry.Register(database, new Fixed<double>(8, false, static s => BinaryPrimitives.ReadDoubleLittleEndian(s)));
        registry.Register(database, new NullableElements<double>(8, false, static s => BinaryPrimitives.ReadDoubleLittleEndian(s)));
        registry.Register(database, new Fixed<float>(4, false, static s => BinaryPrimitives.ReadSingleLittleEndian(s)));
        registry.Register(database, new NullableElements<float>(4, false, static s => BinaryPrimitives.ReadSingleLittleEndian(s)));
        registry.Register(database, new Strings());
    }

    private delegate T ReadValue<out T>(ReadOnlySpan<byte> bytes);

    // Decodes each element of a fixed-width array, null as null; throws unless the bytes are exactly
    // that layout.
    private static T?[] DecodeFixed<T>(byte[] bytes, int width, bool hasIndicator, ReadValue<T> read) where T : struct
    {
        var count = Count(bytes);
        var values = new T?[count];
        var at = 4;
        for (var i = 0; i < count; i++)
        {
            if (hasIndicator)
            {
                if (at >= bytes.Length || bytes[at] > 1)
                {
                    throw Malformed(typeof(T));
                }

                if (bytes[at++] == 0)
                {
                    continue;
                }
            }

            if (at + width > bytes.Length)
            {
                throw Malformed(typeof(T));
            }

            var span = bytes.AsSpan(at, width);
            at += width;
            if (!hasIndicator && !span.ContainsAnyExcept((byte)0xFF))
            {
                continue;
            }

            values[i] = read(span);
        }

        if (at != bytes.Length)
        {
            throw Malformed(typeof(T));
        }

        return values;
    }

    private static int Count(byte[] bytes)
    {
        var count = bytes.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(bytes) : -1;
        if (count < 0 || count > bytes.Length)
        {
            throw new FormatException("The value is not a SAP HANA ARRAY.");
        }

        return count;
    }

    private static FormatException Malformed(Type element) =>
        new($"The SAP HANA ARRAY doesn't decode as {element.Name} elements; map an INTEGER ARRAY to int[], " +
            "BIGINT to long[], SMALLINT to short[], DOUBLE to double[], REAL to float[] and (N)VARCHAR to string[].");

    private sealed class Fixed<T> : DbCoercion<T[]> where T : struct
    {
        private readonly int _width;
        private readonly bool _hasIndicator;
        private readonly ReadValue<T> _read;

        public Fixed(int width, bool hasIndicator, ReadValue<T> read)
        {
            _width = width;
            _hasIndicator = hasIndicator;
            _read = read;
        }

        public override bool TryRead(in DbValue src, out T[] value)
        {
            if (src.RawValue is not byte[] bytes)
            {
                value = null!;
                return false;
            }

            var decoded = DecodeFixed(bytes, _width, _hasIndicator, _read);
            value = new T[decoded.Length];
            for (var i = 0; i < decoded.Length; i++)
            {
                value[i] = decoded[i] ?? throw new FormatException(
                    $"The SAP HANA ARRAY holds a NULL element; read it into a {typeof(T).Name}?[] property.");
            }

            return true;
        }

        public override bool TryWrite([AllowNull] T[] value, DbParameter parameter)
        {
            parameter.Value = value is null ? DBNull.Value : value;
            return true;
        }
    }

    private sealed class NullableElements<T> : DbCoercion<T?[]> where T : struct
    {
        private readonly int _width;
        private readonly bool _hasIndicator;
        private readonly ReadValue<T> _read;

        public NullableElements(int width, bool hasIndicator, ReadValue<T> read)
        {
            _width = width;
            _hasIndicator = hasIndicator;
            _read = read;
        }

        public override bool TryRead(in DbValue src, out T?[] value)
        {
            if (src.RawValue is not byte[] bytes)
            {
                value = null!;
                return false;
            }

            value = DecodeFixed(bytes, _width, _hasIndicator, _read);
            return true;
        }

        public override bool TryWrite([AllowNull] T?[] value, DbParameter parameter)
        {
            parameter.Value = value is null ? DBNull.Value : value;
            return true;
        }
    }

    private sealed class Strings : DbCoercion<string[]>
    {
        public override bool TryRead(in DbValue src, out string[] value)
        {
            if (src.RawValue is not byte[] bytes)
            {
                value = null!;
                return false;
            }

            var count = Count(bytes);
            var values = new string?[count];
            var at = 4;
            for (var i = 0; i < count; i++)
            {
                if (at >= bytes.Length)
                {
                    throw Malformed(typeof(string));
                }

                int length = bytes[at++];
                switch (length)
                {
                    case 0xFF:
                        continue;
                    case 0xF6 when at + 2 <= bytes.Length:
                        length = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));
                        at += 2;
                        break;
                    case 0xF7 when at + 4 <= bytes.Length:
                        length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at));
                        at += 4;
                        break;
                    case > 0xF5:
                        throw Malformed(typeof(string));
                }

                if (length < 0 || at + length > bytes.Length)
                {
                    throw Malformed(typeof(string));
                }

                values[i] = Cesu8.Decode(bytes.AsSpan(at, length));
                at += length;
            }

            if (at != bytes.Length)
            {
                throw Malformed(typeof(string));
            }

            value = values!;
            return true;
        }

        public override bool TryWrite([AllowNull] string[] value, DbParameter parameter)
        {
            parameter.Value = value is null ? DBNull.Value : value;
            return true;
        }
    }
}

/// <summary>
/// CESU-8, the encoding HANA's protocol uses for text: UTF-8 except that a character outside the BMP is
/// sent as its two UTF-16 surrogates, three bytes each, which a strict UTF-8 decoder rejects.
/// </summary>
internal static class Cesu8
{
    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        var chars = new StringBuilder(bytes.Length);
        for (var i = 0; i < bytes.Length;)
        {
            var b = bytes[i];
            if (b < 0x80)
            {
                chars.Append((char)b);
                i++;
            }
            else if ((b & 0xE0) == 0xC0 && i + 1 < bytes.Length)
            {
                chars.Append((char)(((b & 0x1F) << 6) | (bytes[i + 1] & 0x3F)));
                i += 2;
            }
            else if ((b & 0xF0) == 0xE0 && i + 2 < bytes.Length)
            {
                chars.Append((char)(((b & 0x0F) << 12) | ((bytes[i + 1] & 0x3F) << 6) | (bytes[i + 2] & 0x3F)));
                i += 3;
            }
            else if ((b & 0xF8) == 0xF0 && i + 3 < bytes.Length)
            {
                // Plain 4-byte UTF-8, which HANA doesn't send but costs nothing to accept.
                var codePoint = ((b & 0x07) << 18) | ((bytes[i + 1] & 0x3F) << 12) | ((bytes[i + 2] & 0x3F) << 6) |
                                (bytes[i + 3] & 0x3F);
                chars.Append(char.ConvertFromUtf32(codePoint));
                i += 4;
            }
            else
            {
                throw new FormatException("The SAP HANA text is not valid CESU-8.");
            }
        }

        return chars.ToString();
    }
}
