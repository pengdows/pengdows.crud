using System.Linq;
using pengdows.crud.types;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// DRY-010: every CLR type is read by exactly one implementation. A type with an AdvancedTypeRegistry
/// converter has no coercion, general or for any database: the registry is asked first, so a coercion
/// beside a converter shadowed it, and the two copies drifted (DRY-011..015).
/// </summary>
public class OneReaderPerTypeTests
{
    [Fact]
    public void NoTypeHasBothAConverterAndACoercion()
    {
        var converted = AdvancedTypeRegistry.Shared.ConverterTypes.ToHashSet();
        var overlap = CoercionRegistry.Shared.RegisteredTypes()
            .Where(c => converted.Contains(c.Type))
            .Select(c => c.Provider is { } p ? $"{c.Type.Name} ({p})" : c.Type.Name)
            .ToList();

        Assert.True(overlap.Count == 0, "Read by both a converter and a coercion: " + string.Join(", ", overlap));
    }
}
