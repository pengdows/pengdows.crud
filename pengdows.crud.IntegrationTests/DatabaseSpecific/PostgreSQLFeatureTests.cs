using System.Data;
using System.Text.Json;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// Integration tests for PostgreSQL-specific features that showcase pengdows.crud's
/// database-aware advantages over generic ORMs like Entity Framework.
/// </summary>
[Collection("IntegrationTests")]
public class PostgreSQLFeatureTests : DatabaseTestBase
{
    public PostgreSQLFeatureTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture) { }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders()
    {
        // Only test against PostgreSQL
        return new[] { SupportedDatabase.PostgreSql };
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        // Create test tables with PostgreSQL-specific features
        await CreateProductTableWithJsonbAsync(context);
        await CreateArticleTableWithFullTextSearchAsync(context);
        await CreateTaggedItemTableWithArraysAsync(context);
    }

    [Fact]
    public async Task JSONB_NativeOperators_QueryPerformance()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async context =>
        {
            // Arrange - Insert test data with JSONB
            await InsertProductsWithJsonbAsync(context);

            // Act - Use native JSONB operators (-> and ->>)
            await using var container = context.CreateSqlContainer(@"
                SELECT id, name, specifications->>'brand' as brand, specifications->>'model' as model
                FROM products
                WHERE specifications->>'brand' = ");
            container.Query.Append(container.MakeParameterName("brand"));
            container.AddParameterWithValue("brand", DbType.String, "Apple");

            var results = new List<dynamic>();
            await using var reader = await container.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add(new
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    Brand = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Model = reader.IsDBNull(3) ? null : reader.GetString(3)
                });
            }

            // Assert
            Assert.NotEmpty(results);
            Assert.All(results, r => Assert.Equal("Apple", r.Brand));
            Output.WriteLine($"Found {results.Count} Apple products using native JSONB operators");
        });
    }

    [Fact]
    public async Task JSONB_ComplexQueries_NestedPathAccess()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async context =>
        {
            // Arrange
            await InsertProductsWithComplexJsonbAsync(context);

            // Act - Query nested JSONB paths
            await using var container = context.CreateSqlContainer(@"
                SELECT name, specifications->'technical'->>'processor' as processor
                FROM products
                WHERE specifications->'technical'->>'ram' = ");
            container.Query.Append(container.MakeParameterName("ram"));
            container.AddParameterWithValue("ram", DbType.String, "16GB");

            var results = new List<(string Name, string Processor)>();
            await using var reader = await container.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add((
                    reader.GetString(0),
                    reader.IsDBNull(1) ? "Unknown" : reader.GetString(1)
                ));
            }

            // Assert
            Assert.NotEmpty(results);
            Assert.All(results, r => Assert.NotEqual("Unknown", r.Processor));
        });
    }

    [Fact]
    public async Task Arrays_AnyOperator_MembershipQueries()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async context =>
        {
            // Arrange
            await InsertTaggedItemsAsync(context);

            // Act - Use PostgreSQL ANY operator with arrays
            await using var container = context.CreateSqlContainer(@"
                SELECT id, name, tags
                FROM tagged_items
                WHERE ");
            container.Query.Append(container.MakeParameterName("tag"));
            container.Query.Append(" = ANY(tags)");
            container.AddParameterWithValue("tag", DbType.String, "featured");

            var results = new List<(long Id, string Name)>();
            await using var reader = await container.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add((reader.GetInt64(0), reader.GetString(1)));
            }

            // Assert
            Assert.NotEmpty(results);
            Output.WriteLine($"Found {results.Count} items with 'featured' tag using ANY operator");
        });
    }

    [Fact]
    public async Task Arrays_ContainsOperator_SubsetQueries()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async context =>
        {
            // Arrange
            await InsertTaggedItemsAsync(context);

            // Act - Use PostgreSQL @> (contains) operator
            await using var container = context.CreateSqlContainer(@"
                SELECT id, name, tags
                FROM tagged_items
                WHERE tags @> ");
            container.Query.Append(container.MakeParameterName("searchTags"));
            container.AddParameterWithValue("searchTags", DbType.Object, new[] { "premium", "featured" });

            var results = new List<(long Id, string Name)>();
            await using var reader = await container.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add((reader.GetInt64(0), reader.GetString(1)));
            }

            // Assert - Should find items that have both 'premium' AND 'featured' tags
            Assert.NotEmpty(results);
            Output.WriteLine($"Found {results.Count} items containing both premium and featured tags");
        });
    }

    [Fact]
    public async Task FullTextSearch_NativeTSVector_SearchPerformance()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async context =>
        {
            // Arrange
            await InsertArticlesWithFullTextAsync(context);

            // Act - Use PostgreSQL full-text search with tsvector
            await using var container = context.CreateSqlContainer(@"
                SELECT id, title, content
                FROM articles
                WHERE search_vector @@ plainto_tsquery(");
            container.Query.Append(container.MakeParameterName("searchTerm"));
            container.Query.Append(")");
            container.AddParameterWithValue("searchTerm", DbType.String, "database performance");

            var results = new List<(long Id, string Title)>();
            await using var reader = await container.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add((reader.GetInt64(0), reader.GetString(1)));
            }

            // Assert
            Assert.NotEmpty(results);
            Output.WriteLine($"Found {results.Count} articles using full-text search");
        });
    }

    [Fact]
    public async Task FullTextSearch_RankedResults_WithHighlighting()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async context =>
        {
            // Arrange
            await InsertArticlesWithFullTextAsync(context);

            // Act - Get ranked search results with highlighting
            await using var container = context.CreateSqlContainer(@"
                SELECT id, title,
                       ts_rank(search_vector, query) as rank,
                       ts_headline('english', content, query) as snippet
                FROM articles, plainto_tsquery(");
            container.Query.Append(container.MakeParameterName("searchTerm"));
            container.Query.Append(") query");
            container.Query.Append(@"
                WHERE search_vector @@ query
                ORDER BY rank DESC
                LIMIT 5");
            container.AddParameterWithValue("searchTerm", DbType.String, "optimization techniques");

            var results = new List<(string Title, float Rank, string Snippet)>();
            await using var reader = await container.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add((
                    reader.GetString(1),
                    reader.GetFloat(2),
                    reader.GetString(3)
                ));
            }

            // Assert
            Assert.NotEmpty(results);
            Assert.True(results.First().Rank >= results.Last().Rank, "Results should be ranked by relevance");
            Output.WriteLine($"Found {results.Count} ranked results with snippets");
        });
    }

    [Fact]
    public async Task UpsertWithConflictResolution_OnConflictDoUpdate()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async context =>
        {
            // Arrange - Create a product
            await using var insertContainer = context.CreateSqlContainer(@"
                INSERT INTO products (name, specifications)
                VALUES (");
            insertContainer.Query.Append(insertContainer.MakeParameterName("name"));
            insertContainer.Query.Append(", ");
            insertContainer.Query.Append(insertContainer.MakeParameterName("specs"));
            insertContainer.Query.Append(")");
            insertContainer.AddParameterWithValue("name", DbType.String, "Conflict Test Product");
            using var insertSpecs = JsonDocument.Parse("{\"version\": 1}");
            insertContainer.AddParameterWithValue("specs", DbType.Object, insertSpecs);

            await insertContainer.ExecuteNonQueryAsync();

            // Act - Upsert with conflict resolution
            await using var upsertContainer = context.CreateSqlContainer(@"
                INSERT INTO products (name, specifications)
                VALUES (");
            upsertContainer.Query.Append(upsertContainer.MakeParameterName("name"));
            upsertContainer.Query.Append(", ");
            upsertContainer.Query.Append(upsertContainer.MakeParameterName("specs"));
            upsertContainer.Query.Append(@")
                ON CONFLICT (name)
                DO UPDATE SET
                    specifications = products.specifications || EXCLUDED.specifications,
                    updated_at = CURRENT_TIMESTAMP");

            upsertContainer.AddParameterWithValue("name", DbType.String, "Conflict Test Product");
            using var upsertSpecs = JsonDocument.Parse("{\"version\": 2, \"updated\": true}");
            upsertContainer.AddParameterWithValue("specs", DbType.Object, upsertSpecs);

            var affectedRows = await upsertContainer.ExecuteNonQueryAsync();

            // Assert
            Assert.Equal(1, affectedRows);

            // Verify the merge worked
            await using var verifyContainer = context.CreateSqlContainer(@"
                SELECT specifications FROM products WHERE name = ");
            verifyContainer.Query.Append(verifyContainer.MakeParameterName("name"));
            verifyContainer.AddParameterWithValue("name", DbType.String, "Conflict Test Product");

            var result = await verifyContainer.ExecuteScalarAsync<string>();
            Assert.NotNull(result);
            Assert.Contains("\"version\": 2", result);
            Assert.Contains("\"updated\": true", result);
        });
    }

    // Helper methods for test data setup

    private async Task CreateProductTableWithJsonbAsync(IDatabaseContext context)
    {
        await using var container = context.CreateSqlContainer(@"
            CREATE TABLE IF NOT EXISTS products (
                id BIGSERIAL PRIMARY KEY,
                name VARCHAR(255) UNIQUE NOT NULL,
                specifications JSONB,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            )");
        await container.ExecuteNonQueryAsync();
    }

    private async Task CreateArticleTableWithFullTextSearchAsync(IDatabaseContext context)
    {
        await using var container = context.CreateSqlContainer(@"
            CREATE TABLE IF NOT EXISTS articles (
                id BIGSERIAL PRIMARY KEY,
                title VARCHAR(255) NOT NULL,
                content TEXT NOT NULL,
                search_vector tsvector
            );

            CREATE INDEX IF NOT EXISTS articles_search_idx ON articles USING gin(search_vector);");
        await container.ExecuteNonQueryAsync();
    }

    private async Task CreateTaggedItemTableWithArraysAsync(IDatabaseContext context)
    {
        await using var container = context.CreateSqlContainer(@"
            CREATE TABLE IF NOT EXISTS tagged_items (
                id BIGSERIAL PRIMARY KEY,
                name VARCHAR(255) NOT NULL,
                tags TEXT[]
            );

            CREATE INDEX IF NOT EXISTS tagged_items_tags_idx ON tagged_items USING gin(tags);");
        await container.ExecuteNonQueryAsync();
    }

    private async Task InsertProductsWithJsonbAsync(IDatabaseContext context)
    {
        var products = new[]
        {
            ("iPhone 15", "{\"brand\": \"Apple\", \"model\": \"15\", \"color\": \"black\"}"),
            ("MacBook Pro", "{\"brand\": \"Apple\", \"model\": \"Pro\", \"screen\": \"14-inch\"}"),
            ("Galaxy S24", "{\"brand\": \"Samsung\", \"model\": \"S24\", \"color\": \"white\"}")
        };

        foreach (var (name, specsJson) in products)
        {
            await using var container = context.CreateSqlContainer(@"
                INSERT INTO products (name, specifications) VALUES (");
            container.Query.Append(container.MakeParameterName("name"));
            container.Query.Append(", ");
            container.Query.Append(container.MakeParameterName("specs"));
            container.Query.Append(")");
            container.AddParameterWithValue("name", DbType.String, name);
            using var specsDoc = JsonDocument.Parse(specsJson);
            container.AddParameterWithValue("specs", DbType.Object, specsDoc);

            await container.ExecuteNonQueryAsync();
        }
    }

    private async Task InsertProductsWithComplexJsonbAsync(IDatabaseContext context)
    {
        var complexProduct = @"{
            ""brand"": ""Apple"",
            ""technical"": {
                ""processor"": ""M3 Pro"",
                ""ram"": ""16GB"",
                ""storage"": ""512GB""
            },
            ""features"": [""TouchID"", ""FaceTime"", ""Retina""]
        }";

        await using var container = context.CreateSqlContainer(@"
            INSERT INTO products (name, specifications) VALUES (");
        container.Query.Append(container.MakeParameterName("name"));
        container.Query.Append(", ");
        container.Query.Append(container.MakeParameterName("specs"));
        container.Query.Append(")");
        container.AddParameterWithValue("name", DbType.String, "MacBook Pro M3");
        using var specsDoc = JsonDocument.Parse(complexProduct);
        container.AddParameterWithValue("specs", DbType.Object, specsDoc);

        await container.ExecuteNonQueryAsync();
    }

    private async Task InsertTaggedItemsAsync(IDatabaseContext context)
    {
        var items = new[]
        {
            ("Premium Headphones", new[] { "premium", "featured", "audio" }),
            ("Basic Mouse", new[] { "basic", "office" }),
            ("Gaming Keyboard", new[] { "gaming", "featured", "rgb" }),
            ("Pro Monitor", new[] { "premium", "featured", "display", "professional" })
        };

        foreach (var (name, tags) in items)
        {
            await using var container = context.CreateSqlContainer(@"
                INSERT INTO tagged_items (name, tags) VALUES (");
            container.Query.Append(container.MakeParameterName("name"));
            container.Query.Append(", ");
            container.Query.Append(container.MakeParameterName("tags"));
            container.Query.Append(")");
            container.AddParameterWithValue("name", DbType.String, name);
            container.AddParameterWithValue("tags", DbType.Object, tags);

            await container.ExecuteNonQueryAsync();
        }
    }

    private async Task InsertArticlesWithFullTextAsync(IDatabaseContext context)
    {
        var articles = new[]
        {
            ("Database Performance Optimization", "Learn about indexing strategies, query optimization techniques, and database tuning for maximum performance."),
            ("Modern Web Development", "Exploring the latest trends in web development including frameworks, tools, and best practices."),
            ("Machine Learning Fundamentals", "Introduction to machine learning algorithms, data preprocessing, and model evaluation techniques."),
            ("Advanced Database Design", "Deep dive into database normalization, optimization patterns, and performance monitoring.")
        };

        foreach (var (title, content) in articles)
        {
            await using var container = context.CreateSqlContainer(@"
                INSERT INTO articles (title, content, search_vector) VALUES (");
            container.Query.Append(container.MakeParameterName("title"));
            container.Query.Append(", ");
            container.Query.Append(container.MakeParameterName("content"));
            container.Query.Append(", to_tsvector('english', ");
            container.Query.Append(container.MakeParameterName("searchText"));
            container.Query.Append("))");

            container.AddParameterWithValue("title", DbType.String, title);
            container.AddParameterWithValue("content", DbType.String, content);
            container.AddParameterWithValue("searchText", DbType.String, $"{title} {content}");

            await container.ExecuteNonQueryAsync();
        }
    }
}
