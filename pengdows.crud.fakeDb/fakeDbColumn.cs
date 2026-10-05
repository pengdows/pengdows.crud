namespace pengdows.crud.fakeDb;

/// <summary>
/// A column a <see cref="fakeDbDataReader"/> declares (<see cref="fakeDbDataReader.Columns"/>): its
/// name, the CLR field type the provider reports, and the database's own type name
/// (<see cref="System.Data.Common.DbDataReader.GetDataTypeName"/>), as a real provider reports them
/// even for a result with no rows.
/// </summary>
public sealed record fakeDbColumn(string Name, Type FieldType, string DataTypeName);
