namespace pengdows.crud;

public interface ITableInfo
{
    string Schema { get; set; }
    string Name { get; set; }
    Dictionary<string, IColumnInfo> Columns { get; }
    IColumnInfo Id { get; set; }
    IColumnInfo Version { get; set; }
    IColumnInfo LastUpdatedBy { get; set; }
    IColumnInfo LastUpdatedOn { get; set; }
    IColumnInfo CreatedOn { get; set; }
    IColumnInfo CreatedBy { get; set; }
}