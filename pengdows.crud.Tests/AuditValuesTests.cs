#region

using System;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class AuditValuesTests
{
    [Fact]
    public void As_Returns_UserId_AsType()
    {
        var values = new AuditValues { UserId = 5 };

        var id = values.As<int>();

        Assert.Equal(5, id);
    }

    [Fact]
    public void As_Throws_InvalidCast_ForWrongType()
    {
        var values = new AuditValues { UserId = 5 };

        Assert.Throws<InvalidCastException>(() => values.As<string>());
    }

    [Fact]
    public void UtcNow_Defaults_ToCurrentTime()
    {
        var before = DateTime.UtcNow;
        var values = new AuditValues { UserId = "user" };
        var after = DateTime.UtcNow;

        Assert.True(values.UtcNow >= before && values.UtcNow <= after);
    }

    [Fact]
    public void UtcNow_CanBe_Set()
    {
        var custom = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        var values = new AuditValues { UserId = "user", UtcNow = custom };

        Assert.Equal(custom, values.UtcNow);
    }
}