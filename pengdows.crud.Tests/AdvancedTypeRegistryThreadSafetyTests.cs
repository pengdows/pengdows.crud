using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.types;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class AdvancedTypeRegistryThreadSafetyTests
{
    private const int ThreadCount = 20;
    private const int IterationsPerThread = 200;

    [Fact]
    public void ConcurrentRegisterMappingAndTryConfigureParameter_NoExceptions()
    {
        var registry = new AdvancedTypeRegistry();
        var exceptions = new List<Exception>();
        var barrier = new Barrier(ThreadCount);

        var threads = new Thread[ThreadCount];
        for (var i = 0; i < ThreadCount; i++)
        {
            var threadIndex = i;
            threads[i] = new Thread(() =>
            {
                try
                {
                    barrier.SignalAndWait();

                    for (var j = 0; j < IterationsPerThread; j++)
                    {
                        // Half the threads register mappings, half configure parameters
                        if (threadIndex % 2 == 0)
                        {
                            registry.RegisterMapping<string>(SupportedDatabase.SqlServer,
                                new ProviderTypeMapping { DbType = DbType.String });
                            registry.RegisterMapping<int>(SupportedDatabase.PostgreSql,
                                new ProviderTypeMapping { DbType = DbType.Int32 });
                        }
                        else
                        {
                            var param = new TestDbParameter();
                            registry.TryConfigureParameter(param, typeof(string), "test",
                                SupportedDatabase.SqlServer);
                            registry.TryConfigureParameter(param, typeof(int), 42,
                                SupportedDatabase.PostgreSql);
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            });
            threads[i].Start();
        }

        foreach (var thread in threads)
        {
            thread.Join(TimeSpan.FromSeconds(10));
        }

        Assert.Empty(exceptions);
    }

    [Fact]
    public void ConcurrentRegisterConverterAndTryConfigureParameter_NoExceptions()
    {
        var registry = new AdvancedTypeRegistry();

        // Pre-register mapping so TryConfigureParameter has something to cache
        registry.RegisterMapping<Inet>(SupportedDatabase.PostgreSql,
            new ProviderTypeMapping { DbType = DbType.String });

        var exceptions = new List<Exception>();
        var barrier = new Barrier(ThreadCount);

        var threads = new Thread[ThreadCount];
        for (var i = 0; i < ThreadCount; i++)
        {
            var threadIndex = i;
            threads[i] = new Thread(() =>
            {
                try
                {
                    barrier.SignalAndWait();

                    for (var j = 0; j < IterationsPerThread; j++)
                    {
                        if (threadIndex % 2 == 0)
                        {
                            // Repeatedly register converters (triggers cache invalidation)
                            registry.RegisterConverter(new InetConverter());
                        }
                        else
                        {
                            // Repeatedly configure parameters (reads from cache)
                            var param = new TestDbParameter();
                            var inet = new Inet(IPAddress.Parse("10.0.0.1"));
                            registry.TryConfigureParameter(param, typeof(Inet), inet,
                                SupportedDatabase.PostgreSql);
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            });
            threads[i].Start();
        }

        foreach (var thread in threads)
        {
            thread.Join(TimeSpan.FromSeconds(10));
        }

        Assert.Empty(exceptions);
    }

    [Fact]
    public void ConcurrentOperations_ProduceCorrectResults()
    {
        var registry = new AdvancedTypeRegistry();
        registry.RegisterMapping<string>(SupportedDatabase.SqlServer,
            new ProviderTypeMapping { DbType = DbType.String });

        var results = new bool[ThreadCount * IterationsPerThread];
        var barrier = new Barrier(ThreadCount);

        var threads = new Thread[ThreadCount];
        for (var i = 0; i < ThreadCount; i++)
        {
            var threadIndex = i;
            threads[i] = new Thread(() =>
            {
                barrier.SignalAndWait();
                for (var j = 0; j < IterationsPerThread; j++)
                {
                    var param = new TestDbParameter();
                    var result = registry.TryConfigureParameter(param, typeof(string),
                        $"value-{threadIndex}-{j}", SupportedDatabase.SqlServer);
                    results[threadIndex * IterationsPerThread + j] = result;
                }
            });
            threads[i].Start();
        }

        foreach (var thread in threads)
        {
            thread.Join(TimeSpan.FromSeconds(10));
        }

        // All results should be true since we registered the mapping
        Assert.All(results, r => Assert.True(r));
    }

    [Fact]
    public void ConcurrentIsMappedType_NoExceptions()
    {
        var registry = new AdvancedTypeRegistry();
        var exceptions = new List<Exception>();
        var barrier = new Barrier(ThreadCount);

        var threads = new Thread[ThreadCount];
        for (var i = 0; i < ThreadCount; i++)
        {
            var threadIndex = i;
            threads[i] = new Thread(() =>
            {
                try
                {
                    barrier.SignalAndWait();
                    for (var j = 0; j < IterationsPerThread; j++)
                    {
                        if (threadIndex % 2 == 0)
                        {
                            registry.RegisterMapping<string>(SupportedDatabase.SqlServer,
                                new ProviderTypeMapping { DbType = DbType.String });
                        }
                        else
                        {
                            registry.IsMappedType(typeof(string));
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            });
            threads[i].Start();
        }

        foreach (var thread in threads)
        {
            thread.Join(TimeSpan.FromSeconds(10));
        }

        Assert.Empty(exceptions);
    }

    [Fact]
    public void ConcurrentGetMapping_NoExceptions()
    {
        var registry = new AdvancedTypeRegistry();
        registry.RegisterMapping<string>(SupportedDatabase.SqlServer,
            new ProviderTypeMapping { DbType = DbType.String });

        var exceptions = new List<Exception>();
        var barrier = new Barrier(ThreadCount);

        var threads = new Thread[ThreadCount];
        for (var i = 0; i < ThreadCount; i++)
        {
            threads[i] = new Thread(() =>
            {
                try
                {
                    barrier.SignalAndWait();
                    for (var j = 0; j < IterationsPerThread; j++)
                    {
                        var mapping = registry.GetMapping(typeof(string), SupportedDatabase.SqlServer);
                        Assert.NotNull(mapping);
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            });
            threads[i].Start();
        }

        foreach (var thread in threads)
        {
            thread.Join(TimeSpan.FromSeconds(10));
        }

        Assert.Empty(exceptions);
    }

    [Fact]
    public void ConcurrentGetConverter_NoExceptions()
    {
        var registry = new AdvancedTypeRegistry();
        registry.RegisterConverter(new InetConverter());

        var exceptions = new List<Exception>();
        var barrier = new Barrier(ThreadCount);

        var threads = new Thread[ThreadCount];
        for (var i = 0; i < ThreadCount; i++)
        {
            threads[i] = new Thread(() =>
            {
                try
                {
                    barrier.SignalAndWait();
                    for (var j = 0; j < IterationsPerThread; j++)
                    {
                        var converter = registry.GetConverter(typeof(Inet));
                        Assert.NotNull(converter);
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            });
            threads[i].Start();
        }

        foreach (var thread in threads)
        {
            thread.Join(TimeSpan.FromSeconds(10));
        }

        Assert.Empty(exceptions);
    }

    [Fact]
    public void ConverterVersionBump_InvalidatesCache()
    {
        var registry = new AdvancedTypeRegistry();
        registry.RegisterMapping<Inet>(SupportedDatabase.PostgreSql,
            new ProviderTypeMapping { DbType = DbType.String });

        var inet = new Inet(IPAddress.Parse("10.0.0.1"));

        // First call - no converter
        var param1 = new TestDbParameter();
        registry.TryConfigureParameter(param1, typeof(Inet), inet, SupportedDatabase.PostgreSql);
        Assert.Equal(inet, param1.Value); // No conversion

        // Register a converter - should bump version and invalidate
        registry.RegisterConverter(new InetConverter());

        // Second call - should pick up the new converter via version check
        var param2 = new TestDbParameter();
        registry.TryConfigureParameter(param2, typeof(Inet), inet, SupportedDatabase.PostgreSql);
        Assert.Equal("10.0.0.1", param2.Value); // Converted
    }

    private class TestDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }

        [AllowNull]
        public override string ParameterName
        {
            get => _parameterName;
            set => _parameterName = value ?? string.Empty;
        }

        private string _parameterName = string.Empty;

        public override int Size { get; set; }

        [AllowNull]
        public override string SourceColumn
        {
            get => _sourceColumn;
            set => _sourceColumn = value ?? string.Empty;
        }

        private string _sourceColumn = string.Empty;

        public override bool SourceColumnNullMapping { get; set; }
        [AllowNull] public override object Value { get; set; } = DBNull.Value;

        public override void ResetDbType()
        {
            DbType = DbType.Object;
        }
    }
}
