#region

using System.Data;
using System.Reflection;
using System.Text.Json;

#endregion

namespace pengdow.crud;

public interface IColumnInfo
{
    string Name { get; init; }
    PropertyInfo PropertyInfo { get; init; }
    bool IsId { get; init; }
    DbType DbType { get; set; }
    bool IsNonUpdateable { get; set; }
    bool IsNonInsertable { get; set; }
    bool IsEnum { get; set; }
    Type? EnumType { get; set; }
    bool IsJsonType { get; set; }
    JsonSerializerOptions JsonSerializerOptions { get; set; }
    bool IsIdIsWritable { get; set; }
    bool IsPrimaryKey { get; set; }
    bool IsVersion { get; set; }
    bool IsCreatedBy { get; set; }
    bool IsCreatedOn { get; set; }
    bool IsLastUpdatedBy { get; set; }
    bool IsLastUpdatedOn { get; set; }
    object? MakeParameterValueFromField<T>(T objectToCreate);}