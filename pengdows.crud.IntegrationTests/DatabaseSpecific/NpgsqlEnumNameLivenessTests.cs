using System.Reflection;
using NpgsqlTypes;
using pengdows.crud.types;
using Xunit;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// AdvancedTypeRegistry's private NpgsqlNames class centralizes the NpgsqlDbType enum member
/// names it sets via reflection (SetEnumProperty). That reflection call fails open - a wrong
/// or stale name silently falls back to Npgsql's own type inference from the CLR value instead
/// of throwing, so a typo produces no failure anywhere in the unit suite (which only ever
/// exercises a hand-written MockNpgsqlDbType, not the real enum). This class closes that gap by
/// checking every one of those strings against the real, current Npgsql package's NpgsqlDbType
/// enum. It needs no live database connection - NpgsqlDbType is just an enum.
/// </summary>
public class NpgsqlEnumNameLivenessTests
{
    // Constants on NpgsqlNames that name a CLR property (via reflection), not an NpgsqlDbType
    // enum member - these are deliberately excluded from the enum-membership check below.
    private static readonly HashSet<string> PropertyNameConstants = new(StringComparer.Ordinal)
    {
        "DbTypeProperty",
        "DataTypeName"
    };

    [Fact]
    public void EveryNpgsqlDbTypeNameConstant_MatchesARealEnumMember()
    {
        var registryType = typeof(AdvancedTypeRegistry);
        var namesType = registryType.GetNestedType("NpgsqlNames", BindingFlags.NonPublic);
        Assert.NotNull(namesType);

        var realEnumNames = Enum.GetNames(typeof(NpgsqlDbType)).ToHashSet(StringComparer.Ordinal);

        var fields = namesType!.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Where(f => !PropertyNameConstants.Contains(f.Name))
            .ToList();

        // Sanity check that reflection actually found the constants we expect to check -
        // an empty list would make every assertion below vacuously true.
        Assert.NotEmpty(fields);

        var missing = new List<string>();
        foreach (var field in fields)
        {
            var value = (string)field.GetRawConstantValue()!;
            if (!realEnumNames.Contains(value))
            {
                missing.Add($"{namesType.Name}.{field.Name} = \"{value}\"");
            }
        }

        Assert.True(missing.Count == 0,
            $"The following NpgsqlNames constants do not name a real NpgsqlDbType member: {string.Join(", ", missing)}");
    }
}
