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

    // The usage text shows `--allow "a;b"` (space-separated), but only `--allow=a;b` was parsed; the
    // space form was silently ignored and every default-forbidden vendor stayed forbidden.
    public static TheoryData<string[]> AllowForms() => new()
    {
        new[] { "dir", "--allow=Npgsql;DuckDB" },
        new[] { "dir", "--allow", "Npgsql;DuckDB" },
        new[] { "dir", "--ALLOW", "Npgsql;DuckDB" }
    };

    [Theory]
    [MemberData(nameof(AllowForms))]
    public void ParseAllows_AcceptsBothForms(string[] args)
    {
        var asm = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "verify-novendor.dll"));
        var method = asm.GetType("V", throwOnError: true)!
            .GetMethod("ParseAllows", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var allows = (string[])method!.Invoke(null, new object?[] { args })!;

        Assert.Equal(new[] { "Npgsql", "DuckDB" }, allows);
    }
}
