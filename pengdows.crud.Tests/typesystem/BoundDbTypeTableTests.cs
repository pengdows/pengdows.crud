using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// Answers, from measurement rather than reading the code: for every sample in
/// <see cref="TypeCompletenessTests.Samples"/> (every CLR type the library reads or writes), what
/// <see cref="DbType"/> the parameter is actually bound with on every database, when the column
/// declares the sample's <see cref="DbType"/>. The bound type is whatever the dialect's
/// CreateDbParameter produces, so a rewrite (for example ulong declared UInt64 binding as Decimal on
/// PostgreSQL) shows up here. Pinned as BoundDbTypes.txt; PIN_TYPES=1 rewrites it, any other
/// difference fails.
/// </summary>
[Collection("TypeRegistry")]
public class BoundDbTypeTableTests
{
    private const string GoldenFile = "BoundDbTypes.txt";

    [Fact]
    public void BoundDbTypeTable_MatchesGoldenFile()
    {
        var products = Enum.GetValues<SupportedDatabase>().Where(p => p != SupportedDatabase.Unknown).ToArray();
        var dialects = products.ToDictionary(
            p => p,
            p => (SqlDialect)SqlDialectFactory.CreateDialectForType(p, new fakeDbFactory(p), NullLogger.Instance));

        var text = new StringBuilder();
        var summary = new SortedDictionary<string, SortedSet<string>>();

        text.AppendLine("# label | declared DbType | CLR type | per database: the DbType the parameter is bound with");
        foreach (var sample in TypeCompletenessTests.Samples())
        {
            var cells = new List<string>();
            foreach (var product in products)
            {
                var bound = BoundTypeOf(dialects[product], sample);
                cells.Add($"{product}={bound}");
                if (!summary.ContainsKey(sample.DbType.ToString()))
                {
                    summary[sample.DbType.ToString()] = new SortedSet<string>();
                }

                summary[sample.DbType.ToString()].Add(bound);
            }

            text.Append(sample.Label).Append(" | ").Append(sample.DbType).Append(" | ").Append(sample.Clr.Name)
                .Append(" | ").AppendLine(string.Join(" ", cells));
        }

        text.AppendLine();
        text.AppendLine("# declared DbType -> every DbType it is bound as, across all databases");
        foreach (var (declared, bound) in summary)
        {
            text.Append(declared).Append(" -> ").AppendLine(string.Join(", ", bound));
        }

        TypeSystemCharacterizationTests.AssertPinnedFile(
            Path.Combine(Path.GetDirectoryName(PinnedHere())!, GoldenFile),
            text.ToString().Replace("\r\n", "\n"));
    }

    private static string BoundTypeOf(SqlDialect dialect, TypeCompletenessTests.Sample sample)
    {
        try
        {
            var parameter = dialect.CreateDbParameter("p", sample.DbType, sample.Value());
            return parameter.DbType.ToString();
        }
        catch (Exception ex)
        {
            return $"throws:{ex.GetType().Name}";
        }
    }

    private static string PinnedHere([CallerFilePath] string here = "") => here;
}
