#region

using System.Text.Json;

#endregion

namespace pengdow.crud.attributes;

[AttributeUsage(AttributeTargets.Property)]
public class JsonAttribute : Attribute
{
    public JsonSerializerOptions SerializerOptions { get; set; } = JsonSerializerOptions.Default;
}