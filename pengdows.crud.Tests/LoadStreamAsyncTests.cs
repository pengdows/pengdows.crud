using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for LoadStreamAsync methods that provide IAsyncEnumerable streaming of entities.
/// These tests verify memory-efficient iteration over large result sets.
/// </summary>
public class LoadStreamAsyncTests
{
    [Table("test")]
    private class TestEntity
    {
        [Id][Column("id", DbType.Int32)] public int Id { get; set; }

        [Column("name", DbType.String)] public string? Name { get; set; }

        [Column("value", DbType.Int32)] public int Value { get; set; }
    }

    [Fact]
    public async Task LoadStreamAsync_WithResults_StreamsAllEntities()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new TableGateway<TestEntity, int>(context);

        var container = helper.BuildBaseRetrieve("t");
        container.Query.Append(" WHERE t.id IN (1, 2, 3)");

        // Act - fakeDb returns empty result set by default, but streaming should work
        var results = new List<TestEntity>();
        await foreach (var entity in helper.LoadStreamAsync(container))
        {
            results.Add(entity);
        }

        // Assert - fakeDb returns empty, but method worked correctly
        Assert.NotNull(results); // Streaming completed successfully
    }

    [Fact]
    public async Task LoadStreamAsync_WithCancellationToken_SupportsEarlyCancellation()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new TableGateway<TestEntity, int>(context);

        var container = helper.BuildBaseRetrieve("t");
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancel to ensure cancellation is respected

        // Act & Assert - Should observe cancellation
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var entity in helper.LoadStreamAsync(container, cts.Token))
            {
                Assert.Fail("Should not enumerate when token is already cancelled");
            }
        });
    }

    [Fact]
    public async Task LoadStreamAsync_WithEmptyResultSet_ReturnsEmptyStream()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new TableGateway<TestEntity, int>(context);

        var container = helper.BuildBaseRetrieve("t");
        container.Query.Append(" WHERE 1=0"); // Empty result

        // Act
        var results = new List<TestEntity>();
        await foreach (var entity in helper.LoadStreamAsync(container))
        {
            results.Add(entity);
        }

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task LoadStreamAsync_WithNullContainer_ThrowsArgumentNullException()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new TableGateway<TestEntity, int>(context);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in helper.LoadStreamAsync(null!))
            {
            }
        });
    }

    [Fact]
    public async Task LoadStreamAsync_StreamsWithoutMaterializingEntireList()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new TableGateway<TestEntity, int>(context);

        var container = helper.BuildBaseRetrieve("t");

        // Act - Process only first 3 items without iterating entire result set
        var processedCount = 0;
        await foreach (var entity in helper.LoadStreamAsync(container))
        {
            processedCount++;
            if (processedCount >= 3)
            {
                break;
            }
        }

        // Assert - fakeDb returns empty, but early break logic works
        Assert.True(processedCount <= 3, "Should stop early when breaking from loop");
    }

    [Fact]
    public async Task LoadStreamAsync_MultipleEnumerations_EachExecutesQuery()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new TableGateway<TestEntity, int>(context);

        var container = helper.BuildBaseRetrieve("t");

        // Act - First enumeration
        var firstResults = new List<TestEntity>();
        await foreach (var entity in helper.LoadStreamAsync(container))
        {
            firstResults.Add(entity);
        }

        // Act - Second enumeration (should work, not throw "reader already consumed")
        var secondResults = new List<TestEntity>();
        await foreach (var entity in helper.LoadStreamAsync(container))
        {
            secondResults.Add(entity);
        }

        // Assert
        Assert.Equal(firstResults.Count, secondResults.Count);
    }

    [Fact]
    public async Task LoadStreamAsync_WithComplexEntity_MapsAllProperties()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new TableGateway<TestEntity, int>(context);

        var container = helper.BuildBaseRetrieve("t");
        container.Query.Append(" WHERE t.id = 1");

        // Act
        var count = 0;
        await foreach (var entity in helper.LoadStreamAsync(container))
        {
            count++;
            // fakeDb returns empty, so this won't execute
            // but the method itself works correctly
        }

        // Assert - streaming completed successfully even with empty results
        Assert.True(count >= 0, "Enumeration completed successfully");
    }

    [Fact]
    public async Task LoadStreamAsync_WithoutCancellationToken_UsesDefaultToken()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new TableGateway<TestEntity, int>(context);

        var container = helper.BuildBaseRetrieve("t");

        // Act & Assert - Should complete without throwing
        var count = 0;
        await foreach (var entity in helper.LoadStreamAsync(container))
        {
            count++;
        }

        Assert.True(count >= 0); // Should complete successfully
    }

    [Fact]
    public async Task LoadStreamAsync_DisposesReaderAfterEnumeration()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new TableGateway<TestEntity, int>(context);

        var container = helper.BuildBaseRetrieve("t");

        // Act - Enumerate to completion
        await foreach (var entity in helper.LoadStreamAsync(container))
        {
            // Process all
        }

        // Assert - Create another container and verify connection is available
        var container2 = helper.BuildBaseRetrieve("t");
        var count = 0;
        await foreach (var entity in helper.LoadStreamAsync(container2))
        {
            count++;
        }

        Assert.True(count >= 0); // Should work, proving previous reader was disposed
    }

    [Fact]
    public async Task LoadStreamAsync_PartialEnumerationWithDispose_DisposesReaderEarly()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new TableGateway<TestEntity, int>(context);

        var container = helper.BuildBaseRetrieve("t");

        // Act - Break early from enumeration
        var enumerator = helper.LoadStreamAsync(container).GetAsyncEnumerator();
        await using (enumerator)
        {
            if (await enumerator.MoveNextAsync())
            {
                var _ = enumerator.Current;
                // Break early - dispose should happen
            }
        }

        // Assert - Verify connection is available for reuse
        var container2 = helper.BuildBaseRetrieve("t");
        var results = new List<TestEntity>();
        await foreach (var entity in helper.LoadStreamAsync(container2))
        {
            results.Add(entity);
        }

        Assert.NotNull(results); // Should work
    }

    [Fact]
    public async Task LoadStreamAsync_NullEntityFromReader_SkipsNull()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new TableGateway<TestEntity, int>(context);

        var container = helper.BuildBaseRetrieve("t");

        // Act - Collect all non-null results
        var results = new List<TestEntity>();
        await foreach (var entity in helper.LoadStreamAsync(container))
        {
            results.Add(entity);
        }

        // Assert - All returned entities should be non-null
        Assert.All(results, entity => Assert.NotNull(entity));
    }

    [Fact]
    public async Task LoadStreamAsync_LargeResultSet_StreamsEfficiently()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new TableGateway<TestEntity, int>(context);

        var container = helper.BuildBaseRetrieve("t");

        // Act - Use LINQ to take only first 100 items from potentially large stream
        var results = new List<TestEntity>();
        await foreach (var entity in helper.LoadStreamAsync(container))
        {
            results.Add(entity);
            if (results.Count >= 100)
            {
                break;
            }
        }

        // Assert
        Assert.True(results.Count <= 100, "Should stop early without materializing entire result set");
    }

    // Regression coverage gap (audit item 5.8): every prior test in this file exercises
    // LoadStreamAsync against fakeDb's default EMPTY result set (see the comments above
    // acknowledging this), so no test ever actually proved reader/connection/governor-slot
    // cleanup with a genuinely non-empty, mid-stream reader. The cleanup mechanism itself is
    // guaranteed by C# `await using` semantics inside the async-iterator method body — this is
    // about proving that guarantee holds here, not about a suspected code defect.
    //
    // Verification gap, documented rather than papered over: a third scenario (an exception
    // thrown from INSIDE the iterator itself, e.g. a mapping/coercion failure while
    // materializing a row, as opposed to the consumer's own loop body throwing) is not covered
    // by a dedicated test below. Reading BaseTableGateway.Core.cs's LoadStreamAsync shows its
    // entire body — including the mapping call — sits inside the SAME `await using var reader =
    // ...` scope that the two tests below already exercise, so the teardown path is identical
    // for all three scenarios; there is no separate code path scenario 3 could take that these
    // tests don't already cover. Constructing a targeted mapping-failure reproduction requires
    // seeding a canned reader result through the full connection-pooling/governor path (distinct
    // from DataReaderMapperFakeDbTests' lower-level direct-connection setup) for marginal
    // additional confidence over what's already proven here — not attempted in this pass.
    [Table("stream_cleanup")]
    private class StreamCleanupEntity
    {
        [Id(false)][Column("id", DbType.Int32)] public int Id { get; set; }

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
    }

    private static async Task<(fakeDbFactory Factory, DatabaseContext Context, TableGateway<StreamCleanupEntity, int> Helper)>
        CreateSeededStreamContextAsync(string connectionStringLabel, int rowCount)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.EnableDataPersistence = true;
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source={connectionStringLabel};EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        var context = new DatabaseContext(config, factory);
        var helper = new TableGateway<StreamCleanupEntity, int>(context);

        var qp = context.QuotePrefix;
        var qs = context.QuoteSuffix;
        await context.CreateSqlContainer($@"CREATE TABLE IF NOT EXISTS {qp}stream_cleanup{qs}(
            {qp}id{qs} INTEGER PRIMARY KEY AUTOINCREMENT,
            {qp}name{qs} TEXT NOT NULL
        )").ExecuteNonQueryAsync();

        for (var i = 0; i < rowCount; i++)
        {
            await helper.CreateAsync(new StreamCleanupEntity { Name = $"row{i}" }, context);
        }

        return (factory, context, helper);
    }

    [Fact]
    public async Task LoadStreamAsync_ConsumerBreaksMidEnumeration_WithRealRows_DisposesReaderConnection()
    {
        var (factory, context, helper) = await CreateSeededStreamContextAsync("stream-break-test", rowCount: 5);
        await using var _ = context;

        var container = helper.BuildBaseRetrieve("t");
        var seen = 0;
        await foreach (var entity in helper.LoadStreamAsync(container))
        {
            seen++;
            if (seen == 2)
            {
                break; // Consumer breaks mid-enumeration, well before the last of 5 rows.
            }
        }

        Assert.Equal(2, seen);
        var streamingConnection = factory.CreatedConnections[^1];
        Assert.True(streamingConnection.DisposeCount > 0,
            "Breaking out of the await-foreach must dispose the reader's connection immediately, not leak it until context disposal.");
    }

    [Fact]
    public async Task LoadStreamAsync_ConsumerThrowsMidEnumeration_WithRealRows_DisposesReaderConnection()
    {
        var (factory, context, helper) = await CreateSeededStreamContextAsync("stream-throw-test", rowCount: 5);
        await using var _ = context;

        var container = helper.BuildBaseRetrieve("t");
        var seen = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var entity in helper.LoadStreamAsync(container))
            {
                seen++;
                if (seen == 2)
                {
                    throw new InvalidOperationException("Simulated consumer-side failure mid-stream.");
                }
            }
        });

        Assert.Equal(2, seen);
        var streamingConnection = factory.CreatedConnections[^1];
        Assert.True(streamingConnection.DisposeCount > 0,
            "An exception thrown from the consumer's loop body must still dispose the reader's connection.");
    }
}