// =============================================================================
// FILE: PostgreSqlGeometricConverters.cs
// PURPOSE: Converters for PostgreSqlPoint and PostgreSqlPolygon, the native PostgreSQL geometric types.
//
// AI SUMMARY:
// - ConvertToProvider(): the PostgreSQL text ("(x,y)", "((x,y),...)"). The column's native type is applied by the
//   statement (NativeType cast), so the text is all the library writes.
// - TryConvertFromProvider(): the value itself, its text, or Npgsql's own value (NpgsqlPoint, NpgsqlPolygon), which is
//   read through its text.
// - Thread-safe: stateless.
// =============================================================================

using pengdows.crud.enums;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.converters;

/// <summary>Converts <see cref="PostgreSqlPoint"/> to and from PostgreSQL's point text.</summary>
internal sealed class PostgreSqlPointConverter : AdvancedTypeConverter<PostgreSqlPoint>
{
    protected override object? ConvertToProvider(PostgreSqlPoint value, SupportedDatabase provider) => value.ToString();

    public override bool TryConvertFromProvider(object value, SupportedDatabase provider, out PostgreSqlPoint result)
    {
        switch (value)
        {
            case PostgreSqlPoint point:
                result = point;
                return true;
            case string text when PostgreSqlPoint.TryParse(text, out var parsed):
                result = parsed;
                return true;
            case not string when value.GetType().FullName?.StartsWith("NpgsqlTypes.", System.StringComparison.Ordinal) == true
                                 && PostgreSqlPoint.TryParse(value.ToString(), out var native):
                result = native;
                return true;
            default:
                result = default;
                return false;
        }
    }
}

/// <summary>Converts <see cref="PostgreSqlPolygon"/> to and from PostgreSQL's polygon text.</summary>
internal sealed class PostgreSqlPolygonConverter : AdvancedTypeConverter<PostgreSqlPolygon>
{
    // The default value has no points, which PostgreSQL refuses as "()": it is written as NULL.
    protected override object? ConvertToProvider(PostgreSqlPolygon value, SupportedDatabase provider) =>
        value.Points.Count == 0 ? null : value.ToString();

    public override bool TryConvertFromProvider(object value, SupportedDatabase provider, out PostgreSqlPolygon result)
    {
        switch (value)
        {
            case PostgreSqlPolygon polygon:
                result = polygon;
                return true;
            case string text when PostgreSqlPolygon.TryParse(text, out var parsed):
                result = parsed;
                return true;
            case not string when value.GetType().FullName?.StartsWith("NpgsqlTypes.", System.StringComparison.Ordinal) == true
                                 && PostgreSqlPolygon.TryParse(value.ToString(), out var native):
                result = native;
                return true;
            default:
                result = default;
                return false;
        }
    }
}
