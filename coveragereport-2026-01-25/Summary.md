# Summary - pengdows.crud Coverage - Targeting 95%

|||
|:---|:---|
| Generated on: | 1/25/2026 - 6:29:44 AM |
| Coverage date: | 1/19/2026 - 5:32:17 PM - 1/23/2026 - 5:08:52 PM |
| Parser: | MultiReport (2x Cobertura) |
| Assemblies: | 2 |
| Classes: | 179 |
| Files: | 145 |
| **Line coverage:** | 87.2% (14092 of 16154) |
| Covered lines: | 14092 |
| Uncovered lines: | 2062 |
| Coverable lines: | 16154 |
| Total lines: | 26704 |
| **Branch coverage:** | 80.3% (6626 of 8250) |
| Covered branches: | 6626 |
| Total branches: | 8250 |
| **Method coverage:** | [Feature is only available for sponsors](https://reportgenerator.io/pro) |

# Risk Hotspots

| **Assembly** | **Class** | **Method** | **Crap Score** | **Cyclomatic complexity** |
|:---|:---|:---|---:|---:|
| pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | GetFirebirdDataType(...) | 650 | 25 || pengdows.crud | pengdows.crud.types.converters.SpatialConverter<T> | CreateSqlServerSpatial(...) | 572 | 32 || pengdows.crud | pengdows.crud.types.converters.SpatialConverter<T> | CreateSqlServerSpatial(...) | 545 | 32 || pengdows.crud | pengdows.crud.types.converters.PostgreSqlRangeConverter<T> | TryConvertFromProvider(...) | 235 | 30 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | BuildUpdateByKey(...) | 210 | 14 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | BuildUpdateByKey(...) | 210 | 14 || pengdows.crud | pengdows.crud.dialects.PostgreSqlDialect | ConfigureProviderSpecificSettings(...) | 205 | 24 || pengdows.crud | pengdows.crud.dialects.PostgreSqlDialect | ConfigureProviderSpecificSettings(...) | 201 | 24 || pengdows.crud | pengdows.crud.types.coercion.CidrCoercion | TryRead(...) | 195 | 20 || pengdows.crud | pengdows.crud.DatabaseContext | GetSingleWriterConnection(...) | 156 | 12 || pengdows.crud | pengdows.crud.DatabaseContext | GetSingleWriterConnection(...) | 156 | 12 || pengdows.crud | pengdows.crud.types.coercion.InetCoercion | TryRead(...) | 142 | 22 || pengdows.crud | pengdows.crud.types.valueobjects.HStore | Equals(...) | 133 | 16 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | PopulateGeneratedIdAsync() | 120 | 20 || pengdows.crud | pengdows.crud.dialects.SqlDialect | CreateDbParameter(...) | 113 | 60 || pengdows.crud | pengdows.crud.types.converters.PostgreSqlIntervalConverter | TryConvertFromProvider(...) | 113 | 16 || pengdows.crud | pengdows.crud.types.converters.CidrConverter | TryConvertFromProvider(...) | 110 | 18 || pengdows.crud | pengdows.crud.types.converters.GeographyConverter | ExtractSridFromEwkb(...) | 110 | 10 || pengdows.crud | pengdows.crud.types.valueobjects.Cidr | Equals(...) | 110 | 10 || pengdows.crud | pengdows.crud.types.coercion.MacAddressCoercion | TryRead(...) | 100 | 16 || pengdows.crud | pengdows.crud.types.converters.InetConverter | TryConvertFromProvider(...) | 98 | 20 || pengdows.crud | pengdows.crud.dialects.SqlDialect | CreateDbParameter(...) | 96 | 62 || pengdows.crud | pengdows.crud.types.converters.CidrConverter | TryConvertFromProvider(...) | 92 | 16 || pengdows.crud | pengdows.crud.types.converters.GeometryConverter | ExtractSridFromGeoJson(...) | 91 | 14 || pengdows.crud | pengdows.crud.types.converters.GeometryConverter | ExtractSridFromGeoJson(...) | 90 | 14 || pengdows.crud | pengdows.crud.types.converters.InetConverter | TryConvertFromProvider(...) | 85 | 18 || pengdows.crud | pengdows.crud.types.converters.PostgreSqlIntervalConverter | FormatIso8601(...) | 83 | 16 || pengdows.crud | pengdows.crud.DatabaseContext | .ctor(...) | 82 | 50 || pengdows.crud | pengdows.crud.types.converters.SpatialConverter<T> | FromProviderSpecific(...) | 77 | 24 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | CreateAsync() | 76 | 22 || pengdows.crud | pengdows.crud.types.valueobjects.MacAddress | Equals(...) | 72 | 8 || pengdows.crud | pengdows.crud.types.converters.MacAddressConverter | TryConvertFromProvider(...) | 71 | 16 || pengdows.crud | pengdows.crud.dialects.SqlDialect | ExtractProductNameFromVersion(...) | 70 | 20 || pengdows.crud | pengdows.crud.DatabaseContext | FactoryCreateConnection(...) | 68 | 68 || pengdows.crud | pengdows.crud.TypeCoercionHelper | CoerceBoolean(...) | 65 | 34 || pengdows.crud | pengdows.crud.DatabaseContext | FactoryCreateConnection(...) | 62 | 62 || pengdows.crud | pengdows.crud.SqlContainer | PrepareAndCreateCommandAsync() | 62 | 48 || pengdows.crud | pengdows.crud.SqlContainer | PrepareAndCreateCommandAsync() | 61 | 48 || pengdows.crud | pengdows.crud.types.converters.MacAddressConverter | TryConvertFromProvider(...) | 61 | 14 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | CreateAsync() | 60 | 22 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | BuildWhereInternal(...) | 60 | 50 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | PopulateGeneratedIdAsync() | 59 | 18 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | LoadStreamAsync() | 58 | 12 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | BuildWhereInternal(...) | 57 | 50 || pengdows.crud | pengdows.crud.DatabaseContext | .ctor(...) | 56 | 50 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | SetAuditFields(...) | 56 | 56 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | SetAuditFields(...) | 56 | 56 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | BuildUpsertMerge(...) | 56 | 52 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | BuildUpsertMerge(...) | 55 | 54 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | BuildFirebirdMergeUpsert(...) | 55 | 54 || pengdows.crud | pengdows.crud.dialects.FirebirdDialect | ParseVersion(...) | 54 | 18 || pengdows.crud | pengdows.crud.DatabaseContext | .ctor(...) | 52 | 48 || pengdows.crud | pengdows.crud.DatabaseContext | InitializeInternals(...) | 52 | 52 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | BuildUpsertOnConflict(...) | 51 | 48 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | CreateTemplateRowId() | 50 | 8 || pengdows.crud | pengdows.crud.types.valueobjects.IntervalDaySecond | Parse(...) | 50 | 50 || pengdows.crud | pengdows.crud.types.valueobjects.IntervalDaySecond | Parse(...) | 50 | 50 || pengdows.crud | pengdows.crud.DatabaseContext | InitializeInternals(...) | 49 | 48 || pengdows.crud | pengdows.crud.DatabaseContext | .ctor(...) | 49 | 48 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | BuildUpsertOnConflict(...) | 48 | 48 || pengdows.crud | pengdows.crud.types.converters.PostgreSqlIntervalConverter | FormatIso8601(...) | 48 | 16 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | BuildValueExtractor(...) | 47 | 47 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | BuildValueExtractor(...) | 47 | 47 || pengdows.crud | pengdows.crud.SqlContainer | ExecuteScalarWriteAsync() | 46 | 46 || pengdows.crud | pengdows.crud.DatabaseContext | CoerceMode(...) | 45 | 45 || pengdows.crud | pengdows.crud.DatabaseContext | CoerceMode(...) | 45 | 45 || pengdows.crud | pengdows.crud.dialects.SqlDialect | ExtractProductNameFromVersion(...) | 44 | 20 || pengdows.crud | pengdows.crud.SqlContainer | ExecuteScalarWriteAsync() | 44 | 44 || pengdows.crud | pengdows.crud.DatabaseContext | .ctor(...) | 42 | 6 || pengdows.crud | pengdows.crud.DatabaseContext | .ctor(...) | 42 | 6 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | ResolveUpsertKey_MOVED() | 42 | 6 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | ResolveUpsertKey_MOVED() | 42 | 6 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | ResolveUpsertKey() | 42 | 6 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | ResolveUpsertKey() | 42 | 6 || pengdows.crud | pengdows.crud.dialects.FirebirdDialect | ParseVersion(...) | 41 | 18 || pengdows.crud | pengdows.crud.types.converters.IntervalDaySecondConverter | Parse(...) | 41 | 40 || pengdows.crud | pengdows.crud.types.converters.IntervalDaySecondConverter | Parse(...) | 40 | 40 || pengdows.crud | pengdows.crud.DatabaseContext | InitializePoolGovernors() | 39 | 38 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | CreateTemplateRowId() | 39 | 8 || pengdows.crud | pengdows.crud.TypeCoercionHelper | CoerceBoolean(...) | 38 | 34 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | BuildUpsertOnDuplicate(...) | 37 | 34 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | BuildUpsertOnDuplicate(...) | 37 | 36 || pengdows.crud | pengdows.crud.ReflectionSerializer | Deserialize(...) | 37 | 34 || pengdows.crud | pengdows.crud.ReflectionSerializer | Deserialize(...) | 37 | 34 || pengdows.crud | pengdows.crud.internal.ConnectionPoolingConfiguration | ApplyPoolingDefaults(...) | 36 | 36 || pengdows.crud | pengdows.crud.internal.ConnectionPoolingConfiguration | ApplyPoolingDefaults(...) | 36 | 36 || pengdows.crud | pengdows.crud.types.coercion.BooleanCoercion | TryRead(...) | 36 | 36 || pengdows.crud | pengdows.crud.types.coercion.BooleanCoercion | TryRead(...) | 36 | 36 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | LoadStreamAsync() | 35 | 12 || pengdows.crud | pengdows.crud.SqlContainer | CreateCommand(...) | 35 | 16 || pengdows.crud | pengdows.crud.TypeMapRegistry | ProcessProperty(...) | 35 | 34 || pengdows.crud | pengdows.crud.collections.OrderedDictionary<T1, T2> | TryInsert(...) | 34 | 28 || pengdows.crud | pengdows.crud.TypeMapRegistry | ProcessProperty(...) | 34 | 34 || pengdows.crud | pengdows.crud.types.AdvancedTypeRegistry | TryConfigureParameter(...) | 34 | 28 || pengdows.crud | pengdows.crud.SqlContainer | BuildProcedureArguments() | 31 | 14 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | CreateAsync() | 30 | 30 || pengdows.crud | pengdows.crud.collections.OrderedDictionary<T1, T2> | TryInsert(...) | 29 | 28 || pengdows.crud | pengdows.crud.DatabaseContext | DetectInMemoryKind(...) | 28 | 28 || pengdows.crud | pengdows.crud.DatabaseContext | DetectInMemoryKind(...) | 28 | 28 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | BuildCreate(...) | 28 | 28 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | BuildCreate(...) | 28 | 28 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | CreateAsync() | 28 | 28 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | BuildCachedSqlTemplatesForDialect(...) | 28 | 28 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | BuildCachedSqlTemplatesForDialect(...) | 28 | 28 || pengdows.crud | pengdows.crud.internal.PoolingConfigReader | GetEffectivePoolConfig(...) | 29 | 28 || pengdows.crud | pengdows.crud.TransactionContext | .ctor(...) | 28 | 28 || pengdows.crud | pengdows.crud.types.coercion.ProviderParameterFactory | ApplyPostgreSqlOptimizations(...) | 29 | 28 || pengdows.crud | pengdows.crud.types.coercion.ProviderParameterFactory | ApplyPostgreSqlOptimizations(...) | 29 | 28 || pengdows.crud | pengdows.crud.types.converters.PostgreSqlRangeConverter<T> | TryConvertFromProvider(...) | 30 | 28 || pengdows.crud | pengdows.crud.dialects.SqlDialectFactory | InferDatabaseTypeFromName(...) | 26 | 26 || pengdows.crud | pengdows.crud.dialects.SqlDialectFactory | InferDatabaseTypeFromName(...) | 26 | 26 || pengdows.crud | pengdows.crud.TypeCoercionHelper | CoerceGuid(...) | 26 | 26 || pengdows.crud | pengdows.crud.TypeCoercionHelper | ToJsonDocument(...) | 27 | 26 || pengdows.crud | pengdows.crud.TypeCoercionHelper | ToJsonDocument(...) | 26 | 26 || pengdows.crud | pengdows.crud.types.coercion.GuidCoercion | TryRead(...) | 26 | 26 || pengdows.crud | pengdows.crud.types.coercion.GuidCoercion | TryRead(...) | 26 | 26 || pengdows.crud | pengdows.crud.types.converters.PostgreSqlIntervalConverter | Parse(...) | 26 | 26 || pengdows.crud | pengdows.crud.types.converters.PostgreSqlIntervalConverter | Parse(...) | 26 | 26 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | ValuesAreEqual(...) | 25 | 25 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | ValuesAreEqual(...) | 25 | 25 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | RetrieveAsync() | 25 | 24 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | RetrieveAsync() | 25 | 24 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | BuildUpdateAsync() | 24 | 24 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | BuildUpdateAsync() | 24 | 24 || pengdows.crud | pengdows.crud.SqlContainer | ExecuteNonQueryAsync() | 24 | 24 || pengdows.crud | pengdows.crud.SqlContainer | ExecuteNonQueryAsync() | 24 | 24 || pengdows.crud | pengdows.crud.threading.RealAsyncLocker | LockAsync() | 24 | 24 || pengdows.crud | pengdows.crud.TypeCoercionHelper | CoerceJsonValue(...) | 24 | 24 || pengdows.crud | pengdows.crud.TypeCoercionHelper | ExtractJsonString(...) | 24 | 24 || pengdows.crud | pengdows.crud.TypeCoercionHelper | CoerceGuid(...) | 24 | 24 || pengdows.crud | pengdows.crud.TypeCoercionHelper | CoerceJsonValue(...) | 24 | 24 || pengdows.crud | pengdows.crud.TypeCoercionHelper | ExtractJsonString(...) | 24 | 24 || pengdows.crud | pengdows.crud.TypeMapRegistry | ValidatePrimaryKeys(...) | 24 | 24 || pengdows.crud | pengdows.crud.TypeMapRegistry | ValidatePrimaryKeys(...) | 24 | 24 || pengdows.crud | pengdows.crud.types.AdvancedTypeRegistry | TryConfigureParameter(...) | 26 | 24 || pengdows.crud | pengdows.crud.types.valueobjects.IntervalYearMonth | Parse(...) | 24 | 24 || pengdows.crud | pengdows.crud.types.valueobjects.IntervalYearMonth | Parse(...) | 24 | 24 || pengdows.crud | pengdows.crud.isolation.IsolationResolver | BuildSupportedIsolationLevels(...) | 23 | 23 || pengdows.crud | pengdows.crud.isolation.IsolationResolver | BuildProfileMapping(...) | 23 | 23 || pengdows.crud | pengdows.crud.isolation.IsolationResolver | BuildSupportedIsolationLevels(...) | 23 | 23 || pengdows.crud | pengdows.crud.isolation.IsolationResolver | BuildProfileMapping(...) | 23 | 23 || pengdows.crud | pengdows.crud.dialects.SqlDialect | InferDatabaseTypeFromInfo(...) | 22 | 22 || pengdows.crud | pengdows.crud.dialects.SqlDialect | InferDatabaseTypeFromInfo(...) | 22 | 22 || pengdows.crud | pengdows.crud.TypeCoercionHelper | CoerceCore(...) | 22 | 22 || pengdows.crud | pengdows.crud.TypeCoercionHelper | CoerceCore(...) | 22 | 22 || pengdows.crud | pengdows.crud.types.coercion.InetCoercion | TryRead(...) | 23 | 22 || pengdows.crud | pengdows.crud.types.converters.SpatialConverter<T> | FromProviderSpecific(...) | 22 | 22 || pengdows.crud | pengdows.crud.Utils | IsZeroNumeric(...) | 22 | 22 || pengdows.crud | pengdows.crud.Utils | IsZeroNumeric(...) | 22 | 22 || pengdows.crud | pengdows.crud.dialects.SqlDialectFactory | CreateDialectForType(...) | 21 | 21 || pengdows.crud | pengdows.crud.dialects.SqlDialectFactory | CreateDialectForType(...) | 21 | 21 || pengdows.crud | pengdows.crud.configuration.DbProviderLoader | LoadProviderFactory(...) | 20 | 20 || pengdows.crud | pengdows.crud.configuration.DbProviderLoader | LoadProviderFactory(...) | 20 | 20 || pengdows.crud | pengdows.crud.DatabaseContext | RedactConnectionString(...) | 20 | 20 || pengdows.crud | pengdows.crud.DatabaseContext | BeginTransaction(...) | 21 | 20 || pengdows.crud | pengdows.crud.DatabaseContext | BeginTransaction(...) | 20 | 20 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | Initialize(...) | 20 | 20 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | Initialize(...) | 20 | 20 || pengdows.crud | pengdows.crud.internal.DatabaseDetectionService | DetectFromConnection(...) | 20 | 20 || pengdows.crud | pengdows.crud.internal.DatabaseDetectionService | DetectTopology(...) | 20 | 20 || pengdows.crud | pengdows.crud.internal.DatabaseDetectionService | DetectFromConnection(...) | 20 | 20 || pengdows.crud | pengdows.crud.internal.DatabaseDetectionService | DetectTopology(...) | 20 | 20 || pengdows.crud | pengdows.crud.ReflectionSerializer | Serialize(...) | 22 | 20 || pengdows.crud | pengdows.crud.ReflectionSerializer | Serialize(...) | 20 | 20 || pengdows.crud | pengdows.crud.SqlContainer | MaybePrepareCommand(...) | 21 | 20 || pengdows.crud | pengdows.crud.SqlContainer | MaybePrepareCommand(...) | 20 | 20 || pengdows.crud | pengdows.crud.types.coercion.CidrCoercion | TryRead(...) | 23 | 20 || pengdows.crud | pengdows.crud.types.valueobjects.HStore | EscapeHStoreValue(...) | 21 | 20 || pengdows.crud | pengdows.crud.types.valueobjects.HStore | EscapeHStoreValue(...) | 20 | 20 || pengdows.crud | pengdows.crud.dialects.SqlDialect | IsVersionAtLeast(...) | 18 | 18 || pengdows.crud | pengdows.crud.dialects.SqlDialect | IsValidParameterName(...) | 21 | 18 || pengdows.crud | pengdows.crud.dialects.SqlDialect | IsVersionAtLeast(...) | 18 | 18 || pengdows.crud | pengdows.crud.dialects.SqlDialect | IsValidParameterName(...) | 21 | 18 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | EnsureWritableIdHasValue(...) | 27 | 18 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | EnsureWritableIdHasValue(...) | 23 | 18 || pengdows.crud | pengdows.crud.internal.SessionSettingsConfigurator | GetSessionSettings(...) | 18 | 18 || pengdows.crud | pengdows.crud.internal.SessionSettingsConfigurator | GetSessionSettings(...) | 18 | 18 || pengdows.crud | pengdows.crud.SqlContainer | ExecuteReaderAsync() | 18 | 18 || pengdows.crud | pengdows.crud.SqlContainer | ExecuteReaderSingleRowAsync() | 19 | 18 || pengdows.crud | pengdows.crud.SqlContainer | ExecuteReaderAsync() | 18 | 18 || pengdows.crud | pengdows.crud.SqlContainer | ExecuteReaderSingleRowAsync() | 18 | 18 || pengdows.crud | pengdows.crud.TypeMapRegistry | AssignOrdinals(...) | 22 | 18 || pengdows.crud | pengdows.crud.TypeMapRegistry | IsNumericDbType(...) | 18 | 18 || pengdows.crud | pengdows.crud.TypeMapRegistry | AssignOrdinals(...) | 18 | 18 || pengdows.crud | pengdows.crud.types.coercion.MacAddressCoercion | TryRead(...) | 21 | 18 || pengdows.crud | pengdows.crud.types.coercion.ParameterBindingRules | ApplyBindingRules(...) | 18 | 18 || pengdows.crud | pengdows.crud.types.coercion.ParameterBindingRules | ApplyLargeObjectBinding(...) | 18 | 18 || pengdows.crud | pengdows.crud.types.coercion.ParameterBindingRules | ApplyBindingRules(...) | 18 | 18 || pengdows.crud | pengdows.crud.types.coercion.ParameterBindingRules | ApplyLargeObjectBinding(...) | 18 | 18 || pengdows.crud | pengdows.crud.types.converters.IntervalYearMonthConverter | Parse(...) | 19 | 18 || pengdows.crud | pengdows.crud.types.converters.IntervalYearMonthConverter | Parse(...) | 18 | 18 || pengdows.crud | pengdows.crud.types.converters.PostgreSqlIntervalConverter | ParseTimeComponent(...) | 18 | 18 || pengdows.crud | pengdows.crud.types.converters.PostgreSqlIntervalConverter | ParseTimeComponent(...) | 18 | 18 || pengdows.crud | pengdows.crud.types.valueobjects.Inet | Equals(...) | 19 | 18 || pengdows.crud | pengdows.crud.types.valueobjects.Inet | Equals(...) | 21 | 18 || pengdows.crud | pengdows.crud.types.valueobjects.Range<T> | Parse(...) | 20 | 18 || pengdows.crud | pengdows.crud.types.valueobjects.Range<T> | Parse(...) | 21 | 18 || pengdows.crud | pengdows.crud.Uuid7Optimized | Configure(...) | 18 | 18 || pengdows.crud | pengdows.crud.Uuid7Optimized | Configure(...) | 18 | 18 || pengdows.crud | pengdows.crud.types.converters.SpatialConverter<T> | ConvertToProvider(...) | 18 | 17 || pengdows.crud | pengdows.crud.types.converters.SpatialConverter<T> | ConvertToProvider(...) | 17 | 17 || pengdows.crud | pengdows.crud.collections.OrderedDictionary<T1, T2> | Remove(...) | 22 | 16 || pengdows.crud | pengdows.crud.collections.OrderedDictionary<T1, T2> | Remove(...) | 21 | 16 || pengdows.crud | pengdows.crud.DatabaseContext | WarnOnModeMismatch(...) | 16 | 16 || pengdows.crud | pengdows.crud.DatabaseContext | WarnOnModeMismatch(...) | 16 | 16 || pengdows.crud | pengdows.crud.DataReaderMapper | StreamInternalAsync() | 16 | 16 || pengdows.crud | pengdows.crud.DataReaderMapper | StreamInternalAsync() | 16 | 16 || pengdows.crud | pengdows.crud.dialects.SqlDialectFactory | InferDatabaseTypeFromProvider(...) | 16 | 16 || pengdows.crud | pengdows.crud.dialects.SqlDialectFactory | InferDatabaseTypeFromProvider(...) | 16 | 16 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | MaterializeDistinctIds(...) | 24 | 16 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | IsDefaultId(...) | 23 | 16 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | MaterializeDistinctIds(...) | 19 | 16 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | BuildSetClause(...) | 17 | 16 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | BuildSetClause(...) | 16 | 16 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | BuildUpsert(...) | 17 | 16 || pengdows.crud | pengdows.crud.EntityHelper<T1, T2> | BuildUpsert(...) | 17 | 16 || pengdows.crud | pengdows.crud.SqlContainer | CreateCommand(...) | 23 | 16 || pengdows.crud | pengdows.crud.TypeCoercionHelper | Coerce(...) | 16 | 16 || pengdows.crud | pengdows.crud.TypeCoercionHelper | Coerce(...) | 16 | 16 || pengdows.crud | pengdows.crud.types.coercion.ParameterBindingRules | EnsureDateTimeParameterization(...) | 16 | 16 || pengdows.crud | pengdows.crud.types.coercion.ProviderParameterFactory | ApplySqlServerOptimizations(...) | 17 | 16 || pengdows.crud | pengdows.crud.types.coercion.ProviderParameterFactory | ApplySqlServerOptimizations(...) | 16 | 16 || pengdows.crud | pengdows.crud.types.converters.PostgreSqlIntervalConverter | TryConvertFromProvider(...) | 17 | 16 || pengdows.crud | pengdows.crud.types.valueobjects.HStore | Equals(...) | 16 | 16 |
# Coverage

| **Name** | **Covered** | **Uncovered** | **Coverable** | **Total** | **Line coverage** | **Covered** | **Total** | **Branch coverage** |
|:---|---:|---:|---:|---:|---:|---:|---:|---:|
| **pengdows.crud** | **14015** | **2049** | **16064** | **52918** | **87.2%** | **6613** | **8234** | **80.3%** |
| pengdows.crud.attributes.ColumnAttribute | 9 | 0 | 9 | 22 | 100% | 0 | 0 |  |
| pengdows.crud.attributes.EnumColumnAttribute | 8 | 0 | 8 | 16 | 100% | 2 | 2 | 100% |
| pengdows.crud.attributes.IdAttribute | 5 | 0 | 5 | 15 | 100% | 0 | 0 |  |
| pengdows.crud.attributes.JsonAttribute | 1 | 0 | 1 | 13 | 100% | 0 | 0 |  |
| pengdows.crud.attributes.PrimaryKeyAttribute | 9 | 0 | 9 | 18 | 100% | 0 | 0 |  |
| pengdows.crud.attributes.TableAttribute | 7 | 0 | 7 | 14 | 100% | 0 | 0 |  |
| pengdows.crud.AuditValues | 7 | 0 | 7 | 17 | 100% | 0 | 0 |  |
| pengdows.crud.collections.OrderedDictionary<T1, T2> | 482 | 64 | 546 | 872 | 88.2% | 128 | 148 | 86.4% |
| pengdows.crud.collections.OrderedDictionaryExtensions | 46 | 1 | 47 | 111 | 97.8% | 16 | 18 | 88.8% |
| pengdows.crud.ColumnInfo | 48 | 0 | 48 | 69 | 100% | 12 | 14 | 85.7% |
| pengdows.crud.configuration.DatabaseContextConfiguration | 15 | 0 | 15 | 71 | 100% | 2 | 2 | 100% |
| pengdows.crud.configuration.DatabaseProviderConfig | 4 | 0 | 4 | 9 | 100% | 0 | 0 |  |
| pengdows.crud.configuration.DbProviderLoader | 125 | 16 | 141 | 195 | 88.6% | 28 | 28 | 100% |
| pengdows.crud.DatabaseContext | 1322 | 148 | 1470 | 2278 | 89.9% | 809 | 1006 | 80.4% |
| pengdows.crud.DataReaderMapper | 267 | 15 | 282 | 474 | 94.6% | 84 | 96 | 87.5% |
| pengdows.crud.DataSourceInformation | 90 | 1 | 91 | 137 | 98.9% | 13 | 14 | 92.8% |
| pengdows.crud.DecimalHelpers | 35 | 0 | 35 | 59 | 100% | 10 | 10 | 100% |
| pengdows.crud.diagnostics.EventIds | 5 | 0 | 5 | 52 | 100% | 0 | 0 |  |
| pengdows.crud.dialects.DatabaseProductInfo | 5 | 0 | 5 | 15 | 100% | 0 | 0 |  |
| pengdows.crud.dialects.DuckDbDialect | 208 | 13 | 221 | 303 | 94.1% | 103 | 128 | 80.4% |
| pengdows.crud.dialects.FirebirdDialect | 171 | 19 | 190 | 250 | 90% | 91 | 132 | 68.9% |
| pengdows.crud.dialects.MariaDbDialect | 52 | 2 | 54 | 110 | 96.2% | 24 | 26 | 92.3% |
| pengdows.crud.dialects.MySqlDialect | 151 | 5 | 156 | 193 | 96.7% | 57 | 86 | 66.2% |
| pengdows.crud.dialects.OracleDialect | 100 | 8 | 108 | 158 | 92.5% | 47 | 58 | 81% |
| pengdows.crud.dialects.PostgreSqlDialect | 198 | 49 | 247 | 342 | 80.1% | 52 | 100 | 52% |
| pengdows.crud.dialects.Sql92Dialect | 5 | 0 | 5 | 19 | 100% | 0 | 0 |  |
| pengdows.crud.dialects.SqlDialect | 959 | 126 | 1085 | 1531 | 88.3% | 583 | 710 | 82.1% |
| pengdows.crud.dialects.SqlDialectFactory | 77 | 1 | 78 | 155 | 98.7% | 67 | 73 | 91.7% |
| pengdows.crud.dialects.SqliteDialect | 127 | 15 | 142 | 209 | 89.4% | 48 | 70 | 68.5% |
| pengdows.crud.dialects.SqlServerDialect | 171 | 13 | 184 | 245 | 92.9% | 34 | 56 | 60.7% |
| pengdows.crud.EntityHelper<T1, T2> | 2012 | 350 | 2362 | 3210 | 85.1% | 1242 | 1564 | 79.4% |
| pengdows.crud.EphemeralSecureString | 67 | 2 | 69 | 112 | 97.1% | 9 | 12 | 75% |
| pengdows.crud.exceptions.ConnectionFailedException | 3 | 0 | 3 | 9 | 100% | 0 | 0 |  |
| pengdows.crud.exceptions.InvalidValueException | 3 | 0 | 3 | 8 | 100% | 0 | 0 |  |
| pengdows.crud.exceptions.ModeContentionException | 7 | 1 | 8 | 20 | 87.5% | 0 | 0 |  |
| pengdows.crud.exceptions.NoColumnsFoundException | 3 | 0 | 3 | 8 | 100% | 0 | 0 |  |
| pengdows.crud.exceptions.PoolSaturatedException | 9 | 1 | 10 | 22 | 90% | 0 | 0 |  |
| pengdows.crud.exceptions.PrimaryKeyOnRowIdColumn | 3 | 0 | 3 | 9 | 100% | 0 | 0 |  |
| pengdows.crud.exceptions.TooManyColumns | 3 | 0 | 3 | 9 | 100% | 0 | 0 |  |
| pengdows.crud.exceptions.TooManyParametersException | 5 | 0 | 5 | 11 | 100% | 0 | 0 |  |
| pengdows.crud.exceptions.TransactionModeNotSupportedException | 3 | 0 | 3 | 15 | 100% | 0 | 0 |  |
| pengdows.crud.infrastructure.PoolGovernor | 59 | 16 | 75 | 195 | 78.6% | 20 | 28 | 71.4% |
| pengdows.crud.infrastructure.PoolPermit | 12 | 0 | 12 | 44 | 100% | 3 | 4 | 75% |
| pengdows.crud.infrastructure.SafeAsyncDisposableBase | 76 | 4 | 80 | 139 | 95% | 13 | 14 | 92.8% |
| pengdows.crud.infrastructure.StringBuilderPool | 26 | 5 | 31 | 65 | 83.8% | 8 | 10 | 80% |
| pengdows.crud.internal.BoundedCache<T1, T2> | 35 | 0 | 35 | 58 | 100% | 12 | 12 | 100% |
| pengdows.crud.internal.ClauseCounters | 6 | 0 | 6 | 18 | 100% | 0 | 0 |  |
| pengdows.crud.internal.ConnectionPoolingConfiguration | 125 | 17 | 142 | 268 | 88% | 73 | 84 | 86.9% |
| pengdows.crud.internal.ConnectionStringHelper | 33 | 10 | 43 | 75 | 76.7% | 11 | 18 | 61.1% |
| pengdows.crud.internal.DatabaseDetectionService | 130 | 10 | 140 | 232 | 92.8% | 63 | 70 | 90% |
| pengdows.crud.internal.DatabaseTopology | 1 | 0 | 1 | 232 | 100% | 0 | 0 |  |
| pengdows.crud.internal.MetricsCollector | 288 | 35 | 323 | 525 | 89.1% | 75 | 86 | 87.2% |
| pengdows.crud.internal.PoolConfig | 1 | 0 | 1 | 145 | 100% | 0 | 0 |  |
| pengdows.crud.internal.PoolingConfigReader | 54 | 14 | 68 | 145 | 79.4% | 35 | 48 | 72.9% |
| pengdows.crud.internal.SbLite | 1 | 0 | 1 | 12 | 100% | 0 | 0 |  |
| pengdows.crud.internal.SessionSettingsConfigurator | 46 | 3 | 49 | 91 | 93.8% | 24 | 24 | 100% |
| pengdows.crud.internal.StringBuilderLite | 60 | 11 | 71 | 120 | 84.5% | 11 | 18 | 61.1% |
| pengdows.crud.isolation.IsolationResolver | 175 | 7 | 182 | 227 | 96.1% | 62 | 64 | 96.8% |
| pengdows.crud.metrics.AttributionSnapshot | 0 | 8 | 8 | 44 | 0% | 0 | 0 |  |
| pengdows.crud.metrics.AttributionStats | 2 | 15 | 17 | 44 | 11.7% | 0 | 0 |  |
| pengdows.crud.metrics.ModeContentionSnapshot | 5 | 1 | 6 | 76 | 83.3% | 0 | 0 |  |
| pengdows.crud.metrics.ModeContentionStats | 26 | 0 | 26 | 76 | 100% | 8 | 8 | 100% |
| pengdows.crud.metrics.PoolStatisticsSnapshot | 7 | 2 | 9 | 15 | 77.7% | 0 | 0 |  |
| pengdows.crud.ReflectionSerializer | 90 | 14 | 104 | 151 | 86.5% | 51 | 58 | 87.9% |
| pengdows.crud.SqlContainer | 745 | 113 | 858 | 1297 | 86.8% | 292 | 368 | 79.3% |
| pengdows.crud.SqlContainerExtensions | 22 | 18 | 40 | 94 | 55% | 8 | 14 | 57.1% |
| pengdows.crud.strategies.connection.ConnectionStrategyFactory | 9 | 1 | 10 | 51 | 90% | 4 | 6 | 66.6% |
| pengdows.crud.strategies.connection.KeepAliveConnectionStrategy | 55 | 20 | 75 | 169 | 73.3% | 18 | 26 | 69.2% |
| pengdows.crud.strategies.connection.KeepAliveConnectionStrategyTestExtensions | 10 | 0 | 10 | 169 | 100% | 0 | 0 |  |
| pengdows.crud.strategies.connection.SingleConnectionStrategy | 36 | 3 | 39 | 115 | 92.3% | 14 | 18 | 77.7% |
| pengdows.crud.strategies.connection.SingleWriterConnectionStrategy | 39 | 3 | 42 | 116 | 92.8% | 16 | 20 | 80% |
| pengdows.crud.strategies.connection.StandardConnectionStrategy | 29 | 2 | 31 | 88 | 93.5% | 9 | 12 | 75% |
| pengdows.crud.strategies.proc.CallProcWrappingStrategy | 7 | 0 | 7 | 17 | 100% | 4 | 6 | 66.6% |
| pengdows.crud.strategies.proc.ExecProcWrappingStrategy | 7 | 0 | 7 | 17 | 100% | 6 | 8 | 75% |
| pengdows.crud.strategies.proc.ExecuteProcedureWrappingStrategy | 7 | 2 | 9 | 19 | 77.7% | 5 | 8 | 62.5% |
| pengdows.crud.strategies.proc.OracleProcWrappingStrategy | 7 | 0 | 7 | 17 | 100% | 6 | 8 | 75% |
| pengdows.crud.strategies.proc.PostgresProcWrappingStrategy | 7 | 2 | 9 | 19 | 77.7% | 5 | 8 | 62.5% |
| pengdows.crud.strategies.proc.ProcWrappingStrategyFactory | 15 | 0 | 15 | 25 | 100% | 2 | 2 | 100% |
| pengdows.crud.strategies.proc.UnsupportedProcWrappingStrategy | 2 | 0 | 2 | 11 | 100% | 0 | 0 |  |
| pengdows.crud.StubAuditValueResolver | 11 | 0 | 11 | 20 | 100% | 0 | 0 |  |
| pengdows.crud.TableInfo | 30 | 0 | 30 | 65 | 100% | 4 | 4 | 100% |
| pengdows.crud.tenant.MultiTenantOptions | 1 | 0 | 1 | 7 | 100% | 0 | 0 |  |
| pengdows.crud.tenant.TenantConfiguration | 2 | 0 | 2 | 9 | 100% | 0 | 0 |  |
| pengdows.crud.tenant.TenantConnectionResolver | 48 | 8 | 56 | 100 | 85.7% | 20 | 26 | 76.9% |
| pengdows.crud.tenant.TenantContextRegistry | 42 | 11 | 53 | 88 | 79.2% | 6 | 8 | 75% |
| pengdows.crud.tenant.TenantServiceCollectionExtensions | 16 | 0 | 16 | 34 | 100% | 4 | 4 | 100% |
| pengdows.crud.threading.NoOpAsyncLocker | 11 | 0 | 11 | 31 | 100% | 0 | 0 |  |
| pengdows.crud.threading.RealAsyncLocker | 101 | 6 | 107 | 228 | 94.3% | 46 | 56 | 82.1% |
| pengdows.crud.TransactionContext | 309 | 43 | 352 | 567 | 87.7% | 68 | 86 | 79% |
| pengdows.crud.TypeCoercionHelper | 297 | 11 | 308 | 548 | 96.4% | 292 | 312 | 93.5% |
| pengdows.crud.TypeMapRegistry | 321 | 23 | 344 | 530 | 93.3% | 193 | 202 | 95.5% |
| pengdows.crud.types.AdvancedTypeRegistry | 367 | 16 | 383 | 620 | 95.8% | 61 | 76 | 80.2% |
| pengdows.crud.types.attributes.ComputedAttribute | 1 | 0 | 1 | 164 | 100% | 0 | 0 |  |
| pengdows.crud.types.attributes.CurrencyAttribute | 5 | 0 | 5 | 164 | 100% | 2 | 2 | 100% |
| pengdows.crud.types.attributes.DbEnumAttribute | 2 | 0 | 2 | 164 | 100% | 0 | 0 |  |
| pengdows.crud.types.attributes.JsonContractAttribute | 5 | 0 | 5 | 164 | 100% | 2 | 2 | 100% |
| pengdows.crud.types.attributes.MaxLengthForInlineAttribute | 7 | 0 | 7 | 164 | 100% | 2 | 2 | 100% |
| pengdows.crud.types.attributes.RangeTypeAttribute | 1 | 0 | 1 | 164 | 100% | 0 | 0 |  |
| pengdows.crud.types.attributes.SpatialTypeAttribute | 2 | 0 | 2 | 164 | 100% | 0 | 0 |  |
| pengdows.crud.types.CachedParameterConfig | 4 | 0 | 4 | 620 | 100% | 0 | 0 |  |
| pengdows.crud.types.coercion.AdvancedCoercions | 22 | 0 | 22 | 803 | 100% | 0 | 0 |  |
| pengdows.crud.types.coercion.BasicCoercions | 17 | 0 | 17 | 726 | 100% | 0 | 0 |  |
| pengdows.crud.types.coercion.BlobStreamCoercion | 37 | 10 | 47 | 803 | 78.7% | 21 | 26 | 80.7% |
| pengdows.crud.types.coercion.BooleanCoercion | 44 | 2 | 46 | 726 | 95.6% | 49 | 50 | 98% |
| pengdows.crud.types.coercion.ByteArrayCoercion | 20 | 0 | 20 | 726 | 100% | 9 | 10 | 90% |
| pengdows.crud.types.coercion.CidrCoercion | 33 | 27 | 60 | 803 | 55% | 19 | 40 | 47.5% |
| pengdows.crud.types.coercion.ClobStreamCoercion | 30 | 14 | 44 | 803 | 68.1% | 13 | 16 | 81.2% |
| pengdows.crud.types.coercion.CoercionRegistry | 38 | 7 | 45 | 145 | 84.4% | 10 | 12 | 83.3% |
| pengdows.crud.types.coercion.DateTimeCoercion | 30 | 2 | 32 | 726 | 93.7% | 15 | 16 | 93.7% |
| pengdows.crud.types.coercion.DateTimeOffsetCoercion | 15 | 3 | 18 | 726 | 83.3% | 5 | 6 | 83.3% |
| pengdows.crud.types.coercion.DateTimeRangeCoercion | 20 | 2 | 22 | 726 | 90.9% | 3 | 4 | 75% |
| pengdows.crud.types.coercion.DbCoercion<T> | 17 | 4 | 21 | 145 | 80.9% | 7 | 12 | 58.3% |
| pengdows.crud.types.coercion.DbValue | 6 | 0 | 6 | 70 | 100% | 4 | 4 | 100% |
| pengdows.crud.types.coercion.DecimalCoercion | 21 | 0 | 21 | 726 | 100% | 4 | 4 | 100% |
| pengdows.crud.types.coercion.GeographyCoercion | 41 | 23 | 64 | 803 | 64% | 19 | 26 | 73% |
| pengdows.crud.types.coercion.GeometryCoercion | 43 | 21 | 64 | 803 | 67.1% | 20 | 26 | 76.9% |
| pengdows.crud.types.coercion.GuidCoercion | 31 | 0 | 31 | 726 | 100% | 26 | 26 | 100% |
| pengdows.crud.types.coercion.HStoreCoercion | 20 | 2 | 22 | 726 | 90.9% | 3 | 4 | 75% |
| pengdows.crud.types.coercion.InetCoercion | 45 | 20 | 65 | 803 | 69.2% | 22 | 44 | 50% |
| pengdows.crud.types.coercion.IntArrayCoercion | 25 | 3 | 28 | 726 | 89.2% | 7 | 8 | 87.5% |
| pengdows.crud.types.coercion.IntervalDaySecondCoercion | 31 | 15 | 46 | 803 | 67.3% | 14 | 20 | 70% |
| pengdows.crud.types.coercion.IntervalYearMonthCoercion | 27 | 14 | 41 | 803 | 65.8% | 10 | 16 | 62.5% |
| pengdows.crud.types.coercion.IntRangeCoercion | 19 | 3 | 22 | 726 | 86.3% | 3 | 4 | 75% |
| pengdows.crud.types.coercion.JsonDocumentCoercion | 34 | 0 | 34 | 726 | 100% | 16 | 16 | 100% |
| pengdows.crud.types.coercion.JsonElementCoercion | 33 | 0 | 33 | 726 | 100% | 14 | 14 | 100% |
| pengdows.crud.types.coercion.JsonValueCoercion | 20 | 5 | 25 | 726 | 80% | 6 | 8 | 75% |
| pengdows.crud.types.coercion.MacAddressCoercion | 37 | 25 | 62 | 803 | 59.6% | 20 | 34 | 58.8% |
| pengdows.crud.types.coercion.ParameterBindingRules | 128 | 13 | 141 | 497 | 90.7% | 95 | 108 | 87.9% |
| pengdows.crud.types.coercion.PostgreSqlIntervalCoercion | 25 | 1 | 26 | 803 | 96.1% | 13 | 14 | 92.8% |
| pengdows.crud.types.coercion.PostgreSqlRangeDateTimeCoercion | 27 | 13 | 40 | 803 | 67.5% | 8 | 12 | 66.6% |
| pengdows.crud.types.coercion.PostgreSqlRangeIntCoercion | 27 | 13 | 40 | 803 | 67.5% | 8 | 12 | 66.6% |
| pengdows.crud.types.coercion.PostgreSqlRangeLongCoercion | 19 | 21 | 40 | 803 | 47.5% | 8 | 12 | 66.6% |
| pengdows.crud.types.coercion.ProviderParameterFactory | 140 | 12 | 152 | 497 | 92.1% | 110 | 122 | 90.1% |
| pengdows.crud.types.coercion.RowVersionValueCoercion | 45 | 0 | 45 | 803 | 100% | 24 | 24 | 100% |
| pengdows.crud.types.coercion.StringArrayCoercion | 16 | 4 | 20 | 726 | 80% | 5 | 6 | 83.3% |
| pengdows.crud.types.coercion.TimeSpanCoercion | 21 | 0 | 21 | 726 | 100% | 10 | 10 | 100% |
| pengdows.crud.types.converters.AdvancedTypeConverter<T> | 15 | 14 | 29 | 60 | 51.7% | 11 | 16 | 68.7% |
| pengdows.crud.types.converters.BlobStreamConverter | 33 | 0 | 33 | 107 | 100% | 22 | 22 | 100% |
| pengdows.crud.types.converters.CidrConverter | 28 | 26 | 54 | 140 | 51.8% | 8 | 26 | 30.7% |
| pengdows.crud.types.converters.ClobStreamConverter | 21 | 0 | 21 | 94 | 100% | 16 | 16 | 100% |
| pengdows.crud.types.converters.GeographyConverter | 66 | 25 | 91 | 209 | 72.5% | 32 | 38 | 84.2% |
| pengdows.crud.types.converters.GeometryConverter | 48 | 36 | 84 | 193 | 57.1% | 12 | 32 | 37.5% |
| pengdows.crud.types.converters.InetConverter | 33 | 25 | 58 | 144 | 56.8% | 12 | 30 | 40% |
| pengdows.crud.types.converters.IntervalDaySecondConverter | 87 | 15 | 102 | 208 | 85.2% | 57 | 64 | 89% |
| pengdows.crud.types.converters.IntervalYearMonthConverter | 48 | 15 | 63 | 160 | 76.1% | 26 | 34 | 76.4% |
| pengdows.crud.types.converters.MacAddressConverter | 18 | 19 | 37 | 115 | 48.6% | 6 | 18 | 33.3% |
| pengdows.crud.types.converters.PostgreSqlIntervalConverter | 109 | 33 | 142 | 261 | 76.7% | 76 | 92 | 82.6% |
| pengdows.crud.types.converters.PostgreSqlRangeConverter<T> | 68 | 12 | 80 | 208 | 85% | 34 | 54 | 62.9% |
| pengdows.crud.types.converters.RowVersionConverter | 31 | 0 | 31 | 118 | 100% | 16 | 16 | 100% |
| pengdows.crud.types.converters.SpatialConverter<T> | 95 | 45 | 140 | 247 | 67.8% | 66 | 107 | 61.6% |
| pengdows.crud.types.MappingKey | 13 | 0 | 13 | 620 | 100% | 3 | 4 | 75% |
| pengdows.crud.types.ProviderTypeMapping | 2 | 0 | 2 | 620 | 100% | 0 | 0 |  |
| pengdows.crud.types.valueobjects.Cidr | 40 | 22 | 62 | 94 | 64.5% | 16 | 32 | 50% |
| pengdows.crud.types.valueobjects.Geography | 17 | 4 | 21 | 47 | 80.9% | 2 | 4 | 50% |
| pengdows.crud.types.valueobjects.Geometry | 17 | 4 | 21 | 47 | 80.9% | 2 | 4 | 50% |
| pengdows.crud.types.valueobjects.HStore | 141 | 26 | 167 | 279 | 84.4% | 101 | 110 | 91.8% |
| pengdows.crud.types.valueobjects.Inet | 39 | 9 | 48 | 74 | 81.2% | 26 | 38 | 68.4% |
| pengdows.crud.types.valueobjects.IntervalDaySecond | 85 | 3 | 88 | 129 | 96.5% | 59 | 68 | 86.7% |
| pengdows.crud.types.valueobjects.IntervalYearMonth | 48 | 2 | 50 | 84 | 96% | 32 | 38 | 84.2% |
| pengdows.crud.types.valueobjects.JsonValue | 58 | 8 | 66 | 135 | 87.8% | 15 | 24 | 62.5% |
| pengdows.crud.types.valueobjects.MacAddress | 36 | 16 | 52 | 83 | 69.2% | 14 | 24 | 58.3% |
| pengdows.crud.types.valueobjects.PostgreSqlInterval | 19 | 13 | 32 | 53 | 59.3% | 5 | 6 | 83.3% |
| pengdows.crud.types.valueobjects.Range<T> | 67 | 14 | 81 | 121 | 82.7% | 33 | 48 | 68.7% |
| pengdows.crud.types.valueobjects.RowVersion | 26 | 3 | 29 | 55 | 89.6% | 10 | 12 | 83.3% |
| pengdows.crud.types.valueobjects.SpatialValue | 63 | 4 | 67 | 95 | 94% | 22 | 28 | 78.5% |
| pengdows.crud.Utils | 37 | 4 | 41 | 70 | 90.2% | 32 | 34 | 94.1% |
| pengdows.crud.Uuid7Optimized | 220 | 20 | 240 | 473 | 91.6% | 60 | 64 | 93.7% |
| pengdows.crud.Uuid7Options | 7 | 0 | 7 | 473 | 100% | 0 | 0 |  |
| pengdows.crud.wrappers.TrackedConnection | 287 | 28 | 315 | 498 | 91.1% | 113 | 138 | 81.8% |
| pengdows.crud.wrappers.TrackedReader | 224 | 21 | 245 | 348 | 91.4% | 35 | 44 | 79.5% |
| **pengdows.crud.abstractions** | **77** | **13** | **90** | **543** | **85.5%** | **13** | **16** | **81.2%** |
| pengdows.crud.connection.ConnectionLocalState | 22 | 9 | 31 | 72 | 70.9% | 7 | 10 | 70% |
| pengdows.crud.IAuditValues | 0 | 3 | 3 | 27 | 0% | 0 | 0 |  |
| pengdows.crud.IDatabaseContext | 2 | 1 | 3 | 266 | 66.6% | 0 | 0 |  |
| pengdows.crud.isolation.IsolationResolution | 1 | 0 | 1 | 21 | 100% | 0 | 0 |  |
| pengdows.crud.MapperOptions | 6 | 0 | 6 | 17 | 100% | 0 | 0 |  |
| pengdows.crud.metrics.DatabaseMetrics | 24 | 0 | 24 | 54 | 100% | 0 | 0 |  |
| pengdows.crud.metrics.MetricsOptions | 18 | 0 | 18 | 57 | 100% | 6 | 6 | 100% |
| pengdows.crud.TypeCoercionOptions | 2 | 0 | 2 | 12 | 100% | 0 | 0 |  |
| pengdows.crud.TypeMapRegistryExtensions | 2 | 0 | 2 | 17 | 100% | 0 | 0 |  |

