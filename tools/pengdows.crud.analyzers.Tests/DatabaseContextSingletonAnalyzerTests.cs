using Xunit;

namespace pengdows.crud.analyzers.Tests;

public sealed class DatabaseContextSingletonAnalyzerTests
{
    [Fact]
    public async Task ScopedDatabaseContextRegistration_ProducesDiagnostic()
    {
        var source = """
            using System;

            namespace Microsoft.Extensions.DependencyInjection
            {
                public interface IServiceCollection
                {
                }

                public static class ServiceCollectionServiceExtensions
                {
                    public static IServiceCollection AddScoped<TService>(this IServiceCollection services, Func<IServiceProvider, TService> factory)
                        where TService : class
                    {
                        throw new NotImplementedException();
                    }
                }
            }

            namespace Sample;

            public interface IDatabaseContext
            {
            }

            public sealed class DatabaseContext : IDatabaseContext
            {
            }

            public static class Setup
            {
                public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
                    services.AddScoped<IDatabaseContext>(_ => new DatabaseContext());
                }
            }
            """;

        await CSharpAnalyzerVerifier<DatabaseContextSingletonAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            DatabaseContextSingletonAnalyzer.DiagnosticId,
            expectedCount: 1);
    }

    [Fact]
    public async Task TransientDatabaseContextTypeRegistration_ProducesDiagnostic()
    {
        var source = """
            using System;

            namespace Microsoft.Extensions.DependencyInjection
            {
                public interface IServiceCollection
                {
                }

                public static class ServiceCollectionServiceExtensions
                {
                    public static IServiceCollection AddTransient(this IServiceCollection services, Type serviceType, Type implementationType)
                    {
                        throw new NotImplementedException();
                    }
                }
            }

            namespace Sample;

            public interface IDatabaseContext
            {
            }

            public sealed class DatabaseContext : IDatabaseContext
            {
            }

            public static class Setup
            {
                public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
                    services.AddTransient(typeof(IDatabaseContext), typeof(DatabaseContext));
                }
            }
            """;

        await CSharpAnalyzerVerifier<DatabaseContextSingletonAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            DatabaseContextSingletonAnalyzer.DiagnosticId,
            expectedCount: 1);
    }

    [Fact]
    public async Task ScopedITableGatewayRegistration_ProducesDiagnostic()
    {
        var source = """
            using System;

            namespace Microsoft.Extensions.DependencyInjection
            {
                public interface IServiceCollection
                {
                }

                public static class ServiceCollectionServiceExtensions
                {
                    public static IServiceCollection AddScoped<TService>(this IServiceCollection services, Func<IServiceProvider, TService> factory)
                        where TService : class
                    {
                        throw new NotImplementedException();
                    }
                }
            }

            namespace Sample;

            public interface ITableGateway<TEntity, TId>
            {
            }

            public sealed class Provider
            {
            }

            public static class Setup
            {
                public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
                    services.AddScoped<ITableGateway<Provider, System.Guid>>(_ => throw new System.NotImplementedException());
                }
            }
            """;

        await CSharpAnalyzerVerifier<DatabaseContextSingletonAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            DatabaseContextSingletonAnalyzer.DiagnosticId,
            expectedCount: 1);
    }

    [Fact]
    public async Task ScopedTableGatewayConcreteRegistration_ProducesDiagnostic()
    {
        var source = """
            using System;

            namespace Microsoft.Extensions.DependencyInjection
            {
                public interface IServiceCollection
                {
                }

                public static class ServiceCollectionServiceExtensions
                {
                    public static IServiceCollection AddScoped<TService>(this IServiceCollection services, Func<IServiceProvider, TService> factory)
                        where TService : class
                    {
                        throw new NotImplementedException();
                    }
                }
            }

            namespace Sample;

            public class TableGateway<TEntity, TId>
            {
            }

            public sealed class Provider
            {
            }

            public static class Setup
            {
                public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
                    services.AddScoped<TableGateway<Provider, System.Guid>>(_ => throw new System.NotImplementedException());
                }
            }
            """;

        await CSharpAnalyzerVerifier<DatabaseContextSingletonAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            DatabaseContextSingletonAnalyzer.DiagnosticId,
            expectedCount: 1);
    }

    [Fact]
    public async Task ScopedIPrimaryKeyTableGatewayRegistration_ProducesDiagnostic()
    {
        var source = """
            using System;

            namespace Microsoft.Extensions.DependencyInjection
            {
                public interface IServiceCollection
                {
                }

                public static class ServiceCollectionServiceExtensions
                {
                    public static IServiceCollection AddScoped<TService>(this IServiceCollection services, Func<IServiceProvider, TService> factory)
                        where TService : class
                    {
                        throw new NotImplementedException();
                    }
                }
            }

            namespace Sample;

            public interface IPrimaryKeyTableGateway<TEntity>
            {
            }

            public sealed class OrderItem
            {
            }

            public static class Setup
            {
                public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
                    services.AddScoped<IPrimaryKeyTableGateway<OrderItem>>(_ => throw new System.NotImplementedException());
                }
            }
            """;

        await CSharpAnalyzerVerifier<DatabaseContextSingletonAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            DatabaseContextSingletonAnalyzer.DiagnosticId,
            expectedCount: 1);
    }

    [Fact]
    public async Task SingletonITableGatewayRegistration_DoesNotProduceDiagnostic()
    {
        var source = """
            using System;

            namespace Microsoft.Extensions.DependencyInjection
            {
                public interface IServiceCollection
                {
                }

                public static class ServiceCollectionServiceExtensions
                {
                    public static IServiceCollection AddSingleton<TService>(this IServiceCollection services, Func<IServiceProvider, TService> factory)
                        where TService : class
                    {
                        throw new NotImplementedException();
                    }
                }
            }

            namespace Sample;

            public interface ITableGateway<TEntity, TId>
            {
            }

            public sealed class Provider
            {
            }

            public static class Setup
            {
                public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
                    services.AddSingleton<ITableGateway<Provider, System.Guid>>(_ => throw new System.NotImplementedException());
                }
            }
            """;

        await CSharpAnalyzerVerifier<DatabaseContextSingletonAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            DatabaseContextSingletonAnalyzer.DiagnosticId,
            expectedCount: 0);
    }

    [Fact]
    public async Task SingletonDatabaseContextRegistration_DoesNotProduceDiagnostic()
    {
        var source = """
            using System;

            namespace Microsoft.Extensions.DependencyInjection
            {
                public interface IServiceCollection
                {
                }

                public static class ServiceCollectionServiceExtensions
                {
                    public static IServiceCollection AddSingleton<TService>(this IServiceCollection services, Func<IServiceProvider, TService> factory)
                        where TService : class
                    {
                        throw new NotImplementedException();
                    }
                }
            }

            namespace Sample;

            public interface IDatabaseContext
            {
            }

            public sealed class DatabaseContext : IDatabaseContext
            {
            }

            public static class Setup
            {
                public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
                    services.AddSingleton<IDatabaseContext>(_ => new DatabaseContext());
                }
            }
            """;

        await CSharpAnalyzerVerifier<DatabaseContextSingletonAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            DatabaseContextSingletonAnalyzer.DiagnosticId,
            expectedCount: 0);
    }
}
