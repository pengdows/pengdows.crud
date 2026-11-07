using System;
using pengdows.crud.@internal;
using Xunit;

namespace NpgsqlTypes
{
    public struct NpgsqlInet { }
    public struct NpgsqlCidr { }
}

namespace pengdows.crud.Tests.ProviderTypeFakes
{
    public struct SqlHierarchyId { }
    public struct SqlGeometry { }
    public struct SqlGeography { }
}

namespace pengdows.crud.Tests
{
    /// <summary>
    /// The provider types the read path recognises by name (their libraries are optional dependencies, so they are not
    /// referenced). Each predicate matches exactly the type its name denotes and nothing near it.
    /// </summary>
    public class ProviderTypeNamesTests
    {
        [Fact]
        public void TheNpgsqlTypesAreRecognisedByTheirFullNames()
        {
            Assert.True(ProviderTypeNames.IsNpgsqlInterval(typeof(NpgsqlTypes.NpgsqlInterval)));
            Assert.True(ProviderTypeNames.IsNpgsqlInet(typeof(NpgsqlTypes.NpgsqlInet)));
            Assert.True(ProviderTypeNames.IsNpgsqlCidr(typeof(NpgsqlTypes.NpgsqlCidr)));
            Assert.True(ProviderTypeNames.IsNpgsqlRange(typeof(NpgsqlTypes.NpgsqlRange<int>)));
            Assert.True(ProviderTypeNames.IsNpgsqlRangeDefinition(typeof(NpgsqlTypes.NpgsqlRange<>)));
        }

        [Fact]
        public void ANameThatOnlyLooksLikeAProviderTypeIsNotOne()
        {
            Assert.False(ProviderTypeNames.IsNpgsqlInterval(typeof(TimeSpan)));
            Assert.False(ProviderTypeNames.IsNpgsqlInet(typeof(NpgsqlTypes.NpgsqlInterval)));
            Assert.False(ProviderTypeNames.IsNpgsqlRangeDefinition(typeof(NpgsqlTypes.NpgsqlInterval)));
        }

        [Fact]
        public void TheSqlServerHierarchyIdIsRecognisedByItsName()
        {
            Assert.True(ProviderTypeNames.IsSqlHierarchyId(typeof(pengdows.crud.Tests.ProviderTypeFakes.SqlHierarchyId)));
            Assert.False(ProviderTypeNames.IsSqlHierarchyId(typeof(string)));
        }

        [Fact]
        public void TheDuckDbReaderAndTheFirebirdZonedTypesAreRecognisedByName()
        {
            Assert.True(ProviderTypeNames.IsDuckDbReaderTypeName("DuckDB.NET.Data.DuckDBDataReader"));
            Assert.False(ProviderTypeNames.IsDuckDbReaderTypeName("Npgsql.NpgsqlDataReader"));
            Assert.True(ProviderTypeNames.IsFirebirdZonedDateTime(typeof(FirebirdSql.Data.Types.FbZonedDateTime)));
            Assert.False(ProviderTypeNames.IsFirebirdZonedTime(typeof(FirebirdSql.Data.Types.FbZonedDateTime)));
            Assert.True(ProviderTypeNames.IsFirebirdZonedTime(typeof(FirebirdSql.Data.Types.FbZonedTime)));
        }

        [Fact]
        public void TheSqlServerSpatialTypesAreRecognisedByTheirName()
        {
            Assert.True(ProviderTypeNames.IsSqlSpatial(typeof(pengdows.crud.Tests.ProviderTypeFakes.SqlGeometry)));
            Assert.True(ProviderTypeNames.IsSqlSpatial(typeof(pengdows.crud.Tests.ProviderTypeFakes.SqlGeography)));
            Assert.False(ProviderTypeNames.IsSqlSpatial(typeof(string)));
        }
    }
}

