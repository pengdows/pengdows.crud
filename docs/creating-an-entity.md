# Creating an Entity for `EntityHelper`

`EntityHelper<TEntity, TRowID>` uses attributes to map a POCO class to a database table. This guide describes each attribute and shows how to apply them when modelling an entity.

## TableAttribute

Applies to the class. Defines the table name and optional schema.

```csharp
[Table("person", "dbo")]
public class Person { }
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `name` | `string` | Table name. |
| `schema` | `string?` | Optional schema. |

## ColumnAttribute

Marks a property as a column and specifies the database type.

```csharp
[Column("first_name", DbType.String)]
public string FirstName { get; set; } = string.Empty;
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `name` | `string` | Column name. |
| `type` | `DbType` | Database type. |

## IdAttribute

Identifies the primary ID column. Use `writable=false` for auto-generated keys.

```csharp
[Id(writable: false)]
[Column("id", DbType.Int64)]
public long Id { get; set; }
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `writable` | `bool` | If `false`, the column is omitted from INSERT statements. |

## PrimaryKeyAttribute

Marks a property as part of a composite primary key. Order determines key sequence. Cannot be combined with `IdAttribute`.

```csharp
[PrimaryKey(order: 1)]
[Column("tenant_id", DbType.Int32)]
public int TenantId { get; set; }

[PrimaryKey(order: 2)]
[Column("code", DbType.String)]
public string Code { get; set; } = string.Empty;
```

## CreatedOn & CreatedBy

Indicates audit fields set on insert.

```csharp
[CreatedOn]
[Column("created_on", DbType.DateTime2)]
public DateTime CreatedOn { get; set; }

[CreatedBy]
[Column("created_by", DbType.Int64)]
public long CreatedBy { get; set; }
```

## LastUpdatedOn & LastUpdatedBy

Audit fields automatically updated on modification.

```csharp
[LastUpdatedOn]
[Column("last_updated_on", DbType.DateTime2)]
public DateTime? LastUpdatedOn { get; set; }

[LastUpdatedBy]
[Column("last_updated_by", DbType.Int64)]
public long? LastUpdatedBy { get; set; }
```

## VersionAttribute

Marks a concurrency token column.

```csharp
[Version]
[Column("row_version", DbType.Int64)]
public long RowVersion { get; set; }
```

## NonInsertableAttribute & NonUpdateableAttribute

Skip a property during INSERT or UPDATE operations.

```csharp
[NonInsertable]
[Column("generated", DbType.String)]
public string Generated { get; set; } = string.Empty;

[NonUpdateable]
[Column("created_by", DbType.Int64)]
public long CreatedBy { get; set; }
```

## JsonAttribute

Serializes a property to JSON when saving to the database.

```csharp
[Json]
[Column("metadata", DbType.String)]
public Dictionary<string, string> Metadata { get; set; } = new();
```

## EnumColumnAttribute

Maps an enum property to a column when the stored values are numeric.

```csharp
[EnumColumn(typeof(PersonStatus))]
[Column("status", DbType.Int32)]
public PersonStatus Status { get; set; }
```

## Putting It All Together

```csharp
[Table("person", "dbo")]
public class Person
{
    [Id]
    [Column("id", DbType.Int64)]
    public long Id { get; set; }

    [Column("first_name", DbType.String)]
    public string FirstName { get; set; } = string.Empty;

    [Column("last_name", DbType.String)]
    public string LastName { get; set; } = string.Empty;

    [EnumColumn(typeof(PersonStatus))]
    [Column("status", DbType.Int32)]
    public PersonStatus Status { get; set; }

    [Json]
    [Column("metadata", DbType.String)]
    public Dictionary<string, string> Metadata { get; set; } = new();

    [CreatedOn]
    [Column("created_on", DbType.DateTime2)]
    public DateTime CreatedOn { get; set; }

    [CreatedBy]
    [Column("created_by", DbType.Int64)]
    public long CreatedBy { get; set; }

    [LastUpdatedOn]
    [Column("last_updated_on", DbType.DateTime2)]
    public DateTime? LastUpdatedOn { get; set; }

    [LastUpdatedBy]
    [Column("last_updated_by", DbType.Int64)]
    public long? LastUpdatedBy { get; set; }

    [Version]
    [Column("row_version", DbType.Int64)]
    public long RowVersion { get; set; }
}
```

This entity can now be used with `EntityHelper<Person, long>` to generate CRUD statements and map results.

