using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.tenant;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class CoverageQuickWinsAdditionalTests
{
    [Fact]
    public void JsonValue_ImplicitOperators_AsElementFastPath_AndToString_AreCovered()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("{\"x\":1}");
        JsonValue fromString = "{\"x\":1}";
        JsonValue fromDocument = doc;
        JsonValue fromElement = doc.RootElement;

        var element = fromElement.AsElement();
        string text = fromString;

        Assert.Equal("x", element.EnumerateObject().First().Name);
        Assert.Equal("{\"x\":1}", text);
        Assert.Equal("{\"x\":1}", fromDocument.ToString());
    }

    [Fact]
    public void IntervalDaySecondConverter_AdditionalProviderBranches_AreCovered()
    {
        var converter = new IntervalDaySecondConverter();
        var value = new IntervalDaySecond(2, TimeSpan.FromHours(3));

        var sameSuccess = converter.TryConvertFromProvider(value, SupportedDatabase.Sqlite, out var sameResult);
        Assert.True(sameSuccess);
        Assert.Equal(value, sameResult);

        var unknownSuccess = converter.TryConvertFromProvider(new object(), SupportedDatabase.Sqlite, out _);
        Assert.False(unknownSuccess);

        var whitespaceSuccess = converter.TryConvertFromProvider("   ", SupportedDatabase.Sqlite, out var zeroResult);
        Assert.True(whitespaceSuccess);
        Assert.Equal(0, zeroResult.Days);
        Assert.Equal(TimeSpan.Zero, zeroResult.Time);

        var overflowSuccess = converter.TryConvertFromProvider("P999999999999999999999D", SupportedDatabase.Sqlite,
            out _);
        Assert.False(overflowSuccess);
    }

    [Fact]
    public void TenantContextRegistry_CatchesDisposeFailures_OnInvalidateAndShutdown()
    {
        var context = new Mock<IDatabaseContext>();
        context.Setup(c => c.Dispose()).Throws(new InvalidOperationException("dispose-fail"));
        context.Setup(c => c.DisposeAsync()).Returns(ValueTask.CompletedTask);

        using var services = BuildTenantServices();

        var registryForInvalidate = new TenantContextRegistry(
            services,
            new StaticTenantResolver(),
            new FixedContextFactory(context.Object),
            NullLoggerFactory.Instance);

        _ = registryForInvalidate.GetContext("tenant-a");
        var invalidateException = Record.Exception(() => registryForInvalidate.Invalidate("tenant-a"));
        Assert.Null(invalidateException);

        var registryForDispose = new TenantContextRegistry(
            services,
            new StaticTenantResolver(),
            new FixedContextFactory(context.Object),
            NullLoggerFactory.Instance);

        _ = registryForDispose.GetContext("tenant-b");
        var disposeException = Record.Exception(() => registryForDispose.Dispose());
        Assert.Null(disposeException);
    }

    [Fact]
    public void DbProviderLoader_AssemblyLoadFromBadImage_ThrowsWrappedInvalidOperationException()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var fileName = "bad-image-" + Guid.NewGuid().ToString("N") + ".dll";
        var fullPath = Path.Combine(baseDir, fileName);
        File.WriteAllText(fullPath, "this is not a managed assembly");

        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DatabaseProviders:badimage:ProviderName"] = "Bad.Image.Provider",
                    ["DatabaseProviders:badimage:FactoryType"] = "Bad.Factory.Type",
                    ["DatabaseProviders:badimage:AssemblyPath"] = fileName
                })
                .Build();

            var loader = new DbProviderLoader(config, NullLogger<DbProviderLoader>.Instance);

            Assert.Throws<InvalidOperationException>(() => loader.LoadAndRegisterProviders(new ServiceCollection()));
        }
        finally
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
    }

    [Fact]
    public void Uuid7Optimized_PrivateBranches_AreExercisedViaReflection()
    {
        Uuid7Optimized.Configure(new Uuid7Options(Uuid7ClockMode.NtpSynced));

        var uuidType = typeof(Uuid7Optimized);
        var tlsType = uuidType.GetNestedType("V7ThreadState", BindingFlags.NonPublic)
                      ?? throw new InvalidOperationException("V7ThreadState type not found.");

        var randAMask = Convert.ToUInt16(uuidType.GetField("RandAMask", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetRawConstantValue() ?? throw new InvalidOperationException("RandAMask not found."));
        var randBMask = Convert.ToUInt64(uuidType.GetField("RandBMask", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetRawConstantValue() ?? throw new InvalidOperationException("RandBMask not found."));

        var increment = uuidType.GetMethod("IncrementRandState", BindingFlags.NonPublic | BindingFlags.Static)
                        ?? throw new InvalidOperationException("IncrementRandState not found.");
        var interlockedMax = uuidType.GetMethod("InterlockedMax", BindingFlags.NonPublic | BindingFlags.Static)
                           ?? throw new InvalidOperationException("InterlockedMax not found.");
        var handleClockDrift = uuidType.GetMethod("HandleClockDrift", BindingFlags.NonPublic | BindingFlags.Static)
                             ?? throw new InvalidOperationException("HandleClockDrift not found.");

        var state = Activator.CreateInstance(tlsType, nonPublic: true)
                    ?? throw new InvalidOperationException("Could not create V7ThreadState.");
        var randAField = tlsType.GetField("RandA") ?? throw new InvalidOperationException("RandA field not found.");
        var randBField = tlsType.GetField("RandB") ?? throw new InvalidOperationException("RandB field not found.");

        randAField.SetValue(state, (ushort)(randAMask - 1));
        randBField.SetValue(state, randBMask);
        var wrapped = (bool)increment.Invoke(null, new[] { state })!;
        Assert.True(wrapped);
        Assert.Equal(randAMask, (ushort)randAField.GetValue(state)!);
        Assert.Equal(0UL, (ulong)randBField.GetValue(state)!);

        randAField.SetValue(state, randAMask);
        randBField.SetValue(state, randBMask);
        var exhausted = (bool)increment.Invoke(null, new[] { state })!;
        Assert.False(exhausted);

        var noUpdateArgs = new object[] { 10L, 5L };
        interlockedMax.Invoke(null, noUpdateArgs);
        Assert.Equal(10L, (long)noUpdateArgs[0]);

        var pinned = (long)handleClockDrift.Invoke(null, new object[] { 95L, 100L, 90L })!;
        Assert.Equal(100L, pinned);

        var threadStateField = uuidType.GetField("_threadState", BindingFlags.NonPublic | BindingFlags.Static)
                               ?? throw new InvalidOperationException("_threadState field not found.");
        var threadLocal = threadStateField.GetValue(null)
                          ?? throw new InvalidOperationException("ThreadLocal state missing.");
        var currentState = threadLocal.GetType().GetProperty("Value")?.GetValue(threadLocal)
                           ?? throw new InvalidOperationException("ThreadLocal.Value missing.");

        var lastMsField = tlsType.GetField("LastMs") ?? throw new InvalidOperationException("LastMs field missing.");
        var counterField = tlsType.GetField("Counter")
                           ?? throw new InvalidOperationException("Counter field missing.");

        var originalLastMs = (long)lastMsField.GetValue(currentState)!;
        var originalCounter = (int)counterField.GetValue(currentState)!;
        var originalRandA = (ushort)randAField.GetValue(currentState)!;
        var originalRandB = (ulong)randBField.GetValue(currentState)!;

        try
        {
            lastMsField.SetValue(currentState, long.MaxValue);
            counterField.SetValue(currentState, 0);
            randAField.SetValue(currentState, randAMask);
            randBField.SetValue(currentState, randBMask);

            _ = Uuid7Optimized.NewUuid7();
            Assert.Equal(4096, (int)counterField.GetValue(currentState)!);

            lastMsField.SetValue(currentState, long.MaxValue);
            counterField.SetValue(currentState, 0);
            randAField.SetValue(currentState, randAMask);
            randBField.SetValue(currentState, randBMask);

            var success = Uuid7Optimized.TryNewUuid7(out _);
            Assert.True(success);
            Assert.Equal(4096, (int)counterField.GetValue(currentState)!);
        }
        finally
        {
            lastMsField.SetValue(currentState, originalLastMs);
            counterField.SetValue(currentState, originalCounter);
            randAField.SetValue(currentState, originalRandA);
            randBField.SetValue(currentState, originalRandB);
        }
    }

    private static ServiceProvider BuildTenantServices()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<DbProviderFactory>("fake", new fakeDbFactory(SupportedDatabase.Sqlite));
        return services.BuildServiceProvider();
    }

    private sealed class StaticTenantResolver : ITenantConnectionResolver
    {
        public IDatabaseContextConfiguration GetDatabaseContextConfiguration(string tenant)
        {
            return new DatabaseContextConfiguration
            {
                ConnectionString = "Data Source=:memory:",
                ProviderName = "fake"
            };
        }
    }

    private sealed class FixedContextFactory : IDatabaseContextFactory
    {
        private readonly IDatabaseContext _context;

        public FixedContextFactory(IDatabaseContext context)
        {
            _context = context;
        }

        public IDatabaseContext Create(
            IDatabaseContextConfiguration configuration,
            DbProviderFactory factory,
            Microsoft.Extensions.Logging.ILoggerFactory loggerFactory)
        {
            return _context;
        }
    }
}
