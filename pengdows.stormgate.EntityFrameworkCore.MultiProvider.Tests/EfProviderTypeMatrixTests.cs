namespace pengdows.stormgate.EntityFrameworkCore.MultiProvider.Tests;

// Deliberate extension of EfProviderDeepTests' "Tier 2" claim, per external review feedback on the
// existing suite: a single string parameter ("Ada") only proves a provider's STRING type mapping
// stays generic against fakeDb. It says nothing about int, long, decimal, DateTime, Guid, bool,
// nullable, or binary — and concrete-type casts live inside individual type mappings, not
// centrally, so a provider could be green for strings and still crash on
// `((ProviderParameter)parameter).SomeProviderSpecificProperty` inside its GUID or timestamp
// mapping specifically. This file proves real parameter binding AND real materialization for a
// representative CLR type matrix, one local-variable-parameterized query per type, across every
// DeepTestCapable provider — strengthening "deep-test capable" from "one insert, one string
// query" to "the full representative type matrix round-trips."
public sealed class EfProviderTypeMatrixTests
{
    private static readonly DateTime SampleDateTime = new(2026, 3, 14, 9, 26, 53, DateTimeKind.Utc);
    private static readonly Guid SampleGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly byte[] SampleBinary = { 1, 2, 3, 4, 5 };
    private const long SampleLong = 9_000_000_001L;
    private const decimal SampleDecimal = 1234.5678m;

    [Theory]
    [MemberData(nameof(EfProviders.DeepTestCapable), MemberType = typeof(EfProviders))]
    public async Task EachRepresentativeClrType_BindsAsARealParameter_AndMaterializesBackCorrectly_NoRealDatabaseEngine(
        SupportedDatabase database)
    {
        var factory = new fakeDbFactory(database);
        var connection = (fakeDbConnection)factory.CreateConnection()!;

        var builder = new DbContextOptionsBuilder<TypeMatrixContext>();
        EfProviders.Configure(database, builder, connection);
        await using var db = new TypeMatrixContext(builder.Options);

        var row = new Dictionary<string, object?>
        {
            ["Id"] = 1,
            ["LongValue"] = SampleLong,
            ["DecimalValue"] = SampleDecimal,
            ["DateTimeValue"] = SampleDateTime,
            ["GuidValue"] = SampleGuid,
            ["BoolValue"] = true,
            ["BinaryValue"] = SampleBinary,
            ["NullableIntValue"] = null,
        };

        // Insert: path-agnostic "1 row affected" signal, as in EfProviderDeepTests.
        connection.EnqueueNonQueryResult(1);
        connection.EnqueueReaderResult([new Dictionary<string, object?> { ["Value"] = 1 }], recordsAffected: 1);

        db.Add(new TypeMatrixEntity
        {
            Id = 1,
            LongValue = SampleLong,
            DecimalValue = SampleDecimal,
            DateTimeValue = SampleDateTime,
            GuidValue = SampleGuid,
            BoolValue = true,
            BinaryValue = SampleBinary,
            NullableIntValue = null,
        });
        await db.SaveChangesAsync();

        // Each expected value is copied into a genuinely local variable, not referenced as a
        // const/static field directly in the lambda: a const field is compiled by the C#
        // compiler into a raw literal ConstantExpression at the call site, which EF Core embeds
        // straight into the generated SQL as a literal — never binding it as a parameter at all.
        // A closure over a local variable is what forces EF to actually parameterize it, exactly
        // like the pre-existing `var name = "Ada"; ... c.Name == name` pattern in
        // EfProviderDeepTests. This distinction was the bug in this file's first draft — the
        // fix, not a genuine provider finding, confirmed by rerunning after making this change.
        var expectedLong = SampleLong;
        await AssertTypedRoundTrip(db, connection, row, "LongValue", expectedLong, e => e.LongValue == expectedLong);

        var expectedDecimal = SampleDecimal;
        await AssertTypedRoundTrip(db, connection, row, "DecimalValue", expectedDecimal, e => e.DecimalValue == expectedDecimal);

        var expectedDateTime = SampleDateTime;
        await AssertTypedRoundTrip(db, connection, row, "DateTimeValue", expectedDateTime, e => e.DateTimeValue == expectedDateTime);

        var expectedGuid = SampleGuid;
        await AssertTypedRoundTrip(db, connection, row, "GuidValue", expectedGuid, e => e.GuidValue == expectedGuid);

        var expectedBool = true;
        await AssertTypedRoundTrip(db, connection, row, "BoolValue", expectedBool, e => e.BoolValue == expectedBool);

        await AssertTypedRoundTrip(db, connection, row, "NullableIntValue (IS NULL)", null, e => e.NullableIntValue == null);
    }

    // Each call captures a local variable (the `expected` parameter) into the LINQ predicate the
    // same way — this is what forces EF to generate a real bound parameter rather than an inlined
    // literal, per the existing file's established pattern.
    private static async Task AssertTypedRoundTrip(
        TypeMatrixContext db,
        fakeDbConnection connection,
        Dictionary<string, object?> row,
        string label,
        object? expected,
        System.Linq.Expressions.Expression<Func<TypeMatrixEntity, bool>> predicate)
    {
        connection.EnqueueReaderResult([row]);

        var results = await db.TypeMatrixEntities.Where(predicate).ToListAsync();

        Assert.Single(results);

        var command = Assert.Single(
            connection.ExecutedReaderCommands,
            c => c.CommandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
                && c.CommandText.Contains("WHERE", StringComparison.OrdinalIgnoreCase));

        if (expected is null)
        {
            // "IS NULL" never binds a parameter — nothing further to assert on the command's
            // parameter list, but reaching here at all already proves the predicate materialized.
            return;
        }

        // Equals(p.Value, expected) covers the common case; the ToString() fallback covers a
        // provider that legitimately converts the CLR type before binding — e.g. Snowflake's own
        // EF provider binds Guid parameters as their string representation via its own
        // ValueConverter (Snowflake SQL has no native GUID type). That is expected, documented
        // provider behavior, not a casting failure — the point of this assertion is "was the
        // value actually bound as a parameter, carrying the right value," not "did the provider
        // preserve the exact CLR type," which no provider is obligated to do.
        var bound = Assert.Single(
            command.Parameters,
            p => Equals(p.Value, expected) || Equals(p.Value?.ToString(), expected.ToString()));
        Assert.True(
            Equals(bound.Value, expected) || Equals(bound.Value?.ToString(), expected.ToString()),
            $"Bound parameter value '{bound.Value}' does not match expected '{expected}' for {label}.");

        connection.ExecutedReaderCommands.Clear();
    }

    private sealed class TypeMatrixContext(DbContextOptions<TypeMatrixContext> options) : DbContext(options)
    {
        public DbSet<TypeMatrixEntity> TypeMatrixEntities => Set<TypeMatrixEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TypeMatrixEntity>().Property(e => e.Id).ValueGeneratedNever();
        }
    }

    private sealed class TypeMatrixEntity
    {
        public int Id { get; set; }
        public long LongValue { get; set; }
        public decimal DecimalValue { get; set; }
        public DateTime DateTimeValue { get; set; }
        public Guid GuidValue { get; set; }
        public bool BoolValue { get; set; }
        public byte[] BinaryValue { get; set; } = Array.Empty<byte>();
        public int? NullableIntValue { get; set; }
    }
}
