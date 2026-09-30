using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.isolation;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// VAR-001 (documented 2026-09-30 as "detection labels that behave exactly like MySql/PostgreSql"):
/// AuroraMySql/AuroraPostgreSql use the MySQL/PostgreSQL dialect classes, but DatabaseType reports
/// the Aurora value, and several places keyed on MySql/PostgreSql excluded it: the advanced-type
/// registry (no JSON/array/range/inet/... mappings on Aurora PostgreSQL), the coercion provider used
/// on read, the isolation-level tables, and HasSessionScopedLastIdFunction. Each now behaves as the
/// engine Aurora runs.
/// </summary>
public sealed class AuroraBehavesAsItsEngineTests
{
    public static IEnumerable<object[]> Pairs()
    {
        yield return new object[] { SupportedDatabase.AuroraPostgreSql, SupportedDatabase.PostgreSql };
        yield return new object[] { SupportedDatabase.AuroraMySql, SupportedDatabase.MySql };
    }

    private static SqlDialect Dialect(SupportedDatabase database) =>
        (SqlDialect)SqlDialectFactory.CreateDialectForType(database, new fakeDbFactory(database),
            NullLogger<SqlDialect>.Instance);

    [Theory]
    [MemberData(nameof(Pairs))]
    public void TypeMappingProvider_IsTheEngine(SupportedDatabase aurora, SupportedDatabase engine)
    {
        Assert.Equal(aurora, Dialect(aurora).DatabaseType);
        Assert.Equal(engine, Dialect(aurora).TypeMappingProvider);
    }

    [Fact]
    public void TypeMappingProvider_IsTheDatabaseTypeEverywhereElse()
    {
        foreach (var database in Enum.GetValues<SupportedDatabase>())
        {
            if (database is SupportedDatabase.Unknown or SupportedDatabase.AuroraMySql or SupportedDatabase.AuroraPostgreSql)
            {
                continue;
            }

            var dialect = Dialect(database);
            Assert.Equal(dialect.DatabaseType, dialect.TypeMappingProvider);
        }
    }

    [Fact]
    public void AuroraPostgreSql_BindsAdvancedTypesLikePostgreSql()
    {
        var inet = Inet.Parse("10.0.0.1");
        var ints = new[] { 1, 2, 3 };

        foreach (var value in new object[] { inet, ints })
        {
            var aurora = Dialect(SupportedDatabase.AuroraPostgreSql).CreateDbParameter("p", DbType.Object, value);
            var engine = Dialect(SupportedDatabase.PostgreSql).CreateDbParameter("p", DbType.Object, value);

            Assert.Equal(engine.DbType, aurora.DbType);
            Assert.Equal(engine.Value?.GetType(), aurora.Value?.GetType());
            Assert.Equal(engine.Value?.ToString(), aurora.Value?.ToString());
        }
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void ReadCoercion_UsesTheEngineAsProvider(SupportedDatabase aurora, SupportedDatabase engine)
    {
        var factory = new fakeDbFactory(aurora);
        using var context = new DatabaseContext("Server=x;Database=y;EmulatedProduct=" + aurora, factory,
            new TypeMapRegistry(), Dialect(aurora));
        using var container = context.CreateSqlContainer("SELECT 1");
        var options = typeof(SqlContainer).GetProperty("DefaultCoercionOptions",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(container)!;

        Assert.Equal(engine, options.GetType().GetProperty("Provider")!.GetValue(options));
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void IsolationLevels_AreTheEngines(SupportedDatabase aurora, SupportedDatabase engine)
    {
        var auroraResolver = new IsolationResolver(Dialect(aurora), false, false);
        var engineResolver = new IsolationResolver(Dialect(engine), false, false);

        Assert.Equal(engineResolver.GetSupportedLevels().OrderBy(l => l), auroraResolver.GetSupportedLevels().OrderBy(l => l));
        foreach (var profile in Enum.GetValues<IsolationProfile>())
        {
            Assert.Equal(engineResolver.Resolve(profile), auroraResolver.Resolve(profile));
        }
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void SessionScopedLastIdFunction_IsTheEngines(SupportedDatabase aurora, SupportedDatabase engine)
    {
        Assert.Equal(Dialect(engine).HasSessionScopedLastIdFunction(), Dialect(aurora).HasSessionScopedLastIdFunction());
    }
}
