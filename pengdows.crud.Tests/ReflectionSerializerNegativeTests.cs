using System;
using Xunit;

namespace pengdows.crud.Tests;

public class ReflectionSerializerNegativeTests
{
    private class Dummy
    {
    }

    [Fact]
    public void Deserialize_InvalidData_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ReflectionSerializer.Deserialize<Dummy>("not a dict"));
    }
}