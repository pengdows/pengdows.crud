#region

using System;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public sealed class TypeForwardingTests
{
    [Theory]
    [InlineData("pengdows.crud.TypeCoercionOptions, pengdows.crud", typeof(global::pengdows.crud.TypeCoercionOptions))]
    [InlineData("pengdows.crud.JsonPassThrough, pengdows.crud", typeof(global::pengdows.crud.JsonPassThrough))]
    [InlineData("pengdows.crud.TimeMappingPolicy, pengdows.crud", typeof(global::pengdows.crud.TimeMappingPolicy))]
    public void ForwardedTypes_AreResolved(string assemblyQualifiedName, Type expectedType)
    {
        var resolved = Type.GetType(assemblyQualifiedName, throwOnError: false);
        Assert.NotNull(resolved);
        Assert.Equal(expectedType, resolved);
    }

    [Theory]
    [InlineData("pengdows.crud.DoesNotExist, pengdows.crud")]
    [InlineData("pengdows.crud.TypeCoercionOptions, pengdows.crud.runtime")]
    public void ForwardedTypes_InvalidNames_ReturnNull(string assemblyQualifiedName)
    {
        var resolved = Type.GetType(assemblyQualifiedName, throwOnError: false);
        Assert.Null(resolved);
    }
}
