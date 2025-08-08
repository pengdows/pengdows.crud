#region

using System.Data;
using System.Reflection;
using System.Text.Json;

#endregion

namespace pengdows.crud;

public class ColumnInfo : IColumnInfo
{
    public Type? EnumType { get; set; }
    public string Name { get; init; }
    public PropertyInfo PropertyInfo { get; init; }
    public bool IsId { get; init; } = false;
    public DbType DbType { get; set; }
    public bool IsNonUpdateable { get; set; }
    public bool IsNonInsertable { get; set; }
    public bool IsEnum { get; set; }
    public bool IsJsonType { get; set; }
    public JsonSerializerOptions JsonSerializerOptions { get; set; } = JsonSerializerOptions.Default;
    public bool IsIdIsWritable { get; set; }
    public bool IsPrimaryKey { get; set; } = false;
    public bool IsVersion { get; set; }
    public bool IsCreatedBy { get; set; }
    public bool IsCreatedOn { get; set; }
    public bool IsLastUpdatedBy { get; set; }
    public bool IsLastUpdatedOn { get; set; }

    public object? MakeParameterValueFromField<T>(T objectToCreate)
    {
        var value = PropertyInfo.GetValue(objectToCreate);
        if (value != null)
        {
            if (EnumType != null)
                value = DbType == DbType.String
                    ? value.ToString() // Save enum as string name
                    : Convert.ChangeType(value, Enum.GetUnderlyingType(EnumType)); // Save enum as int

            if (IsJsonType) value = JsonSerializer.Serialize(value);
        }

        return value;
    }
}