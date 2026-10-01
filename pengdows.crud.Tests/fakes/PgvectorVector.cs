namespace Pgvector;

/// <summary>
/// Stands in for Pgvector.Vector (the Pgvector.Npgsql plugin's read type), recognized by its full
/// name and ToArray(), without a package dependency (TYPE-015).
/// </summary>
internal sealed class Vector
{
    private readonly float[] _values;
    public Vector(float[] values) => _values = values;
    public float[] ToArray() => (float[])_values.Clone();
}
