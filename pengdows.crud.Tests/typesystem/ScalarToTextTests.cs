using System;
using Xunit;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// Found by the stage-0 pins: a value read into a string property depended on its type. A number
/// became its text, a DateTime became "10/05/2026 13:45:30" (US order, fraction dropped), and a
/// DateTimeOffset, DateOnly, TimeOnly, TimeSpan or Guid failed. Every scalar now reads as its canonical
/// invariant text, exact to the tick; binary stays an error (its text encoding is ambiguous).
/// </summary>
public class ScalarToTextTests
{
    public static TheoryData<object, string> Cases() => new()
    {
        { new DateTime(2026, 10, 5, 13, 45, 30, DateTimeKind.Utc).AddTicks(1234567), "2026-10-05T13:45:30.1234567Z" },
        { new DateTime(2026, 10, 5, 13, 45, 30, DateTimeKind.Unspecified), "2026-10-05T13:45:30.0000000" },
        { new DateTimeOffset(2026, 10, 5, 15, 45, 30, TimeSpan.FromHours(2)).AddTicks(5), "2026-10-05T15:45:30.0000005+02:00" },
        { new DateOnly(2026, 10, 5), "2026-10-05" },
        { new TimeOnly(13, 45, 30).Add(TimeSpan.FromTicks(1234567)), "13:45:30.1234567" },
        { new TimeOnly(13, 45), "13:45:00" },
        { new TimeSpan(1, 2, 3, 4).Add(TimeSpan.FromTicks(5)), "1.02:03:04.0000005" },
        { new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"), "0f8fad5b-d9cb-469f-a165-70867728950e" },
        { 42, "42" },
        { 0.1, "0.1" }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Scalar_ReadsAsItsCanonicalText(object value, string expected)
    {
        Assert.Equal(expected, TypeCoercionHelper.Coerce(value, value.GetType(), typeof(string)));
    }

    [Fact]
    public void Binary_IntoText_StillFails()
    {
        Assert.Throws<InvalidCastException>(() => TypeCoercionHelper.Coerce(new byte[] { 104, 105 }, typeof(byte[]), typeof(string)));
    }
}
