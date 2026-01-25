using System.Linq;
using System.Reflection;
using pengdows.crud;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class ConstructorVisibilityTests
{
    [Fact]
    public void TransactionContext_HasNoPublicConstructors()
    {
        var ctors = typeof(TransactionContext)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Empty(ctors);
    }

    [Fact]
    public void TrackedConnection_HasNoPublicConstructors()
    {
        var ctors = typeof(TrackedConnection)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Empty(ctors);
    }
}