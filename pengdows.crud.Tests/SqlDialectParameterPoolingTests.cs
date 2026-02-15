using System;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlDialectParameterPoolingTests
{
    [Fact]
    public void CreateDbParameter_ReusedParametersClearProviderSpecificMetadata()
    {
        var dialect = new TestDialect(new FakeProviderFactory());

        var first = dialect.CreateDbParameter("p0", DbType.Guid, Guid.NewGuid());
        var firstProvider = Assert.IsType<FakeProviderParameter>(first);
        Assert.Equal(27, firstProvider.NpgsqlDbType);
        Assert.Equal("uuid", firstProvider.DataTypeName);

        dialect.ReturnParameterToPool(first);

        var second = dialect.CreateDbParameter("p1", DbType.DateTime, DateTime.UtcNow);
        var secondProvider = Assert.IsType<FakeProviderParameter>(second);

        Assert.Same(first, second);
        Assert.Equal(0, secondProvider.NpgsqlDbType);
        Assert.Null(secondProvider.DataTypeName);
    }

    /// <summary>
    /// Regression test: Npgsql tracks whether NpgsqlDbType was explicitly set.
    /// Setting NpgsqlDbType=0 via reflection marks it as "explicitly set", causing
    /// Npgsql to try resolving NpgsqlDbType=0 which throws ArgumentOutOfRangeException.
    /// ResetDbType() must be called AFTER reflection-based reset to clear that flag.
    /// </summary>
    [Fact]
    public void CreateDbParameter_ResetDbTypeClearsExplicitlySetFlag_AfterReflectionReset()
    {
        var dialect = new TestDialect(new NpgsqlLikeProviderFactory());

        // Create a parameter that simulates Npgsql's Guid handling
        var first = dialect.CreateDbParameter("p0", DbType.Guid, Guid.NewGuid());
        var firstProvider = Assert.IsType<NpgsqlLikeParameter>(first);
        Assert.True(firstProvider.NpgsqlDbTypeExplicitlySet,
            "NpgsqlDbType should be marked as explicitly set after Guid parameter creation");

        dialect.ReturnParameterToPool(first);

        // Re-rent the parameter for a non-Guid type
        var second = dialect.CreateDbParameter("p1", DbType.DateTime, DateTime.UtcNow);
        var secondProvider = Assert.IsType<NpgsqlLikeParameter>(second);

        Assert.Same(first, second);
        // The critical assertion: ResetDbType() must have cleared the "explicitly set" flag
        // AFTER ResetProviderSpecificMetadata set NpgsqlDbType=0 via reflection.
        // If the order were reversed, this flag would still be true and Npgsql would throw.
        Assert.False(secondProvider.NpgsqlDbTypeExplicitlySet,
            "ResetDbType() must clear the explicitly-set flag after reflection-based reset");
    }

    private sealed class TestDialect : SqlDialect
    {
        public TestDialect(DbProviderFactory factory)
            : base(factory, NullLogger<SqlDialect>.Instance)
        {
        }

        public override SupportedDatabase DatabaseType => SupportedDatabase.PostgreSql;

        public override DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
        {
            var parameter = base.CreateDbParameter(name, type, value);

            if (parameter is FakeProviderParameter providerParam && type == DbType.Guid)
            {
                providerParam.NpgsqlDbType = 27;
                providerParam.DataTypeName = "uuid";
            }
            else if (parameter is NpgsqlLikeParameter npgsqlParam && type == DbType.Guid)
            {
                npgsqlParam.NpgsqlDbType = 27;
            }

            return parameter;
        }
    }

    private sealed class FakeProviderFactory : DbProviderFactory
    {
        public override DbParameter CreateParameter()
        {
            return new FakeProviderParameter();
        }
    }

    private sealed class FakeProviderParameter : fakeDbParameter
    {
        public int NpgsqlDbType { get; set; }
        public string? DataTypeName { get; set; }
    }

    private sealed class NpgsqlLikeProviderFactory : DbProviderFactory
    {
        public override DbParameter CreateParameter()
        {
            return new NpgsqlLikeParameter();
        }
    }

    /// <summary>
    /// Simulates Npgsql's internal behavior: setting NpgsqlDbType marks it as "explicitly set",
    /// and ResetDbType() clears that flag. This is what causes the real-world
    /// ArgumentOutOfRangeException when the order is wrong.
    /// </summary>
    private sealed class NpgsqlLikeParameter : fakeDbParameter
    {
        private int _npgsqlDbType;

        public bool NpgsqlDbTypeExplicitlySet { get; private set; }

        public int NpgsqlDbType
        {
            get => _npgsqlDbType;
            set
            {
                _npgsqlDbType = value;
                NpgsqlDbTypeExplicitlySet = true;
            }
        }

        public override void ResetDbType()
        {
            base.ResetDbType();
            _npgsqlDbType = 0;
            NpgsqlDbTypeExplicitlySet = false;
        }
    }
}
