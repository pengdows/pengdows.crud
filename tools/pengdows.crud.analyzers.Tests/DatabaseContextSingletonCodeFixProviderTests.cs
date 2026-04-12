using Xunit;

namespace pengdows.crud.analyzers.Tests;

public sealed class DatabaseContextSingletonCodeFixProviderTests
{
    private const string ServiceCollectionScaffold = """
        using System;

        namespace Microsoft.Extensions.DependencyInjection
        {
            public interface IServiceCollection { }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddScoped<T>(this IServiceCollection services) => services;
                public static IServiceCollection AddScoped(this IServiceCollection services, Type serviceType) => services;
                public static IServiceCollection AddTransient<T>(this IServiceCollection services) => services;
                public static IServiceCollection AddTransient(this IServiceCollection services, Type serviceType) => services;
                public static IServiceCollection AddSingleton<T>(this IServiceCollection services) => services;
                public static IServiceCollection AddSingleton(this IServiceCollection services, Type serviceType) => services;
            }
        }

        """;

    [Fact]
    public async Task AddScoped_FixReplacesWithAddSingleton()
    {
        var source = ServiceCollectionScaffold + """
            using Microsoft.Extensions.DependencyInjection;

            namespace Sample;

            public class DatabaseContext { }

            public static class Setup
            {
                public static void Register(IServiceCollection services)
                {
                    services.AddScoped<DatabaseContext>();
                }
            }
            """;

        var expected = ServiceCollectionScaffold + """
            using Microsoft.Extensions.DependencyInjection;

            namespace Sample;

            public class DatabaseContext { }

            public static class Setup
            {
                public static void Register(IServiceCollection services)
                {
                    services.AddSingleton<DatabaseContext>();
                }
            }
            """;

        await CSharpCodeFixVerifier<DatabaseContextSingletonAnalyzer, DatabaseContextSingletonCodeFixProvider>
            .VerifyFixAsync(source, expected);
    }

    [Fact]
    public async Task AddTransient_FixReplacesWithAddSingleton()
    {
        var source = ServiceCollectionScaffold + """
            using Microsoft.Extensions.DependencyInjection;

            namespace Sample;

            public class DatabaseContext { }

            public static class Setup
            {
                public static void Register(IServiceCollection services)
                {
                    services.AddTransient<DatabaseContext>();
                }
            }
            """;

        var expected = ServiceCollectionScaffold + """
            using Microsoft.Extensions.DependencyInjection;

            namespace Sample;

            public class DatabaseContext { }

            public static class Setup
            {
                public static void Register(IServiceCollection services)
                {
                    services.AddSingleton<DatabaseContext>();
                }
            }
            """;

        await CSharpCodeFixVerifier<DatabaseContextSingletonAnalyzer, DatabaseContextSingletonCodeFixProvider>
            .VerifyFixAsync(source, expected);
    }

    [Fact]
    public async Task AddScopedWithTypeofArgument_FixPreservesTypeArgument()
    {
        var source = ServiceCollectionScaffold + """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            namespace Sample;

            public class DatabaseContext { }

            public static class Setup
            {
                public static void Register(IServiceCollection services)
                {
                    services.AddScoped(typeof(DatabaseContext));
                }
            }
            """;

        var expected = ServiceCollectionScaffold + """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            namespace Sample;

            public class DatabaseContext { }

            public static class Setup
            {
                public static void Register(IServiceCollection services)
                {
                    services.AddSingleton(typeof(DatabaseContext));
                }
            }
            """;

        await CSharpCodeFixVerifier<DatabaseContextSingletonAnalyzer, DatabaseContextSingletonCodeFixProvider>
            .VerifyFixAsync(source, expected);
    }
}
