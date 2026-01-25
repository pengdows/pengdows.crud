#region

using System.Data;
using System.Reflection;
using System.Text.Json;

#endregion

namespace pengdows.crud;

public class ColumnInfo : IColumnInfo
{
    // Compiled fast getter for this column's PropertyInfo
    public Func<object, object?>? FastGetter { get; set; }
    public Type? EnumType { get; set; }
    public string Name { get; init; } = null!;
    public PropertyInfo PropertyInfo { get; init; } = null!;
    public bool IsId { get; init; } = false;
    public DbType DbType { get; set; }
    public bool IsNonUpdateable { get; set; }
    public bool IsNonInsertable { get; set; }
    public bool IsEnum { get; set; }
    public bool IsJsonType { get; set; }
    public JsonSerializerOptions JsonSerializerOptions { get; set; } = JsonSerializerOptions.Default;
    public bool IsIdIsWritable { get; set; }
    public bool IsPrimaryKey { get; set; } = false;
    public int PkOrder { get; set; }
    public bool IsVersion { get; set; }
    public bool IsCreatedBy { get; set; }
    public bool IsCreatedOn { get; set; }
    public bool IsLastUpdatedBy { get; set; }
    public bool IsLastUpdatedOn { get; set; }
    public int Ordinal { get; set; }
    public Type? EnumUnderlyingType { get; set; }
    public bool EnumAsString { get; set; }

    public object? MakeParameterValueFromField<T>(T objectToCreate)
    {
        var value = FastGetter != null
            ? FastGetter(objectToCreate!)
            : PropertyInfo.GetValue(objectToCreate);
        var current = value;

        if (current != null)
        {
            if (EnumType != null)
            {
                if (DbType == DbType.String)
                {
                    value = current.ToString(); // Save enum as string name
                }
                else
                {
                    // Use cached underlying type, or determine it if not cached
                    var underlyingType = EnumUnderlyingType ?? Enum.GetUnderlyingType(EnumType);
                    value = Convert.ChangeType(current, underlyingType);
                }
            }

            if (IsJsonType)
            {
                var options = JsonSerializerOptions ?? JsonSerializerOptions.Default;
                value = TypeCoercionHelper.GetJsonText(current, options);
            }
        }

        return value;
    }
}