using pengdows.crud.enums;
using Xunit;

// Stands in for an Npgsql value type (NpgsqlTypes.NpgsqlPoint, NpgsqlTsQuery, ...): the library recognises those by namespace.
namespace NpgsqlTypes
{
    public sealed class FakeNativeText
    {
        public override string ToString() => "(1,2)";
    }
}

namespace pengdows.crud.Tests
{
    /// <summary>
    /// Npgsql returns its own value types for a native PostgreSQL column (a point, a tsquery, a log sequence number). They
    /// are not IConvertible, so a string read of one has no conversion: its text is the column's value.
    /// </summary>
    public class NpgsqlNativeTextTests
    {
        [Fact]
        public void ANpgsqlNativeValueReadsAsItsText()
        {
            var value = new NpgsqlTypes.FakeNativeText();

            var text = pengdows.crud.TypeCoercionHelper.Coerce(value, value.GetType(), typeof(string), null);

            Assert.Equal("(1,2)", text);
        }
    }
}
