namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// The shape every fidelity entity (TypeFidelityCells.cs, generated) has: the row's id, the columns it
/// maps (the column name, the property, and the probe's semantic type for it), the value the test writes,
/// and the edge values (the limits and the awkward members of the type) the test writes and reads back too.
/// Edges are numbered from id 2; <see cref="Expected"/> is id 1.
/// </summary>
public interface IFidelityRow<TSelf> where TSelf : class, IFidelityRow<TSelf>, new()
{
    int Id { get; set; }

    static abstract IReadOnlyList<(string Column, string Property, string Semantic)> Columns { get; }

    static abstract TSelf Expected();

    static abstract IReadOnlyList<TSelf> Edges();
}
