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
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.Tests.Logging;
using Xunit;

namespace pengdows.crud.Tests;

// [Collection("TypeRegistry")]: this class assigns TypeCoercionHelper.Logger (a process-global
// static) directly rather than saving/restoring around a scoped try/finally in some tests (see
// ConnectionString_PublicSurface_ReturnsRedactedValue's setup below) — sharing the "TypeRegistry"
// collection with every other test class that touches that same static prevents this from
// racing with them.
[Collection("TypeRegistry")]
public sealed class SecurityRegressionTests
{
    // Threat note (review 2026-09-29): PreventDatabaseUnload rewrites the connection string (raises
    // the provider minimum pool size) after construction and rebuilds the owned data sources; the
    // public, redacted form must be recomputed from the rewritten string and still hide secrets.
    [Fact]
    public void ConnectionString_AfterPreventDatabaseUnloadRebuild_IsStillRedacted()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Database=/data/app.fdb;User=app;Password=super-secret;EmulatedProduct=Firebird",
            DbMode = DbMode.PreventDatabaseUnload
        };

        using var context = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.Firebird));

        Assert.Equal(DbMode.PreventDatabaseUnload, context.ConnectionMode);
        Assert.DoesNotContain("super-secret", context.ConnectionString, StringComparison.Ordinal);
        Assert.Contains("Password=REDACTED", context.ConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MinPoolSize=2", context.ConnectionString.Replace(" ", string.Empty),
            StringComparison.OrdinalIgnoreCase); // the rewritten string, not the original
    }

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

    // Malformed JSON never puts the payload in the exception chain or the logs, on the gateway's
    // [Json] column read and on the typed read scalar reads and DataReaderMapper use.
    [Fact]
    public async System.Threading.Tasks.Task InvalidJson_DoesNotExposePayloadValue()
    {
        var logger = new ListLoggerProvider();
        using var loggerFactory = new LoggerFactory(new[] { logger });
        TypeCoercionHelper.Logger = loggerFactory.CreateLogger("TypeCoercion");
        const string payload = "{\"secret\":\"hunter2\"";

        try
        {
            var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
            factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite });
            var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite };
            exec.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 1, ["payload"] = payload } });
            factory.Connections.Add(exec);
            await using var context = new DatabaseContext(new DatabaseContextConfiguration
            {
                ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
                DbMode = DbMode.Standard
            }, factory, loggerFactory);

            var gatewayFailure = await Assert.ThrowsAnyAsync<Exception>(async () =>
                await new TableGateway<SecurityJsonEntity, int>(context).RetrieveOneAsync(1));
            var typedFailure = Assert.ThrowsAny<Exception>(() =>
                TypeCoercionHelper.Coerce(payload, typeof(string), typeof(JsonDocument)));

            foreach (var failure in new[] { gatewayFailure, typedFailure })
            {
                for (Exception? e = failure; e != null; e = e.InnerException)
                {
                    Assert.DoesNotContain("hunter2", e.Message, StringComparison.Ordinal);
                }
            }

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

    // BP-101 (3.0 CORE-015): ResolveAssemblyPath's containment check is purely lexical. A symlink
    // placed directly under the base directory lexically satisfies "starts with the base
    // directory" but can point at a target outside it; the target must be resolved and re-checked.
    [Fact]
    public void LoadAndRegisterProviders_RejectsSymlinkUnderBaseDirectoryPointingOutside()
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var outsideDirectory = Directory.CreateTempSubdirectory("pengdows-bp101-outside-");
        var outsideTarget = Path.Combine(outsideDirectory.FullName, "outside.dll");
        File.WriteAllBytes(outsideTarget, new byte[] { 0x00 });

        var linkName = $"pengdows-bp101-escape-{Guid.NewGuid():N}.dll";
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

    [pengdows.crud.attributes.Table("secure")]
    public sealed class SecurityJsonEntity
    {
        [pengdows.crud.attributes.Id] [pengdows.crud.attributes.Column("id", System.Data.DbType.Int32)] public int Id { get; set; }

        [pengdows.crud.attributes.Json] [pengdows.crud.attributes.Column("payload", System.Data.DbType.String)]
        public Dictionary<string, string>? Payload { get; set; }
    }
}
