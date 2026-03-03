using System;
using System.Reflection;
using System.Linq;

var asm = Assembly.LoadFrom("pengdows.crud/bin/Release/net8.0/pengdows.crud.dll");
foreach (var type in asm.GetTypes())
{
    if (type.IsPublic && type.IsClass && !type.IsAbstract)
    {
        if (type.Name == "DatabaseContext" || type.Name.StartsWith("TableGateway") || type.Name == "DefaultDatabaseContextFactory" || type.Name == "StubAuditValueResolver" || type.Name == "ReflectionSerializer" || type.Name.StartsWith("SystemTextJson") || type.Name == "Uuid7Optimized" || type.Name == "TableInfo" || type.Name == "ColumnInfo" || type.Name == "TypeMapRegistry" || type.Name == "MapperOptions" || type.Name == "FakeDb" || type.Name == "TypeCoercionOptions") continue;
        
        var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (ctors.Any())
        {
            Console.WriteLine($"Public constructor found on: {type.FullName}");
        }
    }
}
