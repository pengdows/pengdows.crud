using System;
using pengdows.crud.attributes;
using Xunit;

namespace pengdows.crud.Tests;

public class EnumColumnAttributeAdditionalTests
{
    [Fact]
    public void Constructor_NonEnumType_Throws()
    {
        Assert.Throws<ArgumentException>(() => new EnumColumnAttribute(typeof(string)));
    }
}
