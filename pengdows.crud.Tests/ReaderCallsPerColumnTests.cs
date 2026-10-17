using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

// What a column costs is mostly how many times the mapper calls into the reader: a provider's getter does real work
// (a lookup, a decode). A mapper that reads each column with one call matches what hand-written code does, so each
// mapper path is held to one reader call per column for the common types.
public class ReaderCallsPerColumnTests
{
    // Counts every call a mapper can make to read a value.
    private sealed class CountingReader : fakeDbDataReader
    {
        public CountingReader(IEnumerable<Dictionary<string, object>> rows) : base(rows)
        {
        }

        public int Calls { get; private set; }
        public List<string> Log { get; } = new();

        // fakeDb's typed getters call the virtual GetValue themselves; only the call the mapper made counts.
        private int _depth;

        private T Hit<T>(string name, Func<T> read)
        {
            if (_depth++ == 0)
            {
                Calls++;
                Log.Add(name);
            }

            try
            {
                return read();
            }
            finally
            {
                _depth--;
            }
        }

        public override bool IsDBNull(int o) => Hit(nameof(IsDBNull), () => base.IsDBNull(o));
        public override object GetValue(int o) => Hit(nameof(GetValue), () => base.GetValue(o));
        public override string GetString(int o) => Hit(nameof(GetString), () => base.GetString(o));
        public override int GetInt32(int o) => Hit(nameof(GetInt32), () => base.GetInt32(o));
        public override long GetInt64(int o) => Hit(nameof(GetInt64), () => base.GetInt64(o));
        public override decimal GetDecimal(int o) => Hit(nameof(GetDecimal), () => base.GetDecimal(o));
        public override bool GetBoolean(int o) => Hit(nameof(GetBoolean), () => base.GetBoolean(o));
        public override DateTime GetDateTime(int o) => Hit(nameof(GetDateTime), () => base.GetDateTime(o));
        public override double GetDouble(int o) => Hit(nameof(GetDouble), () => base.GetDouble(o));
        public override float GetFloat(int o) => Hit(nameof(GetFloat), () => base.GetFloat(o));
        public override Guid GetGuid(int o) => Hit(nameof(GetGuid), () => base.GetGuid(o));

        public override long GetBytes(int o, long dataOffset, byte[]? buffer, int bufferOffset, int length) =>
            Hit(nameof(GetBytes), () => base.GetBytes(o, dataOffset, buffer, bufferOffset, length));

        public override T GetFieldValue<T>(int o) => Hit("GetFieldValue<" + typeof(T).Name + ">", () => base.GetFieldValue<T>(o));
    }

    [Table("t_str")] public sealed class StringEntity { [PrimaryKey(1)] [Column("V", DbType.String)] public string V { get; set; } = ""; }
    [Table("t_bin")] public sealed class BytesEntity { [PrimaryKey(1)] [Column("V", DbType.Binary)] public byte[] V { get; set; } = []; }
    [Table("t_int")] public sealed class IntEntity { [PrimaryKey(1)] [Column("V", DbType.Int32)] public int V { get; set; } }
    [Table("t_dec")] public sealed class DecimalEntity { [PrimaryKey(1)] [Column("V", DbType.Decimal)] public decimal V { get; set; } }
    [Table("t_guid")] public sealed class GuidEntity { [PrimaryKey(1)] [Column("V", DbType.Guid)] public Guid V { get; set; } }
    public enum Mood { Sad = 0, Ok = 1, Happy = 2 }

