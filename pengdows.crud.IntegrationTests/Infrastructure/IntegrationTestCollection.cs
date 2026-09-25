namespace pengdows.crud.IntegrationTests.Infrastructure;

[CollectionDefinition("IntegrationTests")]
public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
{
}
/// <summary>
/// Test classes that start their own containers (outside <see cref="IntegrationTestFixture"/>).
/// Without a collection, xUnit ran each of them as its own collection in parallel with the fixture's
/// startup, so the full testbed matrix and several standalone containers competed with the fixture's
/// containers - SQL Server then intermittently failed to accept connections within its startup wait.
/// DisableParallelization runs this collection on its own, after the parallel collections finish.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class StandaloneContainerCollection
{
    public const string Name = "StandaloneContainers";
}
