#region

using System.Data.Common;
using System.Reflection;

#endregion

namespace testbed;

public static class DbProviderFactoryFinder
{
    public static void LoadAllAssembliesFromBaseDirectory()
    {
        var baseDir = AppContext.BaseDirectory;
        var loadedPaths = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => a.Location)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var dllFiles = Directory.GetFiles(baseDir, "*.dll", SearchOption.TopDirectoryOnly);

        foreach (var dll in dllFiles)
        {
            try
            {
                if (!loadedPaths.Contains(dll))
                {
                    Assembly.LoadFrom(dll);
                }
            }
            catch
            {
                // Log or ignore non-loadable assemblies (e.g. native libraries)
            }
        }
    }

    public static IEnumerable<(string AssemblyName, string TypeName, DbProviderFactory Factory)> FindAllFactories()
    {
        LoadAllAssembliesFromBaseDirectory();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetExportedTypes();
            }
            catch
            {
                continue; // skip problematic assemblies
            }

            foreach (var type in types)
            {
                if (!typeof(DbProviderFactory).IsAssignableFrom(type))
                {
                    continue;
                }

                if (type.IsAbstract || !type.IsPublic)
                {
                    continue;
                }

                var instanceProp = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProp?.PropertyType == type)
                {
                    Console.WriteLine("Found DbProviderFactory instance on property");
                    var factory = instanceProp.GetValue(null) as DbProviderFactory;
                    if (factory != null)
                    {
                        yield return (assembly.GetName().Name!, type.FullName!, factory);
                    }
                }

                var instanceField = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceField?.FieldType == type)
                {
                    Console.WriteLine("Found DbProviderFactory instance on field");
                    var factory = instanceField.GetValue(null) as DbProviderFactory;
                    if (factory != null)
                    {
                        yield return (assembly.GetName().Name!, type.FullName!, factory);
                    }
                }
            }
        }
    }
}