    [Table("t_bits")] public sealed class BitArrayEntity { [PrimaryKey(1)] [Column("V", DbType.Object)] public System.Collections.BitArray? V { get; set; } }
    [Table("t_chars")] public sealed class CharArrayEntity { [PrimaryKey(1)] [Column("V", DbType.String)] public char[]? V { get; set; } }
    [Table("t_jsondoc")] public sealed class JsonDocumentEntity { [PrimaryKey(1)] [Column("V", DbType.Object)] public System.Text.Json.JsonDocument? V { get; set; } }
    [Table("t_textreader")] public sealed class TextReaderEntity { [PrimaryKey(1)] [Column("V", DbType.String)] public System.IO.TextReader? V { get; set; } }
    [Table("t_geom")] public sealed class GeometryEntity { [PrimaryKey(1)] [Column("V", DbType.Object)] public pengdows.crud.types.valueobjects.Geometry? V { get; set; } }
    [Table("t_rv")] public sealed class RowVersionEntity { [PrimaryKey(1)] [Column("V", DbType.Binary)] public pengdows.crud.types.valueobjects.RowVersion V { get; set; } }
    [Table("t_gb")] public sealed class BinaryGuidEntity { [PrimaryKey(1)] [Column("V", DbType.Guid)] public Guid V { get; set; } }
    [Table("t_stream")] public sealed class StreamEntity { [PrimaryKey(1)] [Column("V", DbType.Binary)] public System.IO.Stream? V { get; set; } }

    [Table("t_enum")] public sealed class EnumEntity { [PrimaryKey(1)] [Column("V", DbType.Int32)] public Mood V { get; set; } }
    [Table("t_dto")] public sealed class OffsetEntity { [PrimaryKey(1)] [Column("V", DbType.DateTimeOffset)] public DateTimeOffset V { get; set; } }

    private static CountingReader Reader(Type clr, string typeName, object value) =>
        new(new List<Dictionary<string, object>>
        {
            new(StringComparer.OrdinalIgnoreCase) { ["V"] = value }
        }) { Columns = [new fakeDbColumn("V", clr, typeName)] };

    // The calls the mapper makes for one row, after the mapping is resolved.
    private static int CompiledCalls<T>(Type clr, string typeName, object value) where T : class, new()
    {
        using var reader = Reader(clr, typeName, value);
        var columns = new TypeMapRegistry().GetTableInfo<T>().Columns;
        var mapper = CompiledMapperFactory<T>.Create<DbDataReader>(
            reader, columns, EnumParseFailureMode.Throw, ["V"], [clr]);
        Assert.True(reader.Read());
        var before = reader.Calls;
        mapper(reader);
        LastLog = string.Join(",", reader.Log.Skip(before));
        return reader.Calls - before;
    }

    private static int PlanCalls<T>(Type clr, string typeName, object value) where T : class, new()
    {
        using var reader = Reader(clr, typeName, value);
        var mapper = DataReaderMapper.ResolveRowMapper<T>(reader, MapperOptions.Default);
        Assert.True(reader.Read());
        var before = reader.Calls;
        mapper(reader);
        LastLog = string.Join(",", reader.Log.Skip(before));
        return reader.Calls - before;
    }

    [ThreadStatic] private static string? LastLog;

    private static void One(int calls) => Assert.True(calls == 1, $"{calls} reader calls: {LastLog}");

    [Fact] public void Compiled_String_OneCall() => One(CompiledCalls<StringEntity>(typeof(string), "TEXT", "x"));
    [Fact] public void Compiled_Bytes_OneCall() => One(CompiledCalls<BytesEntity>(typeof(byte[]), "BLOB", new byte[] { 1, 2, 3, 4 }));
    [Fact] public void Compiled_Int32_OneCall() => One(CompiledCalls<IntEntity>(typeof(int), "INTEGER", 7));
    [Fact] public void Compiled_Decimal_OneCall() => One(CompiledCalls<DecimalEntity>(typeof(decimal), "DECIMAL", 7m));
    [Fact] public void Compiled_Guid_OneCall() => One(CompiledCalls<GuidEntity>(typeof(Guid), "UUID", Guid.NewGuid()));
    [Fact] public void Compiled_DateTimeOffset_OneCall() => One(CompiledCalls<OffsetEntity>(typeof(DateTimeOffset), "TIMESTAMP_TZ", DateTimeOffset.UnixEpoch));

