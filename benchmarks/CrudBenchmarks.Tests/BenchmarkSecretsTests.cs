using CrudBenchmarks;

namespace CrudBenchmarks.Tests;

// Every benchmark container gets its password from BenchmarkSecrets, never from a literal. These tests pin the
// properties that make a generated password safe to use: unpredictable, able to satisfy SQL Server's complexity
// rule, and safe inside a key=value connection string.
public class BenchmarkSecretsTests
{
    [Fact]
    public void NewPassword_IsNotTheSameTwice()
    {
        Assert.NotEqual(BenchmarkSecrets.NewPassword(), BenchmarkSecrets.NewPassword());
    }

    [Fact]
    public void NewPassword_IsLongEnoughToBeUnguessable()
    {
        Assert.True(BenchmarkSecrets.NewPassword().Length >= 24);
    }

    [Fact]
    public void NewPassword_HasAllFourCharacterClasses_SoSqlServerAcceptsIt()
    {
        var password = BenchmarkSecrets.NewPassword();

        Assert.Contains(password, char.IsUpper);
        Assert.Contains(password, char.IsLower);
        Assert.Contains(password, char.IsDigit);
        Assert.Contains(password, c => !char.IsLetterOrDigit(c));
    }

    [Fact]
    public void NewPassword_ContainsNoConnectionStringDelimiters()
    {
        var password = BenchmarkSecrets.NewPassword();

        Assert.DoesNotContain(password, c => c is ';' or '\'' or '"' or '=' || char.IsWhiteSpace(c));
    }

    [Fact]
    public void NewPassword_IsDrawnFromACryptographicSource()
    {
        // Two thousand samples with no repeats is not proof of randomness, but it catches a constant or a counter.
        var seen = new HashSet<string>();
        for (var i = 0; i < 2000; i++)
        {
            Assert.True(seen.Add(BenchmarkSecrets.NewPassword()));
        }
    }
}
