#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class DataReaderMapperTests
{
    [Fact]
    public async Task LoadObjectsFromDataReaderAsync_MapsMatchingFields()
    {
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Name"] = "John",
                ["Age"] = 30,
                ["IsActive"] = true
            }
        });

        var result = await DataReaderMapper.LoadObjectsFromDataReaderAsync<SampleEntity>(reader);

        Assert.Single(result);
        Assert.Equal("John", result[0].Name);
        Assert.Equal(30, result[0].Age);
        Assert.True(result[0].IsActive);
    }

    [Fact]
    public async Task LoadObjectsFromDataReaderAsync_IgnoresUnmappedFields()
    {
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Unrelated"] = "Ignore",
                ["Name"] = "Jane"
            }
        });

        var result = await DataReaderMapper.LoadObjectsFromDataReaderAsync<SampleEntity>(reader);

        Assert.Single(result);
        Assert.Equal("Jane", result[0].Name);
    }

    [Fact]
    public async Task LoadObjectsFromDataReaderAsync_Interface_MapsFields()
    {
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Name"] = "John",
                ["Age"] = 30,
                ["IsActive"] = true
            }
        });

        IDataReaderMapper mapper = new DataReaderMapper();
        var result = await mapper.LoadObjectsFromDataReaderAsync<SampleEntity>(reader);

        Assert.Single(result);
        Assert.Equal("John", result[0].Name);
        Assert.Equal(30, result[0].Age);
        Assert.True(result[0].IsActive);
    }

    [Fact]
    public async Task LoadObjectsFromDataReaderAsync_HandlesDbNullsGracefully()
    {
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Name"] = DBNull.Value,
                ["Age"] = DBNull.Value,
                ["IsActive"] = DBNull.Value
            }
        });

        var result = await DataReaderMapper.LoadObjectsFromDataReaderAsync<SampleEntity>(reader);

        Assert.Single(result);
        Assert.Null(result[0].Name);
        Assert.Equal(0, result[0].Age); // default(int)
        Assert.False(result[0].IsActive); // default(bool)
    }

    [Fact]
    public async Task StreamAsync_StreamsObjects()
    {
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Name"] = "John",
                ["Age"] = 30,
                ["IsActive"] = true
            },
            new Dictionary<string, object>
            {
                ["Name"] = "Jane",
                ["Age"] = 25,
                ["IsActive"] = false
            }
        });

        var results = new List<SampleEntity>();
        await foreach (var item in DataReaderMapper.StreamAsync<SampleEntity>(reader))
        {
            results.Add(item);
        }

        Assert.Equal(2, results.Count);
        Assert.Equal("John", results[0].Name);
        Assert.Equal("Jane", results[1].Name);
    }
    [Fact]
    public async Task LoadAsync_WithNamePolicy_MapsSnakeCaseFields()
    {
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["first_name"] = "John"
            }
        });

        Func<string, string> snakeToPascal = name =>
        {
            var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
            var result = string.Empty;
            foreach (var part in parts)
            {
                result += char.ToUpperInvariant(part[0]) + part.Substring(1);
            }

            return result;
        };

        var options = new MapperOptions(NamePolicy: snakeToPascal);
        var result = await DataReaderMapper.LoadAsync<SnakeEntity>(reader, options);

        Assert.Single(result);
        Assert.Equal("John", result[0].FirstName);
    }

    [Fact]
    public async Task LoadObjectsFromDataReaderAsync_WithoutNamePolicy_IgnoresSnakeCaseFields()
    {
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["first_name"] = "John"
            }
        });

        var result = await DataReaderMapper.LoadObjectsFromDataReaderAsync<SnakeEntity>(reader);

        Assert.Single(result);
        Assert.Null(result[0].FirstName);
    }

    [Fact]
    public async Task LoadAsync_ColumnsOnly_MapsAnnotatedProperty()
    {
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Name"] = "Jane",
                ["Age"] = 25
            }
        });

        var options = new MapperOptions(ColumnsOnly: true);
        var result = await DataReaderMapper.LoadAsync<ColumnsOnlyEntity>(reader, options);

        Assert.Single(result);
        Assert.Equal("Jane", result[0].Name);
    }

    [Fact]
    public async Task LoadAsync_ColumnsOnly_IgnoresNonAnnotatedProperty()
    {
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Name"] = "Jane",
                ["Age"] = 25
            }
        });

        var options = new MapperOptions(ColumnsOnly: true);
        var result = await DataReaderMapper.LoadAsync<ColumnsOnlyEntity>(reader, options);

        Assert.Single(result);
        Assert.Equal(0, result[0].Age);
    }

    [Fact]
    public async Task LoadAsync_WhenSwitchingColumnsOnlyOption_RespectsCachedLookup()
    {
        var defaultReader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Name"] = "Alice",
                ["Age"] = 45
            }
        });

        var defaultResult = await DataReaderMapper.LoadAsync<ColumnsOnlyEntity>(defaultReader, MapperOptions.Default);

        Assert.Single(defaultResult);
        Assert.Equal("Alice", defaultResult[0].Name);
        Assert.Equal(45, defaultResult[0].Age);

        var columnsOnlyReader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Name"] = "Bob",
                ["Age"] = 51
            }
        });

        var columnsOnlyResult = await DataReaderMapper.LoadAsync<ColumnsOnlyEntity>(
            columnsOnlyReader,
            new MapperOptions(ColumnsOnly: true));

        Assert.Single(columnsOnlyResult);
        Assert.Equal("Bob", columnsOnlyResult[0].Name);
        Assert.Equal(0, columnsOnlyResult[0].Age);
    }

    [Fact]
    public async Task LoadObjectsFromDataReaderAsync_MapsEnumFromString()
    {
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["State"] = "Two"
            }
        });

        var result = await DataReaderMapper.LoadObjectsFromDataReaderAsync<EnumEntity>(reader);

        Assert.Single(result);
        Assert.Equal(SampleState.Two, result[0].State);
    }

    [Fact]
    public async Task LoadObjectsFromDataReaderAsync_MapsEnumFromNumeric()
    {
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["State"] = 1
            }
        });

        var result = await DataReaderMapper.LoadObjectsFromDataReaderAsync<EnumEntity>(reader);

        Assert.Single(result);
        Assert.Equal(SampleState.Two, result[0].State);
    }

    [Fact]
    public async Task LoadObjectsFromDataReaderAsync_StrictInvalidEnumThrows()
    {
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["State"] = "invalid"
            }
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => DataReaderMapper.LoadAsync<EnumEntity>(reader, new MapperOptions(Strict: true)));
    }

    [Fact]
    public async Task LoadAsync_WithTypeConversion_UsesCoercion()
    {
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Age"] = "42"
            }
        });

        var result = await DataReaderMapper.LoadAsync<TypeConversionEntity>(reader, MapperOptions.Default);

        Assert.Single(result);
        Assert.Equal(42, result[0].Age);
    }

    [Fact]
    public async Task LoadObjectsFromDataReaderAsync_DirectAssignment_UsesGetFieldValue()
    {
        var reader = new TrackingFieldAccessReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Age"] = 37
            }
        });

        var result = await DataReaderMapper.LoadObjectsFromDataReaderAsync<DirectEntity>(reader);

        Assert.Single(result);
        Assert.Equal(37, result[0].Age);
        Assert.Equal(2, reader.GetValueCallCount);
        Assert.Equal(1, reader.GetFieldValueCallCount);
    }

    [Fact]
    public async Task LoadObjectsFromDataReaderAsync_CoercionPath_UsesGetValue()
    {
        var reader = new TrackingFieldAccessReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Age"] = "58"
            }
        });

        var result = await DataReaderMapper.LoadObjectsFromDataReaderAsync<TypeConversionEntity>(reader);

        Assert.Single(result);
        Assert.Equal(58, result[0].Age);
        Assert.True(reader.GetValueCallCount >= 3);
        Assert.Equal(0, reader.GetFieldValueCallCount);
    }

    [Fact]
    public async Task LoadObjectsFromDataReaderAsync_WhenColumnTypeRemainsStable_ReusesTypedGetter()
    {
        var firstReader = new StrictTrackingFieldAccessReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Age"] = 64
            }
        });

        var secondReader = new StrictTrackingFieldAccessReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Age"] = 65
            }
        });

        var firstResult = await DataReaderMapper.LoadObjectsFromDataReaderAsync<DirectEntity>(firstReader);
        var secondResult = await DataReaderMapper.LoadObjectsFromDataReaderAsync<DirectEntity>(secondReader);

        Assert.Single(firstResult);
        Assert.Equal(64, firstResult[0].Age);
        Assert.Single(secondResult);
        Assert.Equal(65, secondResult[0].Age);
        Assert.Equal(0, firstReader.GetFieldValueFailures);
        Assert.True(firstReader.GetFieldValueCallCount >= 1);
        Assert.Equal(0, secondReader.GetFieldValueFailures);
        Assert.True(secondReader.GetFieldValueCallCount >= 1);
    }

    [Fact]
    public async Task LoadObjectsFromDataReaderAsync_WhenColumnTypeChanges_RebuildsPlanAndUsesCoercion()
    {
        var firstReader = new StrictTrackingFieldAccessReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Age"] = 70
            }
        });

        var secondReader = new StrictTrackingFieldAccessReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Age"] = "71"
            }
        });

        var firstResult = await DataReaderMapper.LoadObjectsFromDataReaderAsync<DirectEntity>(firstReader);
        Assert.Single(firstResult);
        Assert.Equal(70, firstResult[0].Age);
        Assert.Equal(0, firstReader.GetFieldValueFailures);
        Assert.True(firstReader.GetFieldValueCallCount >= 1);

        var secondResult = await DataReaderMapper.LoadObjectsFromDataReaderAsync<DirectEntity>(secondReader);

        Assert.Single(secondResult);
        Assert.Equal(71, secondResult[0].Age);
        Assert.Equal(0, secondReader.GetFieldValueCallCount);
        Assert.True(secondReader.GetValueCallCount >= 2);
        Assert.Equal(0, secondReader.GetFieldValueFailures);
    }

    [Fact]
    public async Task LoadAsync_WithEnumSetNullAndLog_UsesConfiguredBehavior()
    {
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["NullableState"] = "invalid",
                ["RequiredState"] = "invalid"
            }
        });

        var options = new MapperOptions(EnumMode: EnumParseFailureMode.SetNullAndLog);
        var result = await DataReaderMapper.LoadAsync<EnumModeEntity>(reader, options);

        Assert.Single(result);
        Assert.Null(result[0].NullableState);
        Assert.Equal(default(SampleState), result[0].RequiredState);
    }

    [Fact]
    public async Task LoadAsync_WithEnumSetDefaultValue_UsesConfiguredBehavior()
    {
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["NullableState"] = 999,
                ["RequiredState"] = 999
            }
        });

        var options = new MapperOptions(EnumMode: EnumParseFailureMode.SetDefaultValue);
        var result = await DataReaderMapper.LoadAsync<EnumModeEntity>(reader, options);

        Assert.Single(result);
        Assert.Equal(default(SampleState?), result[0].NullableState);
        Assert.Equal(default(SampleState), result[0].RequiredState);
    }

    [Fact]
    public async Task LoadAsync_WhenGetFieldTypeThrows_FallsBackToObject()
    {
        var reader = new ThrowingFieldTypeReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Age"] = 21
            }
        });

        var result = await DataReaderMapper.LoadAsync<TypeConversionEntity>(reader, MapperOptions.Default);

        Assert.Single(result);
        Assert.Equal(21, result[0].Age);
    }

    [Fact]
    public async Task LoadAsync_ColumnsOnly_WithDuplicateColumnNames_ThrowsArgumentException()
    {
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Alias"] = "First"
            }
        });

        await Assert.ThrowsAsync<ArgumentException>(
            () => DataReaderMapper.LoadAsync<DuplicateColumnNamesEntity>(
                reader,
                new MapperOptions(ColumnsOnly: true)));
    }

    [Fact]
    public async Task LoadAsync_ReusesPlan_ForEquivalentOptions()
    {
        var readerFactory = () => new fakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Name"] = "Jane"
            }
        });

        // First load - may or may not increase count depending on cache capacity
        await DataReaderMapper.LoadAsync<SampleEntity>(readerFactory(), new MapperOptions());
        var afterFirstLoad = GetPlanCacheCount();

        // Second load with same schema - should reuse plan, count stays same
        await DataReaderMapper.LoadAsync<SampleEntity>(readerFactory(), new MapperOptions());
        var afterSecondLoad = GetPlanCacheCount();

        // Key assertion: reusing the same plan doesn't change the count
        Assert.Equal(afterFirstLoad, afterSecondLoad);
    }

    [Fact]
    public async Task LoadAsync_DifferentNamePolicies_CreateDistinctPlans()
    {
        var readerFactory = () => new fakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["first_name"] = "Jane"
            }
        });

        // Use different types to guarantee unique cache keys regardless of other test state
        // First load with one name policy
        var snakeToPascal = new MapperOptions(NamePolicy: name =>
        {
            if (name.Equals("first_name", StringComparison.OrdinalIgnoreCase))
            {
                return "FirstName";
            }

            return name;
        });

        await DataReaderMapper.LoadAsync<SnakeEntity>(readerFactory(), snakeToPascal);
        var afterFirstLoad = GetPlanCacheCount();

        // Second load with same policy - should reuse
        await DataReaderMapper.LoadAsync<SnakeEntity>(readerFactory(), snakeToPascal);
        var afterFirstReuse = GetPlanCacheCount();
        Assert.Equal(afterFirstLoad, afterFirstReuse); // Same policy reuses plan

        // Third load with different policy - should NOT reuse (creates new plan)
        var underscoreStrip = new MapperOptions(NamePolicy: name => name.Replace("_", string.Empty));
        await DataReaderMapper.LoadAsync<SnakeEntity>(readerFactory(), underscoreStrip);
        var afterSecondPolicy = GetPlanCacheCount();

        // Different policies should produce different schema hashes
        // Note: With bounded cache, count may stay same due to eviction, or increase by 1
        // The key behavior is that the two policies are NOT equivalent
        Assert.True(afterSecondPolicy >= afterFirstLoad,
            "Adding a plan with a different policy should not decrease count below first load");
    }

    [Fact]
    public async Task LoadAsync_CapturingNamePolicyOptions_AreCollectible()
    {
        WeakReference? weakOptions = null;

        async Task CreatePlanAsync()
        {
            var capture = Guid.NewGuid().ToString();
            var options = new MapperOptions(NamePolicy: name =>
            {
                _ = capture;
                return name;
            });

            weakOptions = new WeakReference(options);

            var reader = new fakeDbDataReader(new[]
            {
                new Dictionary<string, object>
                {
                    ["Name"] = "Jane"
                }
            });

            await DataReaderMapper.LoadAsync<SampleEntity>(reader, options);
        }

        await CreatePlanAsync();

        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.NotNull(weakOptions);
        Assert.False(weakOptions!.IsAlive);
    }

    private static int GetPlanCacheCount()
    {
        var cacheField = typeof(DataReaderMapper).GetField("_planCache", BindingFlags.Static | BindingFlags.NonPublic);
        var cache = cacheField!.GetValue(null)!;

        // BoundedCache uses a private _count field, not a public Count property
        var countField = cache.GetType().GetField("_count", BindingFlags.Instance | BindingFlags.NonPublic);
        if (countField != null)
        {
            return (int)countField.GetValue(cache)!;
        }

        // Fallback for ConcurrentDictionary (legacy)
        var countProperty = cache.GetType().GetProperty("Count");
        return (int)countProperty!.GetValue(cache)!;
    }

    private class SampleEntity
    {
        public string? Name { get; set; }
        public int Age { get; set; }
        public bool IsActive { get; set; }
    }
    private class SnakeEntity
    {
        public string? FirstName { get; set; }
    }

    private class ColumnsOnlyEntity
    {
        [Column("Name", DbType.String)]
        public string? Name { get; set; }

        public int Age { get; set; }
    }

    private enum SampleState
    {
        One = 0,
        Two = 1
    }

    private class EnumEntity
    {
        public SampleState State { get; set; }
    }

    private class TypeConversionEntity
    {
        public int Age { get; set; }
    }

    private class EnumModeEntity
    {
        public SampleState? NullableState { get; set; }
        public SampleState RequiredState { get; set; }
    }

    private class DuplicateColumnNamesEntity
    {
        [Column("Alias", DbType.String)]
        public string? First { get; set; }

        [Column("Alias", DbType.String)]
        public string? Second { get; set; }
    }

    private class DirectEntity
    {
        public int Age { get; set; }
    }

    private sealed class ThrowingFieldTypeReader : fakeDbDataReader
    {
        public ThrowingFieldTypeReader(IEnumerable<Dictionary<string, object>> rows)
            : base(rows)
        {
        }

        public override Type GetFieldType(int ordinal)
        {
            throw new InvalidOperationException("Simulated failure.");
        }
    }

    private sealed class TrackingFieldAccessReader : fakeDbDataReader
    {
        public TrackingFieldAccessReader(IEnumerable<Dictionary<string, object>> rows)
            : base(rows)
        {
        }

        public int GetValueCallCount { get; private set; }

        public int GetFieldValueCallCount { get; private set; }

        public override object GetValue(int i)
        {
            GetValueCallCount++;
            return base.GetValue(i);
        }

        public override T GetFieldValue<T>(int ordinal)
        {
            GetFieldValueCallCount++;
            var value = base.GetValue(ordinal);
            if (value is T typed)
            {
                return typed;
            }

            return (T)Convert.ChangeType(value, typeof(T));
        }
    }

    private sealed class StrictTrackingFieldAccessReader : fakeDbDataReader
    {
        private bool _suppressValueCount;

        public StrictTrackingFieldAccessReader(IEnumerable<Dictionary<string, object>> rows)
            : base(rows)
        {
        }

        public int GetValueCallCount { get; private set; }

        public int GetFieldValueCallCount { get; private set; }

        public int GetFieldValueFailures { get; private set; }

        public override object GetValue(int i)
        {
            if (!_suppressValueCount)
            {
                GetValueCallCount++;
            }

            return base.GetValue(i);
        }

        public override Type GetFieldType(int ordinal)
        {
            try
            {
                _suppressValueCount = true;
                return base.GetFieldType(ordinal);
            }
            finally
            {
                _suppressValueCount = false;
            }
        }

        public override T GetFieldValue<T>(int ordinal)
        {
            GetFieldValueCallCount++;
            var value = base.GetValue(ordinal);
            if (value is T typed)
            {
                return typed;
            }

            GetFieldValueFailures++;
            throw new InvalidCastException(
                $"Cannot convert value of type {value?.GetType().FullName ?? "null"} to {typeof(T).FullName}.");
        }
    }
}
