namespace pengdows.crud;

/// <summary>
/// Provides helper extensions for <see cref="ITypeMapRegistry"/>.
/// </summary>
public static class TypeMapRegistryExtensions
{
    /// <summary>
    /// Ensures metadata for <typeparamref name="T"/> is registered.
    /// </summary>
    /// <typeparam name="T">Entity type.</typeparam>
    /// <param name="registry">Registry instance.</param>
    public static void Register<T>(this ITypeMapRegistry registry)
    {
        _ = registry.GetTableInfo<T>();
    }
}