    [Fact] public void Plan_String_OneCall() => One(PlanCalls<StringEntity>(typeof(string), "TEXT", "x"));
    [Fact] public void Plan_Bytes_OneCall() => One(PlanCalls<BytesEntity>(typeof(byte[]), "BLOB", new byte[] { 1, 2, 3, 4 }));
    [Fact] public void Plan_Int32_OneCall() => One(PlanCalls<IntEntity>(typeof(int), "INTEGER", 7));
    [Fact] public void Plan_Decimal_OneCall() => One(PlanCalls<DecimalEntity>(typeof(decimal), "DECIMAL", 7m));
    [Fact] public void Plan_Guid_OneCall() => One(PlanCalls<GuidEntity>(typeof(Guid), "UUID", Guid.NewGuid()));
    [Fact] public void Plan_DateTimeOffset_OneCall() => One(PlanCalls<OffsetEntity>(typeof(DateTimeOffset), "TIMESTAMP_TZ", DateTimeOffset.UnixEpoch));

    // A non-nullable value type has no IsDBNull guard on the gateway path (a NULL fails loudly, as for every other
    // value type), so an integer column into a non-nullable enum is one call.
    [Fact]
    public void Compiled_EnumInt_OneCall() => One(CompiledCalls<EnumEntity>(typeof(int), "INTEGER", 1));

    // The plan path skips the guard for a non-nullable value type, so an integer column into an enum is one call.
    [Fact]
    public void Plan_EnumInt_OneCall() => One(PlanCalls<EnumEntity>(typeof(int), "INTEGER", 1));

    // A binary column read into something other than byte[] is the same single read: GetValue, then the conversion.
    [Fact] public void Compiled_BinaryIntoRowVersion_OneCall() => One(CompiledCalls<RowVersionEntity>(typeof(byte[]), "BLOB", new byte[8]));
    [Fact] public void Compiled_BinaryIntoGuid_OneCall() => One(CompiledCalls<BinaryGuidEntity>(typeof(byte[]), "BINARY(16)", new byte[16]));
    [Fact] public void Compiled_BinaryIntoStream_OneCall() => One(CompiledCalls<StreamEntity>(typeof(byte[]), "BLOB", new byte[] { 1, 2, 3 }));
    [Fact] public void Plan_BinaryIntoRowVersion_OneCall() => One(PlanCalls<RowVersionEntity>(typeof(byte[]), "BLOB", new byte[8]));
    [Fact] public void Plan_BinaryIntoGuid_OneCall() => One(PlanCalls<BinaryGuidEntity>(typeof(byte[]), "BINARY(16)", new byte[16]));
    [Fact] public void Plan_BinaryIntoStream_OneCall() => One(PlanCalls<StreamEntity>(typeof(byte[]), "BLOB", new byte[] { 1, 2, 3 }));

    // A reference-type column is one GetValue whose DBNull is the null check, on both paths.
    private static readonly object Bits = new System.Collections.BitArray(new[] { true, false, true });
    private static readonly object Chars = "abc".ToCharArray();
    private static readonly object Wkb = pengdows.crud.types.valueobjects.Geometry.FromWellKnownBinary(
        Convert.FromHexString("0101000000000000000000F03F0000000000000040"), 4326);

    [Fact] public void Compiled_BitArray_OneCall() => One(CompiledCalls<BitArrayEntity>(typeof(System.Collections.BitArray), "BIT VARYING", Bits));
    [Fact] public void Compiled_CharArray_OneCall() => One(CompiledCalls<CharArrayEntity>(typeof(char[]), "CHAR", Chars));
    [Fact] public void Compiled_JsonDocumentFromText_OneCall() => One(CompiledCalls<JsonDocumentEntity>(typeof(string), "JSON", "{\"a\":1}"));
    [Fact] public void Compiled_TextReaderFromText_OneCall() => One(CompiledCalls<TextReaderEntity>(typeof(string), "TEXT", "abc"));
    [Fact] public void Compiled_Spatial_OneCall() => One(CompiledCalls<GeometryEntity>(typeof(pengdows.crud.types.valueobjects.Geometry), "GEOMETRY", Wkb));
    [Fact] public void Plan_BitArray_OneCall() => One(PlanCalls<BitArrayEntity>(typeof(System.Collections.BitArray), "BIT VARYING", Bits));
    [Fact] public void Plan_CharArray_OneCall() => One(PlanCalls<CharArrayEntity>(typeof(char[]), "CHAR", Chars));
    [Fact] public void Plan_Spatial_OneCall() => One(PlanCalls<GeometryEntity>(typeof(pengdows.crud.types.valueobjects.Geometry), "GEOMETRY", Wkb));
}
