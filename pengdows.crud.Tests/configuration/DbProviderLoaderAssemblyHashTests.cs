using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using Xunit;

namespace pengdows.crud.Tests.configuration;

/// <summary>
/// An optional <c>AssemblySha256</c> pins a configured <c>AssemblyPath</c> to the bytes that were reviewed. A file
/// that doesn't match is refused before it is loaded, so a swapped or planted provider DLL fails at startup.
/// </summary>
public class DbProviderLoaderAssemblyHashTests
{
    // Loaded from the test output directory, so it is inside the base directory the loader requires.
    private const string ProviderFile = "pengdows.crud.fakeDb.dll";

    private static string ProviderPath => Path.Combine(AppContext.BaseDirectory, ProviderFile);

    private static string Sha256Of(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static DbProviderLoader LoaderFor(Dictionary<string, string?> entry)
    {
        var values = new Dictionary<string, string?>();
        foreach (var pair in entry)
        {
            values["DatabaseProviders:pinned:" + pair.Key] = pair.Value;
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new DbProviderLoader(config, NullLogger<DbProviderLoader>.Instance);
    }

    private static Dictionary<string, string?> FakeDbEntry(string? sha256) => new()
    {
        ["ProviderName"] = "pinned.fakedb",
        ["AssemblyPath"] = ProviderFile,
        ["FactoryType"] = "pengdows.crud.fakeDb.fakeDbFactory",
        ["AssemblySha256"] = sha256
    };

    [Fact]
    public void MatchingHash_LoadsTheProvider()
    {
        var loader = LoaderFor(FakeDbEntry(Sha256Of(ProviderPath)));
        var services = new ServiceCollection();

        loader.LoadAndRegisterProviders(services);
        using var provider = services.BuildServiceProvider();

        Assert.IsAssignableFrom<DbProviderFactory>(provider.GetKeyedService<DbProviderFactory>("pinned.fakedb"));
    }

    [Fact]
    public void MatchingHash_IsCaseInsensitive()
    {
        var loader = LoaderFor(FakeDbEntry(Sha256Of(ProviderPath).ToLowerInvariant()));

        loader.LoadAndRegisterProviders(new ServiceCollection());
    }

    [Fact]
    public void MismatchedHash_IsRefusedBeforeTheAssemblyLoads()
    {
        var loader = LoaderFor(FakeDbEntry(new string('0', 64)));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            loader.LoadAndRegisterProviders(new ServiceCollection()));

        Assert.Contains("SHA-256", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-hex")]
    [InlineData("abc")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdeg")]
    public void MalformedHash_IsRefused(string sha256)
    {
        var loader = LoaderFor(FakeDbEntry(sha256));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            loader.LoadAndRegisterProviders(new ServiceCollection()));

        Assert.Contains("64 hexadecimal", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HashWithAssemblyName_IsRefused_BecauseNoFileIsReadToCheck()
    {
        var loader = LoaderFor(new Dictionary<string, string?>
        {
            ["ProviderName"] = "pinned.fakedb",
            ["AssemblyName"] = "pengdows.crud.fakeDb",
            ["FactoryType"] = "pengdows.crud.fakeDb.fakeDbFactory",
            ["AssemblySha256"] = new string('0', 64)
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            loader.LoadAndRegisterProviders(new ServiceCollection()));

        Assert.Contains("AssemblyPath", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NoHash_StillLoadsTheProvider_ForExistingConfigurations()
    {
        var loader = LoaderFor(FakeDbEntry(sha256: null));

        loader.LoadAndRegisterProviders(new ServiceCollection());
    }
}
