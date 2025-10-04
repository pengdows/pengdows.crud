using System;
using System.Reflection;
using pengdows.crud;

public class TestEntity
{
    [Id]
    [Column("id")]
    public int Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Version]
    [Column("version")]
    public int Version { get; set; }
}

public class NestedTestEntity
{
    public class TestEntity
    {
        [Id]
        [Column("id")]
        public int Id { get; set; }

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Version]
        [Column("version")]
        public int Version { get; set; }
    }
}

public class AttributeDebugger
{
    public static void Main()
    {
        Console.WriteLine("Testing attribute detection...");
        
        // Test top-level class
        TestAttributes(typeof(TestEntity), "Top-level TestEntity");
        
        // Test nested class
        TestAttributes(typeof(NestedTestEntity.TestEntity), "Nested TestEntity");
        
        // Test the actual test class (simulating the real structure)
        TestAttributes(typeof(pengdows.crud.Tests.EntityHelperCriticalPathTests.TestEntity), "Actual test TestEntity");
    }
    
    private static void TestAttributes(Type type, string description)
    {
        Console.WriteLine($"\n--- {description} ---");
        Console.WriteLine($"Type: {type.FullName}");
        
        var properties = type.GetProperties();
        foreach (var prop in properties)
        {
            Console.WriteLine($"Property: {prop.Name}");
            var attrs = prop.GetCustomAttributes(inherit: true);
            Console.WriteLine($"  Attributes count: {attrs.Length}");
            
            foreach (var attr in attrs)
            {
                Console.WriteLine($"    - {attr.GetType().Name}: {attr}");
            }
            
            var idAttr = attrs.FirstOrDefault(a => a is IdAttribute);
            var colAttr = attrs.FirstOrDefault(a => a is ColumnAttribute);
            var verAttr = attrs.FirstOrDefault(a => a is VersionAttribute);
            
            Console.WriteLine($"  IdAttribute found: {idAttr != null}");
            Console.WriteLine($"  ColumnAttribute found: {colAttr != null}");
            Console.WriteLine($"  VersionAttribute found: {verAttr != null}");
        }
    }
}