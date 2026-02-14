using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using pengdows.crud.enums;
using pengdows.crud.types;
using pengdows.crud.types.converters;
using Xunit;

namespace pengdows.crud.Tests;

public class JsonDocumentConverterTests
{
    [Fact]
    public void RoundTrip_JsonDocument_ToStringAndBack()
    {
        var converter = new JsonDocumentConverter();
        var original = JsonDocument.Parse("{\"name\":\"test\",\"value\":42}");

        // Convert to provider (string)
        var providerValue = converter.ToProviderValue(original, SupportedDatabase.PostgreSql);
        Assert.IsType<string>(providerValue);

        var json = (string)providerValue!;
        Assert.Contains("\"name\"", json);
        Assert.Contains("\"test\"", json);
        Assert.Contains("42", json);

        // Convert back from provider
        var success = converter.TryConvertFromProvider(json, SupportedDatabase.PostgreSql, out var result);
        Assert.True(success);
        Assert.NotNull(result);

        // Verify round-trip fidelity
        Assert.Equal("test", result!.RootElement.GetProperty("name").GetString());
        Assert.Equal(42, result.RootElement.GetProperty("value").GetInt32());

        original.Dispose();
        result.Dispose();
    }

    [Fact]
    public void ToProviderValue_NullInput_ReturnsNull()
    {
        var converter = new JsonDocumentConverter();
        var result = converter.ToProviderValue(null!, SupportedDatabase.PostgreSql);
        Assert.Null(result);
    }

    [Fact]
    public void ToProviderValue_WrongType_ThrowsArgumentException()
    {
        var converter = new JsonDocumentConverter();
        Assert.Throws<ArgumentException>(() =>
            converter.ToProviderValue("not a JsonDocument", SupportedDatabase.PostgreSql));
    }

    [Fact]
    public void FromProviderValue_NullInput_ReturnsNull()
    {
        var converter = new JsonDocumentConverter();
        var result = converter.FromProviderValue(null!, SupportedDatabase.PostgreSql);
        Assert.Null(result);
    }

    [Fact]
    public void FromProviderValue_DBNullInput_ReturnsNull()
    {
        var converter = new JsonDocumentConverter();
        var result = converter.FromProviderValue(DBNull.Value, SupportedDatabase.PostgreSql);
        Assert.Null(result);
    }

    [Fact]
    public void TryConvertFromProvider_StringInput_ReturnsJsonDocument()
    {
        var converter = new JsonDocumentConverter();
        var success = converter.TryConvertFromProvider(
            "{\"key\":\"value\"}", SupportedDatabase.SqlServer, out var result);

        Assert.True(success);
        Assert.NotNull(result);
        Assert.Equal("value", result!.RootElement.GetProperty("key").GetString());
        result.Dispose();
    }

    [Fact]
    public void TryConvertFromProvider_InvalidJson_ReturnsFalse()
    {
        var converter = new JsonDocumentConverter();
        var success = converter.TryConvertFromProvider(
            "not valid json{{{", SupportedDatabase.PostgreSql, out var result);

        Assert.False(success);
    }

    [Fact]
    public void TryConvertFromProvider_JsonDocumentInput_ReturnsSame()
    {
        var converter = new JsonDocumentConverter();
        var doc = JsonDocument.Parse("{}");

        var success = converter.TryConvertFromProvider(doc, SupportedDatabase.PostgreSql, out var result);
        Assert.True(success);
        Assert.Same(doc, result);

        doc.Dispose();
    }

    [Fact]
    public void TryConvertFromProvider_NonStringNonJsonDocument_ReturnsFalse()
    {
        var converter = new JsonDocumentConverter();
        var success = converter.TryConvertFromProvider(42, SupportedDatabase.PostgreSql, out var result);
        Assert.False(success);
    }

    [Fact]
    public void ToProviderValue_EmptyObject_ProducesValidJson()
    {
        var converter = new JsonDocumentConverter();
        var doc = JsonDocument.Parse("{}");
        var result = converter.ToProviderValue(doc, SupportedDatabase.MySql);
        Assert.Equal("{}", result);
        doc.Dispose();
    }

    [Fact]
    public void ToProviderValue_Array_ProducesValidJson()
    {
        var converter = new JsonDocumentConverter();
        var doc = JsonDocument.Parse("[1,2,3]");
        var result = (string)converter.ToProviderValue(doc, SupportedDatabase.SqlServer)!;
        Assert.StartsWith("[", result);
        Assert.EndsWith("]", result);
        doc.Dispose();
    }

    [Fact]
    public void TargetType_IsJsonDocument()
    {
        var converter = new JsonDocumentConverter();
        Assert.Equal(typeof(JsonDocument), converter.TargetType);
    }

    [Fact]
    public void DefaultRegistry_HasJsonDocumentConverter()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var converter = registry.GetConverter(typeof(JsonDocument));
        Assert.NotNull(converter);
        Assert.IsType<JsonDocumentConverter>(converter);
    }

    [Fact]
    public void TryConfigureParameter_WithJsonDocument_UsesConverter()
    {
        var registry = new AdvancedTypeRegistry(includeDefaults: true);
        var doc = JsonDocument.Parse("{\"hello\":\"world\"}");
        var param = new TestDbParameter();

        var result = registry.TryConfigureParameter(param, typeof(JsonDocument), doc,
            SupportedDatabase.PostgreSql);

        Assert.True(result);
        Assert.IsType<string>(param.Value);
        Assert.Contains("\"hello\"", (string)param.Value);

        doc.Dispose();
    }

    private class TestDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }

        [AllowNull]
        public override string ParameterName
        {
            get => _parameterName;
            set => _parameterName = value ?? string.Empty;
        }

        private string _parameterName = string.Empty;

        public override int Size { get; set; }

        [AllowNull]
        public override string SourceColumn
        {
            get => _sourceColumn;
            set => _sourceColumn = value ?? string.Empty;
        }

        private string _sourceColumn = string.Empty;

        public override bool SourceColumnNullMapping { get; set; }
        [AllowNull] public override object Value { get; set; } = DBNull.Value;

        public override void ResetDbType()
        {
            DbType = DbType.Object;
        }
    }
}
