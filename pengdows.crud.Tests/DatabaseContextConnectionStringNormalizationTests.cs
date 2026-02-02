using System;
using System.Data.Common;
using System.Linq;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class DatabaseContextConnectionStringNormalizationTests
{
    [Fact]
    public void NormalizeConnectionString_RestoresSensitiveValues_WhenBuilderStrips()
    {
        var connectionString =
            "EmulatedProduct=SqlServer;Data Source=server;Database=app;User Id=writer;Password=secret;";
        var factory = new CustomBuilderFactory<StrippingConnectionStringBuilder>();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        using var context = new DatabaseContext(config, factory);

        Assert.Contains("Password=secret", context.ConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("User Id=writer", context.ConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalizeConnectionString_UsesBuilderResult_WhenSensitiveValuesPreserved()
    {
        var connectionString =
            "EmulatedProduct=SqlServer;Data Source=server;Database=app;User Id=writer;Password=secret;";
        var factory = new CustomBuilderFactory<DecoratingConnectionStringBuilder>();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        using var context = new DatabaseContext(config, factory);

        Assert.Contains("Normalized=true", context.ConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CustomBuilderFactory<TBuilder> : DbProviderFactory
        where TBuilder : DbConnectionStringBuilder, new()
    {
        public override DbConnection CreateConnection()
        {
            return new fakeDbConnection();
        }

        public override DbConnectionStringBuilder CreateConnectionStringBuilder()
        {
            return new TBuilder();
        }
    }

    private sealed class StrippingConnectionStringBuilder : DbConnectionStringBuilder
    {
        public override string ConnectionString
        {
            get => base.ConnectionString;
            set
            {
                base.ConnectionString = value;
                StripSensitiveKeys();
            }
        }

        private void StripSensitiveKeys()
        {
            var keys = Keys.Cast<object>().Select(key => key?.ToString() ?? string.Empty).ToList();
            foreach (var key in keys)
            {
                if (IsSensitiveKey(key))
                {
                    Remove(key);
                }
            }
        }

        private static bool IsSensitiveKey(string key)
        {
            var lowered = key.ToLowerInvariant();
            return lowered switch
            {
                "password" => true,
                "pwd" => true,
                "user id" => true,
                "uid" => true,
                "user" => true,
                "username" => true,
                _ => lowered.Contains("password", StringComparison.OrdinalIgnoreCase)
                     || lowered.Contains("secret", StringComparison.OrdinalIgnoreCase)
            };
        }
    }

    private sealed class DecoratingConnectionStringBuilder : DbConnectionStringBuilder
    {
        public override string ConnectionString
        {
            get => base.ConnectionString;
            set
            {
                base.ConnectionString = value;
                if (!ContainsKey("Normalized"))
                {
                    this["Normalized"] = "true";
                }
            }
        }
    }
}
