using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace pengdows.crud.Tests;

// captureReturn is reachable only by calling WrapForStoredProc directly (CommandType.StoredProcedure never captures
// the return value), and docs/stored-procedures.md documents it. The member that serves it must not be obsolete.
public class WrapForStoredProcObsolescenceTests
{
    private static MethodInfo[] Overloads(Type type) =>
        type.GetMethods().Where(m => m.Name == "WrapForStoredProc").ToArray();

    [Theory]
    [InlineData(typeof(ISqlContainer))]
    [InlineData(typeof(SqlContainer))]
    public void TheOverloadThatCanCaptureTheReturnValue_IsNotObsolete(Type type)
    {
        var capture = Overloads(type).Single(m => m.GetParameters().Any(p => p.Name == "captureReturn"));

        Assert.Null(capture.GetCustomAttribute<ObsoleteAttribute>());
    }

    [Fact]
    public void TheCaptureReturnWrappers_AreNotObsolete()
    {
        foreach (var name in new[] { "WrapForCreateWithReturn", "WrapForUpdateWithReturn", "WrapForDeleteWithReturn" })
        {
            Assert.Null(typeof(SqlContainer).GetMethod(name)!.GetCustomAttribute<ObsoleteAttribute>());
        }
    }
}
