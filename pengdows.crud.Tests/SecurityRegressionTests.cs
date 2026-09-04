using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.Tests.Logging;
using Xunit;

namespace pengdows.crud.Tests;

// [Collection("TypeRegistry")]: this class assigns TypeCoercionHelper.Logger (a process-global
// static) directly rather than saving/restoring around a scoped try/finally in some tests (see
// ConnectionString_PublicSurface_ReturnsRedactedValue's setup below) — sharing the "TypeRegistry"
// collection with every other test class that touches that same static (see
// TypeCoercionHelperLoggerRaceTests.cs for the full root-cause note) prevents this from racing
// with them.
[Collection("TypeRegistry")]
public sealed class SecurityRegressionTests
{
    [Fact]
    public void ConnectionString_PublicSurface_ReturnsRedactedValue()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=writer;User Id=app;Password=super-secret;Token=abc123;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        using var context = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        Assert.DoesNotContain("super-secret", context.ConnectionString, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", context.ConnectionString, StringComparison.Ordinal);
        Assert.Contains("Password=REDACTED", context.ConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Token=REDACTED", context.ConnectionString, StringComparison.OrdinalIgnoreCase);

        using var transaction = context.BeginTransaction();
        Assert.Equal(context.ConnectionString, transaction.ConnectionString);
    }

    [Fact]
    public void Coerce_InvalidJson_DoesNotLogPayloadValue()
    {
        var logger = new ListLoggerProvider();
        using var loggerFactory = new LoggerFactory(new[] { logger });
        TypeCoercionHelper.Logger = loggerFactory.CreateLogger("TypeCoercion");

        try
        {
            var columnInfo = new ColumnInfo
            {
                Name = "payload",
                PropertyInfo = typeof(SecurityJsonEntity).GetProperty(nameof(SecurityJsonEntity.Payload))!,
                IsJsonType = true,
                JsonSerializerOptions = JsonSerializerOptions.Default
            };

            var ex = Assert.Throws<JsonException>(() =>
                TypeCoercionHelper.Coerce("{\"secret\":\"hunter2\"", typeof(string), columnInfo));

            Assert.DoesNotContain("hunter2", ex.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(logger.Entries, entry => entry.Message.Contains("hunter2", StringComparison.Ordinal));
        }
        finally
        {
            TypeCoercionHelper.Logger = NullLogger.Instance;
        }
    }

    [Fact]
    public void ConvertWithCache_InvalidValue_DoesNotExposePayloadInException()
    {
        var ex = Assert.Throws<InvalidCastException>(() =>
            TypeCoercionHelper.ConvertWithCache("top-secret-value", typeof(int)));

        Assert.DoesNotContain("top-secret-value", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadAndRegisterProviders_RejectsParentRelativeAssemblyPath()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProviders:test:ProviderName"] = "Test.Provider",
                ["DatabaseProviders:test:FactoryType"] = "Ignored.Factory",
                ["DatabaseProviders:test:AssemblyPath"] = "../outside.dll"
            })
            .Build();

        var loader = new DbProviderLoader(config, NullLogger<DbProviderLoader>.Instance);

        var ex = Assert.Throws<InvalidOperationException>(() => loader.LoadAndRegisterProviders(new ServiceCollection()));
        Assert.Contains("must stay within", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // CORE-015: ResolveAssemblyPath's containment check is purely lexical (Path.GetFullPath on
    // the configured relative path, then a string-prefix check against the base directory). A
    // symlink placed directly under the base directory — a real file that lexically satisfies
    // "starts with the base directory" — can still point at a target outside it. The prior
    // implementation never asked the filesystem to resolve that link, so a config value naming
    // such a symlink sailed through containment and would go on to load whatever the link
    // actually points to, silently defeating the documented "must stay within" guarantee.
    [Fact]
    public void LoadAndRegisterProviders_RejectsSymlinkUnderBaseDirectoryPointingOutside()
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var outsideDirectory = Directory.CreateTempSubdirectory("pengdows-core015-outside-");
        var outsideTarget = Path.Combine(outsideDirectory.FullName, "outside.dll");
        File.WriteAllBytes(outsideTarget, new byte[] { 0x00 });

        var linkName = $"pengdows-core015-escape-{Guid.NewGuid():N}.dll";
        var linkPath = Path.Combine(baseDirectory, linkName);

        try
        {
            File.CreateSymbolicLink(linkPath, outsideTarget);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DatabaseProviders:test:ProviderName"] = "Test.Provider",
                    ["DatabaseProviders:test:FactoryType"] = "Ignored.Factory",
                    ["DatabaseProviders:test:AssemblyPath"] = linkName
                })
                .Build();

            var loader = new DbProviderLoader(config, NullLogger<DbProviderLoader>.Instance);

            var ex = Assert.Throws<InvalidOperationException>(
                () => loader.LoadAndRegisterProviders(new ServiceCollection()));
            Assert.Contains("must stay within", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(linkPath))
            {
                File.Delete(linkPath);
            }

            outsideDirectory.Delete(recursive: true);
        }
    }

    private sealed class SecurityJsonEntity
    {
        public JsonDocument? Payload { get; set; }
    }
}
