namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// The shape every fidelity entity (TypeFidelityCells.cs, generated) has: the row's id, the columns it
/// maps (the column name, the property, and the probe's semantic type for it), and the value the test writes.
/// </summary>
public interface IFidelityRow<TSelf> where TSelf : class, IFidelityRow<TSelf>, new()
{
    int Id { get; set; }

    static abstract IReadOnlyList<(string Column, string Property, string Semantic)> Columns { get; }

    static abstract TSelf Expected();
}
