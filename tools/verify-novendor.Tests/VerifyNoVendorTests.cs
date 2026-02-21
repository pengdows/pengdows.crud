using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Xunit;

public class VerifyNoVendorTests
{
    [Fact]
    public void RunCore_EmptyRoot_UsesCurrentDirectory()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        var originalDir = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = tempDir.FullName;

            var asmPath = Path.Combine(AppContext.BaseDirectory, "verify-novendor.dll");
            var asm = Assembly.LoadFrom(asmPath);
            var type = asm.GetType("V", throwOnError: true)!;
            var method = type.GetMethod("RunCore", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var result = (int)method!.Invoke(null, new object?[] { new[] { "" }, CancellationToken.None })!;

            Assert.Equal(0, result);
        }
        finally
        {
            Environment.CurrentDirectory = originalDir;
            tempDir.Delete(true);
        }
    }
}
