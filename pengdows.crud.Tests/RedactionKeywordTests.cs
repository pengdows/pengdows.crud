using System;
using System.Reflection;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// A connection string is logged in redacted form. Redaction works on key names, so a secret under a key that the
/// list doesn't name would reach the log. The list covers the common secret-bearing names.
/// </summary>
public class RedactionKeywordTests
{
    private static readonly MethodInfo Redact =
        typeof(DatabaseContext).GetMethod("RedactConnectionString",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    private static string RedactedOf(string connectionString) =>
        (string)Redact.Invoke(null, new object?[] { connectionString })!;

    [Theory]
    [InlineData("AccountKey")]   // Azure storage style
    [InlineData("ApiKey")]
    [InlineData("PrivateKey")]
    [InlineData("Signature")]    // shared-access style signatures
    [InlineData("Credential")]
    public void SecretUnderTheKey_IsRedacted(string key)
    {
        var redacted = RedactedOf($"Server=example;{key}=s3cr3t-value;Database=app");

        Assert.DoesNotContain("s3cr3t-value", redacted, StringComparison.Ordinal);
        Assert.Contains($"{key}=REDACTED", redacted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OrdinaryKeys_AreNotRedacted()
    {
        var redacted = RedactedOf("Server=example;Database=app;Port=5432;Pooling=true");

        Assert.Contains("Server=example", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Database=app", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("REDACTED", redacted, StringComparison.Ordinal);
    }
}
