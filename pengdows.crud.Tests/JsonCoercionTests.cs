using System;
using System.IO;
using System.Text;
using System.Text.Json;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests
{
    /// <summary>
    /// JSON text is read and written in one place: the text of any JSON-carrying input (text, bytes, a stream, a document, an
    /// element, a node, or an object to serialise), and the JSON null for blank text.
    /// </summary>
    public class JsonCoercionTests
    {
        [Fact]
        public void EveryJsonCarryingInputGivesItsText()
        {
            var options = new JsonSerializerOptions();
            Assert.Equal("{\"a\":1}", JsonCoercion.ExtractJsonString("{\"a\":1}", options));
            Assert.Equal("{\"b\":2}", JsonCoercion.ExtractJsonString(Encoding.UTF8.GetBytes("{\"b\":2}"), options));
            Assert.Equal("{\"e\":5}", JsonCoercion.ExtractJsonString(new MemoryStream(Encoding.UTF8.GetBytes("{\"e\":5}")), options));
            Assert.Equal(string.Empty, JsonCoercion.ExtractJsonString(Array.Empty<byte>(), options));
        }

        [Fact]
        public void AnObjectIsSerialisedAsJson()
        {
            Assert.Contains("\"h\":8", JsonCoercion.ExtractJsonString(new { h = 8 }, new JsonSerializerOptions()));
        }

        [Fact]
        public void BlankTextIsTheJsonNull()
        {
            Assert.Equal("null", JsonCoercion.JsonTextOrNullLiteral("   "));
            Assert.Equal("null", JsonCoercion.JsonTextOrNullLiteral(null));
            Assert.Equal("{}", JsonCoercion.JsonTextOrNullLiteral("{}"));
        }

        [Fact]
        public void ACarrierOfJsonTextIsRecognised()
        {
            Assert.True(JsonCoercion.CarriesJsonText("{}"));
            Assert.True(JsonCoercion.CarriesJsonText(Encoding.UTF8.GetBytes("{}")));
            Assert.False(JsonCoercion.CarriesJsonText(42));
        }

        [Fact]
        public void ADocumentIsParsedFromAnyJsonCarrier()
        {
            using var doc = JsonCoercion.ToJsonDocument("{\"c\":3}", new JsonSerializerOptions());

            Assert.Equal(3, doc.RootElement.GetProperty("c").GetInt32());
        }

        [Fact]
        public void GetJsonTextReadsAStreamToItsText()
        {
            var text = JsonCoercion.GetJsonText(new MemoryStream(Encoding.UTF8.GetBytes("{\"i\":9}")));

            Assert.Equal("{\"i\":9}", text);
        }
    }
}
