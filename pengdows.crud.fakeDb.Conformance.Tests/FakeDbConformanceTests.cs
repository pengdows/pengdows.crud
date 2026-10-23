using System.Data.Common;
using pengdows.AdoNetApiTest.conformance;
using pengdows.crud.enums;

namespace pengdows.crud.fakeDb.Conformance.Tests;

/// <summary>
/// Runs the provider-neutral ADO.NET conformance suite (pengdows.AdoNetApiTest.conformance) against fakeDb, so the fake
/// provider honors the documented System.Data.Common contract that the real providers it stands in for are held to.
/// </summary>
public sealed class FakeDbFactoryFixture : IDbFactoryFixture
{
    public FakeDbFactoryFixture()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite) { EnableDataPersistence = true };
        // The fake database answers SQL itself and refuses text that is not SQL, as a real one does.
        factory.DataStore.StrictMode = true;
        Factory = factory;
    }

    public DbProviderFactory Factory { get; }
    public string ConnectionString => "Data Source=conformance;EmulatedProduct=Sqlite";
}

public sealed class FakeDbCommandTests(FakeDbFactoryFixture f) : CommandTestBase<FakeDbFactoryFixture>(f);
public sealed class FakeDbConnectionTests(FakeDbFactoryFixture f) : ConnectionTestBase<FakeDbFactoryFixture>(f);
public sealed class FakeDbConnectionStringBuilderTests(FakeDbFactoryFixture f) : ConnectionStringTestBase<FakeDbFactoryFixture>(f);
#if NET10_0_OR_GREATER
// The package ships DbDataSourceTestBase only in its net10.0 build (DbDataSource is not in netstandard2.1).
public sealed class FakeDbDataSourceTests(FakeDbFactoryFixture f) : DbDataSourceTestBase<FakeDbFactoryFixture>(f);
#endif
public sealed class FakeDbDataTypesFidelityTests(FakeDbFactoryFixture f) : DataTypesFidelityTestBase<FakeDbFactoryFixture>(f);
public sealed class FakeDbProviderFactoryTests(FakeDbFactoryFixture f) : DbProviderFactoryTestBase<FakeDbFactoryFixture>(f);
public sealed class FakeDbParameterTests(FakeDbFactoryFixture f) : ParameterTestBase<FakeDbFactoryFixture>(f);
public sealed class FakeDbTransactionTests(FakeDbFactoryFixture f) : TransactionTestBase<FakeDbFactoryFixture>(f);
