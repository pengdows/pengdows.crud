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

    private sealed class SecurityJsonEntity
    {
        public JsonDocument? Payload { get; set; }
    }
}
