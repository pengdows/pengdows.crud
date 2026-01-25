#region

using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class AuditValueResolverTests
{
    private class TestResolver : crud.AuditValueResolver
    {
        private readonly IAuditValues _values;

        public TestResolver(IAuditValues values)
        {
            _values = values;
        }

        public override IAuditValues Resolve()
        {
            return _values;
        }
    }

    [Fact]
    public void AuditValueResolver_IsAbstract()
    {
        Assert.True(typeof(crud.AuditValueResolver).IsAbstract);
        Assert.True(typeof(IAuditValueResolver).IsAssignableFrom(typeof(crud.AuditValueResolver)));
    }

    [Fact]
    public void Resolve_ReturnsExpectedValues()
    {
        var expected = new AuditValues { UserId = "u" };
        var resolver = new TestResolver(expected);

        var result = resolver.Resolve();

        Assert.Same(expected, result);
    }
}