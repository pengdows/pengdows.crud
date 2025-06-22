# Pengdows.Crud

High-speed lightweight ORM / DB access library.

## Features

- Fast CRUD operations
- Lightweight ORM mapping
- Supports SQLite

## Usage

```csharp
var db = new DbContext("connection string");
var  helper = new EntityHelper<User,int>(db);
helper.
