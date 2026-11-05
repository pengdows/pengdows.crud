using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// DRY-010 stage 0 for the registry: pins, for every CLR type in the semantic-type proposal and
/// every supported database, what <see cref="AdvancedTypeRegistry.GetMapping"/> returns today: the
/// DbType the mapping declares, the DbType and size its configurator sets on a fakeDb parameter, and
/// the converter registered for the CLR type. Centralizing the CLR mapping (docs/datatype-clr-mapping-proposal.md)
/// must change nothing this file doesn't list as intended. PIN_TYPES=1 rewrites the golden file; any
/// other difference fails.
/// </summary>
[Collection("TypeRegistry")]
public class TypeRegistryMappingCharacterizationTests
{
    private const string GoldenFile = "TypeRegistryMappings.txt";

    // The CLR types the semantic-type proposal names (plus the scalars and the common collections).
    private static readonly Type[] ClrTypes =
    {
        typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int),
        typeof(uint), typeof(long), typeof(ulong), typeof(Int128), typeof(UInt128), typeof(BigInteger),
        typeof(float), typeof(double), typeof(decimal), typeof(string), typeof(Guid),
        typeof(DateTime), typeof(DateTimeOffset), typeof(DateOnly), typeof(TimeOnly), typeof(TimeSpan),
        typeof(byte[]), typeof(BitArray), typeof(JsonDocument), typeof(IPAddress), typeof(PhysicalAddress),
        typeof(Cidr), typeof(Inet), typeof(MacAddress), typeof(Geometry), typeof(Geography),
        typeof(HierarchyId), typeof(Range<int>), typeof(Range<DateTime>), typeof(IntervalYearMonth),
        typeof(IntervalDaySecond), typeof(PostgreSqlInterval), typeof(float[]), typeof(int[]),
        typeof(List<int>), typeof(Dictionary<string, object>), typeof(HStore), typeof(object),
    };

    [Fact]
    public void RegistryMappingTable_MatchesGoldenFile()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var text = new StringBuilder();

        foreach (var clrType in ClrTypes)
        {
            text.AppendLine($"== {TypeName(clrType)}");
            foreach (var database in Enum.GetValues<SupportedDatabase>().Where(d => d != SupportedDatabase.Unknown))
            {
                text.Append("  ").Append(database).Append(": ").AppendLine(Describe(registry, clrType, database));
            }
        }

        TypeSystemCharacterizationTests.AssertPinnedFile(
            Path.Combine(Path.GetDirectoryName(PinnedHere())!, GoldenFile),
            text.ToString().Replace("\r\n", "\n"));
    }

    private static string Describe(AdvancedTypeRegistry registry, Type clrType, SupportedDatabase database)
    {
        var mapping = registry.GetMapping(clrType, database);
        var converter = registry.GetConverter(clrType);
        var converterText = converter == null ? "-" : converter.GetType().Name;

        if (mapping == null)
        {
            return $"no-mapping converter={converterText}";
        }

        var configured = "-";
        if (mapping.ConfigureParameter != null)
        {
            try
            {
                var factory = new fakeDbFactory(database);
                var parameter = factory.CreateParameter();
                mapping.ConfigureParameter(parameter, null);
                configured = $"{parameter.DbType}/size={parameter.Size}";
            }
            catch (Exception ex)
            {
                configured = $"throws:{ex.GetType().Name}";
            }
        }

        return $"declared={mapping.DbType} configured={configured} converter={converterText}";
    }

    // Full names without assembly versions: Range<int> is named with Version=8.0.0.0 on net8 and
    // Version=10.0.0.0 on net10, which would make one golden file fail on the other framework.
    private static string TypeName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        var definition = type.GetGenericTypeDefinition().FullName!;
        var arguments = string.Join(",", type.GetGenericArguments().Select(TypeName));
        return $"{definition}[{arguments}]";
    }

    private static string PinnedHere([CallerFilePath] string here = "") => here;
}
